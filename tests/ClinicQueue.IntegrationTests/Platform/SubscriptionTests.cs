using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Platform.Clinics;
using ClinicQueue.Application.Platform.Subscriptions;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Platform;

/// <summary>
/// Suspended clinic blocks staff ops; expired subscription blocks new bookings/sessions
/// but lets an already-open session finish (FR-100, FR-102).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class SubscriptionTests(Infrastructure.PostgresFixture fixture)
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

    private static ClinicAccessGate Gate(AppDbContext db) => new(db);

    private static CreateAppointmentHandler BookingHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        var gate = Gate(db);
        return new CreateAppointmentHandler(
            db,
            new QueueSessionService(db, gate),
            new QueueNotificationRules(db, countAhead),
            gate);
    }

    private static AssignRealNumberHandler AssignHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        return new AssignRealNumberHandler(
            db,
            new UrgentInjectionService(),
            new QueueNotificationRules(db, countAhead),
            Gate(db));
    }

    private static DateOnly ClinicToday() => ClinicClock.Today("Africa/Cairo");

    [Fact]
    public async Task Suspended_clinic_blocks_staff_ops_and_reactivate_restores()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var patient = new User { EmailOrPhone = $"p-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Suspend Me",
            Specialty = "GP",
            SubdomainSlug = $"su-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo",
            Status = ClinicStatus.Active
        });
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            ClinicId = clinicId,
            PlanName = "standard",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30)
        });
        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);

        var day = ClinicToday();
        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var booked = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        };
        db.Appointments.Add(booked);
        await db.SaveChangesAsync();

        // Suspend via Platform handler (FR-100).
        var suspend = await new SuspendClinicHandler(db).HandleAsync(clinicId);
        suspend.Status.Should().Be(nameof(ClinicStatus.Suspended));

        var assignAct = async () => await AssignHandler(db).HandleAsync(
            new AssignRealNumberCommand(booked.Id, clinicId));
        await assignAct.Should().ThrowAsync<ClinicSuspendedException>();

        var bookAct = async () => await BookingHandler(db).HandleAsync(new CreateAppointmentCommand(
            clinicId, patient.Id, day, AppointmentMethod.WalkIn, false, null));
        await bookAct.Should().ThrowAsync<ClinicSuspendedException>();

        // Reactivate — staff ops resume.
        var activate = await new ActivateClinicHandler(db).HandleAsync(clinicId);
        activate.Status.Should().Be(nameof(ClinicStatus.Active));

        var result = await AssignHandler(db).HandleAsync(
            new AssignRealNumberCommand(booked.Id, clinicId));
        result.RealNumber.Should().NotBeNull();
    }

    [Fact]
    public async Task Expired_subscription_blocks_new_booking_and_session_but_open_session_continues()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var patient = new User { EmailOrPhone = $"e-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Expired Sub Clinic",
            Specialty = "GP",
            SubdomainSlug = $"ex-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            ClinicId = clinicId,
            PlanName = "standard",
            Status = SubscriptionStatus.Expired,
            RenewalDate = DateTime.UtcNow.AddDays(-1)
        });
        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = patient.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);

        var day = ClinicToday();
        // Already-open session (grace for FR-102).
        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var booked = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = patient.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        };
        db.Appointments.Add(booked);
        await db.SaveChangesAsync();

        // New booking blocked.
        var bookAct = async () => await BookingHandler(db).HandleAsync(new CreateAppointmentCommand(
            clinicId, patient.Id, day, AppointmentMethod.WalkIn, false, null));
        await bookAct.Should().ThrowAsync<SubscriptionExpiredException>();

        // New session creation blocked (different day with no existing session).
        var otherDay = day.AddDays(1);
        var openAct = async () => await new QueueSessionService(db, Gate(db))
            .GetOrOpenForBookingAsync(clinicId, otherDay);
        await openAct.Should().ThrowAsync<SubscriptionExpiredException>();

        // Already-open session: confirm-arrival / numbering still works.
        var assign = await AssignHandler(db).HandleAsync(
            new AssignRealNumberCommand(booked.Id, clinicId));
        assign.RealNumber.Should().Be(1);

        // Reads preserved — list clinics / session still readable.
        var listed = await new ListAdminClinicsHandler(db).HandleAsync(new ListAdminClinicsQuery());
        listed.Items.Should().Contain(i => i.ClinicId == clinicId
            && i.SubscriptionStatus == nameof(SubscriptionStatus.Expired));
    }

    [Fact]
    public async Task Update_subscription_and_renewal_notice_job_enqueue_owner_notification()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var owner = new User { EmailOrPhone = $"o-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(owner);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Renewal Clinic",
            Specialty = "GP",
            SubdomainSlug = $"rn-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = owner.Id,
            Role = ClinicRole.ClinicOwner,
            Status = MembershipStatus.Active
        });

        var renewal = DateTime.UtcNow.Date.AddDays(7).AddHours(12);
        var updated = await new UpdateSubscriptionHandler(db).HandleAsync(
            new UpdateSubscriptionCommand(clinicId, "pro", renewal, SubscriptionStatus.Active));
        updated.PlanName.Should().Be("pro");
        updated.Status.Should().Be(nameof(SubscriptionStatus.Active));

        await new SubscriptionLifecycleService(db).ProcessRenewalsAsync();

        var notice = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.Type == NotificationType.SubscriptionExpiring)
            .ToListAsync();
        notice.Should().ContainSingle();
        notice[0].RecipientUserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Lifecycle_marks_past_due_active_subscription_expired()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Past Due",
            Specialty = "GP",
            SubdomainSlug = $"pd-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            ClinicId = clinicId,
            PlanName = "standard",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();

        await new SubscriptionLifecycleService(db).ProcessRenewalsAsync();

        var sub = await db.Subscriptions.SingleAsync(s => s.ClinicId == clinicId);
        sub.Status.Should().Be(SubscriptionStatus.Expired);
    }
}
