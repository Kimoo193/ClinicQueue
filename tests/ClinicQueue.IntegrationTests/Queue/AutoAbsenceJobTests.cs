using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Jobs;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// AutoAbsenceJob runs under BYPASSRLS — isolation must be proven by ClinicId predicates,
/// not by structural uniqueness of QueueSession.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class AutoAbsenceJobTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Guid ClinicId, Guid AppointmentId)> SeedDueInsideAsync(
        AppDbContext db,
        string slugSuffix,
        int timeoutMinutes,
        DateTime calledAtUtc)
    {
        var clinicId = Guid.NewGuid();
        var patient = new User { EmailOrPhone = $"aa-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = $"Absence Clinic {slugSuffix}",
            Specialty = "GP",
            SubdomainSlug = $"aa-{slugSuffix}-{Guid.NewGuid():N}"[..20],
            AutoAbsenceTimeoutMinutes = timeoutMinutes,
            HeadToClinicThreshold = 4
        });
        await db.SaveChangesAsync();

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
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 1,
            RealNumber = 1,
            Status = AppointmentStatus.Inside,
            CalledAt = calledAtUtc
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (clinicId, appointment.Id);
    }

    private static AutoAbsenceJob CreateJob(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        var rules = new QueueNotificationRules(db, countAhead);
        return new AutoAbsenceJob(db, rules, NullLogger<AutoAbsenceJob>.Instance);
    }

    [Fact]
    public async Task ProcessClinicAsync_for_clinic_A_leaves_also_due_clinic_B_untouched()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        // Both clinics are due for absence (same timeout / CalledAt). Invoking only A's
        // ProcessClinicAsync path must not mutate B — proves ClinicId-scoped isolation under BYPASSRLS.
        var (clinicA, apptA) = await SeedDueInsideAsync(db, "a", timeoutMinutes: 15, calledAtUtc: now.AddMinutes(-20));
        var (clinicB, apptB) = await SeedDueInsideAsync(db, "b", timeoutMinutes: 15, calledAtUtc: now.AddMinutes(-20));

        clinicA.Should().NotBe(clinicB);

        var job = CreateJob(db);
        await job.ProcessClinicAsync(clinicA, utcNow: now);

        var statusA = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == apptA && a.ClinicId == clinicA)
            .Select(a => a.Status)
            .SingleAsync();
        var statusB = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == apptB && a.ClinicId == clinicB)
            .Select(a => a.Status)
            .SingleAsync();

        statusA.Should().Be(AppointmentStatus.Absent);
        statusB.Should().Be(AppointmentStatus.Inside);
    }

    [Fact]
    public async Task ProcessAsync_two_clinics_both_due_each_marked_absent_independently()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var (clinicA, apptA) = await SeedDueInsideAsync(db, "c", timeoutMinutes: 10, calledAtUtc: now.AddMinutes(-30));
        var (clinicB, apptB) = await SeedDueInsideAsync(db, "d", timeoutMinutes: 10, calledAtUtc: now.AddMinutes(-30));

        var job = CreateJob(db);
        await job.ProcessAsync(utcNow: now);

        var statuses = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == apptA || a.Id == apptB)
            .Select(a => new { a.Id, a.ClinicId, a.Status })
            .ToListAsync();

        statuses.Should().HaveCount(2);
        statuses.Should().OnlyContain(s => s.Status == AppointmentStatus.Absent);
        statuses.Select(s => s.ClinicId).Should().BeEquivalentTo([clinicA, clinicB]);
    }
}
