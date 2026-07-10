using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Medical;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Platform;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Domain.Reporting;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Isolation;

/// <summary>
/// Tenant-isolation release gate (constitution Principle I / SC-005 / T080).
/// Covers every clinic-scoped entity: Clinic A never reads Clinic B rows via
/// <see cref="BaseRepository{T}"/>, including under a tampered tenant claim.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class TenantIsolationTests(Infrastructure.PostgresFixture fixture)
{
    private sealed class FixedTenantProvider(Guid? clinicId) : ITenantProvider
    {
        public Guid? ClinicId { get; } = clinicId;
        public bool HasTenant => ClinicId.HasValue;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private sealed record SeededTenants(
        Guid ClinicA,
        Guid ClinicB,
        Guid MembershipAId,
        Guid MembershipBId,
        Guid UserAId,
        Guid UserBId,
        Guid SessionAId,
        Guid SessionBId,
        Guid AppointmentAId,
        Guid AppointmentBId,
        Guid VisitAId,
        Guid VisitBId,
        Guid PrescriptionAId,
        Guid PrescriptionBId,
        Guid ProfileAId,
        Guid ProfileBId,
        Guid DependentAId,
        Guid DependentBId,
        Guid SubscriptionAId,
        Guid SubscriptionBId,
        Guid AuditAId,
        Guid AuditBId,
        Guid OutboxAId,
        Guid OutboxBId,
        Guid PushAId,
        Guid PushBId,
        Guid ExportAId,
        Guid ExportBId,
        Guid RatingAId,
        Guid RatingBId);

    private async Task<SeededTenants> SeedTwoClinicsAsync(AppDbContext setup)
    {
        await setup.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var userA = new User { EmailOrPhone = $"iso-a-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var userB = new User { EmailOrPhone = $"iso-b-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        setup.Users.AddRange(userA, userB);
        setup.Clinics.AddRange(
            new Clinic
            {
                Id = clinicA,
                Name = "Iso A",
                Specialty = "OB",
                SubdomainSlug = $"iso-a-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo"
            },
            new Clinic
            {
                Id = clinicB,
                Name = "Iso B",
                Specialty = "OB",
                SubdomainSlug = $"iso-b-{Guid.NewGuid():N}"[..20],
                Timezone = "Africa/Cairo"
            });
        await setup.SaveChangesAsync();

        var membershipA = new ClinicMembership
        {
            ClinicId = clinicA,
            UserId = userA.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        var membershipB = new ClinicMembership
        {
            ClinicId = clinicB,
            UserId = userB.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        setup.ClinicMemberships.AddRange(membershipA, membershipB);

        var subscriptionA = new Subscription
        {
            ClinicId = clinicA,
            PlanName = "standard",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30)
        };
        var subscriptionB = new Subscription
        {
            ClinicId = clinicB,
            PlanName = "standard",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30)
        };
        setup.Subscriptions.AddRange(subscriptionA, subscriptionB);

        var dependentA = new Dependent
        {
            ClinicId = clinicA,
            GuardianUserId = userA.Id,
            Name = "Child A",
            DateOfBirth = new DateOnly(2018, 1, 1),
            RelationshipToGuardian = "child"
        };
        var dependentB = new Dependent
        {
            ClinicId = clinicB,
            GuardianUserId = userB.Id,
            Name = "Child B",
            DateOfBirth = new DateOnly(2018, 1, 1),
            RelationshipToGuardian = "child"
        };
        setup.Dependents.AddRange(dependentA, dependentB);

        var sessionA = new QueueSession
        {
            ClinicId = clinicA,
            OperatingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = QueueSessionStatus.Open
        };
        var sessionB = new QueueSession
        {
            ClinicId = clinicB,
            OperatingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = QueueSessionStatus.Open
        };
        setup.QueueSessions.AddRange(sessionA, sessionB);
        await setup.SaveChangesAsync();

        var appointmentA = new Appointment
        {
            ClinicId = clinicA,
            QueueSessionId = sessionA.Id,
            PatientMembershipId = membershipA.Id,
            BookedByUserId = userA.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked,
            Method = AppointmentMethod.WalkIn
        };
        var appointmentB = new Appointment
        {
            ClinicId = clinicB,
            QueueSessionId = sessionB.Id,
            PatientMembershipId = membershipB.Id,
            BookedByUserId = userB.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked,
            Method = AppointmentMethod.WalkIn
        };
        setup.Appointments.AddRange(appointmentA, appointmentB);

        var profileA = new PatientProfile
        {
            ClinicId = clinicA,
            ClinicMembershipId = membershipA.Id,
            BloodType = "A+"
        };
        var profileB = new PatientProfile
        {
            ClinicId = clinicB,
            ClinicMembershipId = membershipB.Id,
            BloodType = "B+"
        };
        setup.PatientProfiles.AddRange(profileA, profileB);

        var auditA = new AuditLog
        {
            ClinicId = clinicA,
            ActorUserId = userA.Id,
            Action = "read",
            EntityType = "Visit",
            Timestamp = DateTime.UtcNow
        };
        var auditB = new AuditLog
        {
            ClinicId = clinicB,
            ActorUserId = userB.Id,
            Action = "read",
            EntityType = "Visit",
            Timestamp = DateTime.UtcNow
        };
        setup.AuditLogs.AddRange(auditA, auditB);

        var outboxA = new OutboxNotification
        {
            ClinicId = clinicA,
            RecipientUserId = userA.Id,
            Type = NotificationType.BookingConfirmation,
            PayloadJson = "{}"
        };
        var outboxB = new OutboxNotification
        {
            ClinicId = clinicB,
            RecipientUserId = userB.Id,
            Type = NotificationType.BookingConfirmation,
            PayloadJson = "{}"
        };
        setup.OutboxNotifications.AddRange(outboxA, outboxB);

        var pushA = new PushSubscription
        {
            ClinicId = clinicA,
            UserId = userA.Id,
            Endpoint = $"https://push.test/{Guid.NewGuid():N}",
            P256dh = "p256dh-a",
            Auth = "auth-a"
        };
        var pushB = new PushSubscription
        {
            ClinicId = clinicB,
            UserId = userB.Id,
            Endpoint = $"https://push.test/{Guid.NewGuid():N}",
            P256dh = "p256dh-b",
            Auth = "auth-b"
        };
        setup.PushSubscriptions.AddRange(pushA, pushB);

        var exportA = new ClinicExportJob
        {
            ClinicId = clinicA,
            RequestedByUserId = userA.Id,
            Status = ClinicExportJobStatus.Pending
        };
        var exportB = new ClinicExportJob
        {
            ClinicId = clinicB,
            RequestedByUserId = userB.Id,
            Status = ClinicExportJobStatus.Pending
        };
        setup.ClinicExportJobs.AddRange(exportA, exportB);
        await setup.SaveChangesAsync();

        var visitA = new Visit
        {
            ClinicId = clinicA,
            AppointmentId = appointmentA.Id,
            TreatingDoctorUserId = userA.Id,
            Diagnosis = "dx-a",
            Notes = "notes-a"
        };
        var visitB = new Visit
        {
            ClinicId = clinicB,
            AppointmentId = appointmentB.Id,
            TreatingDoctorUserId = userB.Id,
            Diagnosis = "dx-b",
            Notes = "notes-b"
        };
        setup.Visits.AddRange(visitA, visitB);
        await setup.SaveChangesAsync();

        var prescriptionA = new Prescription
        {
            ClinicId = clinicA,
            VisitId = visitA.Id,
            Drug = "drug-a",
            Dose = "1mg",
            Frequency = "qd",
            Duration = "7d"
        };
        var prescriptionB = new Prescription
        {
            ClinicId = clinicB,
            VisitId = visitB.Id,
            Drug = "drug-b",
            Dose = "2mg",
            Frequency = "bid",
            Duration = "5d"
        };
        setup.Prescriptions.AddRange(prescriptionA, prescriptionB);
        await setup.SaveChangesAsync();

        var ratingA = new Rating
        {
            ClinicId = clinicA,
            AppointmentId = appointmentA.Id,
            Score = 5,
            Comment = "a"
        };
        var ratingB = new Rating
        {
            ClinicId = clinicB,
            AppointmentId = appointmentB.Id,
            Score = 4,
            Comment = "b"
        };
        setup.Ratings.AddRange(ratingA, ratingB);
        await setup.SaveChangesAsync();

        return new SeededTenants(
            clinicA,
            clinicB,
            membershipA.Id,
            membershipB.Id,
            userA.Id,
            userB.Id,
            sessionA.Id,
            sessionB.Id,
            appointmentA.Id,
            appointmentB.Id,
            visitA.Id,
            visitB.Id,
            prescriptionA.Id,
            prescriptionB.Id,
            profileA.Id,
            profileB.Id,
            dependentA.Id,
            dependentB.Id,
            subscriptionA.Id,
            subscriptionB.Id,
            auditA.Id,
            auditB.Id,
            outboxA.Id,
            outboxB.Id,
            pushA.Id,
            pushB.Id,
            exportA.Id,
            exportB.Id,
            ratingA.Id,
            ratingB.Id);
    }

    private static async Task AssertTenantScopedAsync<T>(
        AppDbContext tenantContext,
        Guid clinicA,
        Guid clinicB,
        Guid entityAId,
        Guid entityBId)
        where T : class, IClinicScoped
    {
        var repoA = new BaseRepository<T>(tenantContext, new FixedTenantProvider(clinicA));
        var visible = await repoA.QueryTenantScoped().ToListAsync();
        visible.Should().OnlyContain(e => e.ClinicId == clinicA);

        var ids = visible.OfType<BaseEntity>().Select(e => e.Id).ToArray();
        ids.Should().Contain(entityAId);
        ids.Should().NotContain(entityBId);

        var repoTampered = new BaseRepository<T>(tenantContext, new FixedTenantProvider(clinicB));
        var leaked = await repoTampered.GetByIdAsync(entityAId);
        leaked.Should().BeNull(
            because: $"{typeof(T).Name} belonging to clinic A must not be readable under clinic B tenant");
    }

    [Fact]
    public async Task BaseRepository_isolates_every_clinic_scoped_entity()
    {
        await using var setup = CreateContext();
        var seed = await SeedTwoClinicsAsync(setup);

        await using var tenantContext = CreateContext();

        await AssertTenantScopedAsync<ClinicMembership>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.MembershipAId, seed.MembershipBId);
        await AssertTenantScopedAsync<Subscription>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.SubscriptionAId, seed.SubscriptionBId);
        await AssertTenantScopedAsync<Dependent>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.DependentAId, seed.DependentBId);
        await AssertTenantScopedAsync<AuditLog>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.AuditAId, seed.AuditBId);
        await AssertTenantScopedAsync<OutboxNotification>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.OutboxAId, seed.OutboxBId);
        await AssertTenantScopedAsync<PushSubscription>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.PushAId, seed.PushBId);
        await AssertTenantScopedAsync<QueueSession>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.SessionAId, seed.SessionBId);
        await AssertTenantScopedAsync<Appointment>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.AppointmentAId, seed.AppointmentBId);
        await AssertTenantScopedAsync<PatientProfile>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.ProfileAId, seed.ProfileBId);
        await AssertTenantScopedAsync<Visit>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.VisitAId, seed.VisitBId);
        await AssertTenantScopedAsync<Prescription>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.PrescriptionAId, seed.PrescriptionBId);
        await AssertTenantScopedAsync<ClinicExportJob>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.ExportAId, seed.ExportBId);
        await AssertTenantScopedAsync<Rating>(
            tenantContext, seed.ClinicA, seed.ClinicB, seed.RatingAId, seed.RatingBId);
    }

    [Fact]
    public async Task Tampered_clinic_id_claim_does_not_grant_cross_tenant_reads()
    {
        await using var setup = CreateContext();
        var seed = await SeedTwoClinicsAsync(setup);

        await using var tamperedContext = CreateContext();
        var repo = new BaseRepository<ClinicMembership>(
            tamperedContext,
            new FixedTenantProvider(seed.ClinicB));
        var leaked = await repo.GetByIdAsync(seed.MembershipAId);

        leaked.Should().BeNull();
    }

    [Fact]
    public async Task All_clinic_scoped_entity_types_are_covered_by_isolation_sweep()
    {
        // Guard against new ClinicScopedEntity types shipping without T080 coverage.
        var domainAssembly = typeof(ClinicScopedEntity).Assembly;
        var clinicScopedTypes = domainAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IClinicScoped)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        string[] expected =
        [
            nameof(Appointment),
            nameof(AuditLog),
            nameof(ClinicExportJob),
            nameof(ClinicMembership),
            nameof(Dependent),
            nameof(OutboxNotification),
            nameof(PatientProfile),
            nameof(Prescription),
            nameof(PushSubscription),
            nameof(QueueSession),
            nameof(Rating),
            nameof(Subscription),
            nameof(Visit)
        ];

        clinicScopedTypes.Should().BeEquivalentTo(expected);
    }
}
