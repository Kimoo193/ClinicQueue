using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Application.Reporting;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Reporting;

/// <summary>Clinic volume and absence-rate report calculations (FR-090, FR-091).</summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class ReportsTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Volume_and_absence_rate_reflect_seeded_period_activity()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        SeedClinicActivity(db, clinicId);
        await db.SaveChangesAsync();

        var handler = new ClinicReportsHandler(db);
        var from = DateTime.UtcNow.AddDays(-10);
        var to = DateTime.UtcNow.AddDays(1);

        var volume = await handler.GetVolumeAsync(new ReportPeriodQuery(clinicId, from, to, "daily"));
        volume.Series.Should().NotBeEmpty();
        volume.Series.Sum(p => p.Count).Should().Be(5);

        var absence = await handler.GetAbsenceRateAsync(clinicId, from, to);
        // 2 absent + 2 completed → 2/4 = 0.5 (carried-over-with-CalledAt counts as absent)
        absence.AbsentCount.Should().Be(2);
        absence.CompletedCount.Should().Be(2);
        absence.AbsenceRate.Should().BeApproximately(0.5, 0.001);

        var revenue = await handler.GetDepositRevenueAsync(clinicId, from, to);
        revenue.DepositRevenue.Should().Be(1);

        var peaks = await handler.GetPeakHoursAsync(clinicId, from, to);
        peaks.PeakHours.Should().NotBeEmpty();
        peaks.PeakDays.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Submit_rating_updates_clinic_aggregate_and_search()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var (patientId, appointmentId) = SeedCompletedVisit(db, clinicId);
        await db.SaveChangesAsync();

        var audit = new NoOpAuditWriter();
        var submit = new SubmitRatingHandler(db, audit);
        var result = await submit.HandleAsync(
            new SubmitRatingCommand(clinicId, appointmentId, patientId, 4, "Good visit"));

        result.RatingId.Should().NotBeEmpty();

        var clinic = await db.Clinics.AsNoTracking().SingleAsync(c => c.Id == clinicId);
        clinic.AggregateRating.Should().BeApproximately(4.0, 0.001);

        var search = new ClinicQueue.Application.Queue.Booking.ClinicSearchHandler(db);
        var hits = await search.HandleAsync(
            new ClinicQueue.Application.Queue.Booking.ClinicSearchQuery(Name: "Rating Clinic"));
        hits.Should().Contain(h => h.ClinicId == clinicId && h.AggregateRating == 4.0);
    }

    private static void SeedClinicActivity(AppDbContext db, Guid clinicId)
    {
        var owner = new User { EmailOrPhone = $"own-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patient = new User { EmailOrPhone = $"pat-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.AddRange(owner, patient);

        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Reports Clinic",
            Specialty = "GP",
            SubdomainSlug = $"rc-{Guid.NewGuid():N}"[..20],
            Status = ClinicStatus.Active
        });

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = QueueSessionStatus.Closed
        };
        db.QueueSessions.Add(session);

        var temp = 1;
        void AddAppt(AppointmentStatus status, AppointmentMethod method, DateTime? depositAt = null, DateTime? calledAt = null)
        {
            db.Appointments.Add(new Appointment
            {
                ClinicId = clinicId,
                QueueSessionId = session.Id,
                PatientMembershipId = membership.Id,
                BookedByUserId = patient.Id,
                TemporaryNumber = temp++,
                Status = status,
                Method = method,
                DepositConfirmedAt = depositAt,
                CalledAt = calledAt
            });
        }

        AddAppt(AppointmentStatus.Completed, AppointmentMethod.WalkIn);
        AddAppt(AppointmentStatus.Completed, AppointmentMethod.WalkIn);
        AddAppt(AppointmentStatus.Absent, AppointmentMethod.WalkIn);
        AddAppt(AppointmentStatus.CarriedOver, AppointmentMethod.WalkIn, calledAt: DateTime.UtcNow.AddHours(-2));
        AddAppt(
            AppointmentStatus.Booked,
            AppointmentMethod.PhoneDeposit,
            depositAt: DateTime.UtcNow.AddDays(-1));
    }

    private static (Guid PatientId, Guid AppointmentId) SeedCompletedVisit(AppDbContext db, Guid clinicId)
    {
        var patient = new User { EmailOrPhone = $"rp-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(patient);

        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Rating Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"rt-{Guid.NewGuid():N}"[..20],
            Status = ClinicStatus.Active
        });

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 1,
            RealNumber = 1,
            Status = AppointmentStatus.Completed,
            Method = AppointmentMethod.WalkIn
        };
        db.Appointments.Add(appointment);

        return (patient.Id, appointment.Id);
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public void Append(
            Guid clinicId,
            Guid? actorUserId,
            string action,
            string entityType,
            Guid? entityId,
            string? metadataJson = null)
        {
        }
    }
}
