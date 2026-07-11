using ClinicQueue.Application.Platform.Analytics;
using ClinicQueue.Application.Platform.Export;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Platform;

/// <summary>
/// Platform analytics (FR-104) and clinic-scoped self-service export (FR-105).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class AnalyticsExportTests(Infrastructure.PostgresFixture fixture)
{
    // PrivilegedDbContext (subclass of AppDbContext) so platform handlers requiring
    // IPrivilegedDbContext accept it; still migrates and behaves as an AppDbContext.
    private PrivilegedDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PrivilegedDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new PrivilegedDbContext(options);
    }

    [Fact]
    public async Task Platform_analytics_aggregates_usage_and_revenue_across_clinics()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        SeedTwoClinicsWithActivity(db, clinicA, clinicB);
        await db.SaveChangesAsync();

        var handler = new PlatformAnalyticsHandler(db);
        var result = await handler.HandleAsync(new PlatformAnalyticsQuery());

        result.Usage.ClinicCount.Should().BeGreaterThanOrEqualTo(2);
        result.Usage.AppointmentCount.Should().BeGreaterThanOrEqualTo(2);
        result.Usage.VisitCount.Should().BeGreaterThanOrEqualTo(1);
        result.Revenue.ConfirmedDepositCount.Should().BeGreaterThanOrEqualTo(1);
        result.Revenue.ActiveSubscriptionCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Clinic_export_contains_only_that_clinic_patients_appointments_visits()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var (ownerA, _, visitAId, appointmentBId) = SeedTwoClinicsWithActivity(db, clinicA, clinicB);
        await db.SaveChangesAsync();

        var handler = new ClinicExportHandler(db);
        var started = await handler.StartAsync(new StartClinicExportCommand(clinicA, ownerA));
        started.Status.Should().Be("Completed");

        var payload = await handler.GetPayloadAsync(clinicA, started.ExportJobId);
        payload.Should().NotBeNull();
        payload!.ClinicId.Should().Be(clinicA);
        payload.Patients.Should().NotBeEmpty();
        payload.Appointments.Should().NotBeEmpty();
        payload.Appointments.Should().NotContain(a => a.AppointmentId == appointmentBId);
        payload.Visits.Should().ContainSingle(v => v.VisitId == visitAId);
        payload.Visits.Should().OnlyContain(v => v.VisitId == visitAId);

        // Cross-check: clinic B export must not include clinic A visit.
        var ownerB = await db.ClinicMemberships
            .Where(m => m.ClinicId == clinicB && m.Role == ClinicRole.ClinicOwner)
            .Select(m => m.UserId)
            .FirstAsync();
        var exportB = await handler.StartAsync(new StartClinicExportCommand(clinicB, ownerB));
        var payloadB = await handler.GetPayloadAsync(clinicB, exportB.ExportJobId);
        payloadB!.Visits.Should().NotContain(v => v.VisitId == visitAId);
        payloadB.Appointments.Should().Contain(a => a.AppointmentId == appointmentBId);
    }

    private static (Guid OwnerA, Guid PatientA, Guid VisitAId, Guid AppointmentBId) SeedTwoClinicsWithActivity(
        AppDbContext db,
        Guid clinicA,
        Guid clinicB)
    {
        var ownerA = new User { EmailOrPhone = $"oa-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var ownerB = new User { EmailOrPhone = $"ob-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patientA = new User { EmailOrPhone = $"pa-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var patientB = new User { EmailOrPhone = $"pb-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var doctorA = new User { EmailOrPhone = $"da-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.AddRange(ownerA, ownerB, patientA, patientB, doctorA);

        db.Clinics.AddRange(
            new Clinic
            {
                Id = clinicA,
                Name = "Analytics A",
                Specialty = "GP",
                SubdomainSlug = $"aa-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo",
                Status = ClinicStatus.Active
            },
            new Clinic
            {
                Id = clinicB,
                Name = "Analytics B",
                Specialty = "GP",
                SubdomainSlug = $"ab-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo",
                Status = ClinicStatus.Active
            });

        var memPatientA = new ClinicMembership
        {
            ClinicId = clinicA,
            UserId = patientA.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        var memPatientB = new ClinicMembership
        {
            ClinicId = clinicB,
            UserId = patientB.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.AddRange(
            new ClinicMembership
            {
                ClinicId = clinicA,
                UserId = ownerA.Id,
                Role = ClinicRole.ClinicOwner,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicB,
                UserId = ownerB.Id,
                Role = ClinicRole.ClinicOwner,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicA,
                UserId = doctorA.Id,
                Role = ClinicRole.Doctor,
                Status = MembershipStatus.Active
            },
            memPatientA,
            memPatientB);

        db.Subscriptions.AddRange(
            new Subscription
            {
                ClinicId = clinicA,
                PlanName = "standard",
                Status = SubscriptionStatus.Active,
                RenewalDate = DateTime.UtcNow.AddDays(30)
            },
            new Subscription
            {
                ClinicId = clinicB,
                PlanName = "standard",
                Status = SubscriptionStatus.Expired,
                RenewalDate = DateTime.UtcNow.AddDays(-1)
            });

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessionA = new QueueSession
        {
            ClinicId = clinicA,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        var sessionB = new QueueSession
        {
            ClinicId = clinicB,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.AddRange(sessionA, sessionB);

        var appointmentA = new Appointment
        {
            ClinicId = clinicA,
            QueueSessionId = sessionA.Id,
            PatientMembershipId = memPatientA.Id,
            BookedByUserId = patientA.Id,
            Method = AppointmentMethod.PhoneDeposit,
            DepositConfirmedAt = DateTime.UtcNow,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        };
        var appointmentB = new Appointment
        {
            ClinicId = clinicB,
            QueueSessionId = sessionB.Id,
            PatientMembershipId = memPatientB.Id,
            BookedByUserId = patientB.Id,
            Method = AppointmentMethod.WalkIn,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        };
        db.Appointments.AddRange(appointmentA, appointmentB);

        db.PatientProfiles.Add(new PatientProfile
        {
            ClinicId = clinicA,
            ClinicMembershipId = memPatientA.Id,
            BloodType = "O+",
            Allergies = "none"
        });

        var visitA = new Visit
        {
            ClinicId = clinicA,
            AppointmentId = appointmentA.Id,
            TreatingDoctorUserId = doctorA.Id,
            Diagnosis = "checkup",
            Notes = "ok"
        };
        db.Visits.Add(visitA);

        return (ownerA.Id, patientA.Id, visitA.Id, appointmentB.Id);
    }
}
