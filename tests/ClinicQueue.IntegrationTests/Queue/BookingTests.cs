using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Booking assigns a temporary number and persists BookingConfirmation outbox in the same
/// transaction (FR-021, FR-070). Head-to-clinic transitions are enqueued after persist (FR-071).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class BookingTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static CreateAppointmentHandler CreateHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        var gate = new ClinicAccessGate(db);
        return new CreateAppointmentHandler(
            db,
            new QueueSessionService(db, gate),
            new QueueNotificationRules(db, countAhead),
            gate);
    }

    private static DateOnly ClinicToday(string timezone = "Africa/Cairo") =>
        ClinicClock.Today(timezone);

    [Fact]
    public async Task Booking_assigns_temporary_number_and_booking_confirmation_outbox()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"book-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Booking Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"bk-{Guid.NewGuid():N}"[..20],
            HeadToClinicThreshold = 4,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var day = ClinicToday();

        var result = await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId,
            user.Id,
            day,
            AppointmentMethod.WalkIn,
            PossibleUrgentFlag: false,
            DependentId: null));

        result.TemporaryNumber.Should().Be(1);

        var appointment = await db.Appointments.SingleAsync(a => a.Id == result.AppointmentId);
        appointment.TemporaryNumber.Should().Be(1);
        appointment.Status.Should().Be(AppointmentStatus.Booked);
        appointment.Type.Should().Be(AppointmentType.Normal);
        appointment.RealNumber.Should().BeNull();

        var outbox = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.RecipientUserId == user.Id)
            .ToListAsync();

        outbox.Should().ContainSingle(n => n.Type == NotificationType.BookingConfirmation);
        outbox[0].Status.Should().Be(OutboxNotificationStatus.Pending);
        outbox[0].PayloadJson.Should().Contain(appointment.Id.ToString());
        outbox[0].PayloadJson.Should().Contain("temporaryNumber");
    }

    [Fact]
    public async Task Booking_rejects_duplicate_active_same_day_for_same_patient()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"dup-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Dup Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"dp-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var day = ClinicToday();
        var command = new CreateAppointmentCommand(
            clinicId, user.Id, day, AppointmentMethod.WalkIn, 0, false, null);

        await handler.HandleAsync(command);

        var act = async () => await handler.HandleAsync(command);
        await act.Should().ThrowAsync<DuplicateActiveBookingException>();
    }

    [Fact]
    public async Task Booking_unique_index_rejects_duplicate_active_and_maps_as_23505()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"race-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        var day = ClinicToday();
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Race Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"rc-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var membership = new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        };
        db.ClinicMemberships.Add(membership);
        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        db.Appointments.Add(new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = user.Id,
            TemporaryNumber = 1,
            Status = AppointmentStatus.Booked
        });
        await db.SaveChangesAsync();

        // Bypass the app-level check: insert a second active row for the same membership.
        // The partial unique index must reject it as SQLSTATE 23505 (handler maps this to 409).
        await using var raw = CreateContext();
        raw.Appointments.Add(new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = user.Id,
            TemporaryNumber = 99,
            Status = AppointmentStatus.Booked
        });

        var save = async () => await raw.SaveChangesAsync();
        var ex = await save.Should().ThrowAsync<DbUpdateException>();
        PostgresUniqueViolation.Matches(ex.Which, "active_membership").Should().BeTrue();

        var pg = FindPostgresException(ex.Which);
        pg.Should().NotBeNull();
        pg!.SqlState.Should().Be(PostgresUniqueViolation.SqlState);
    }

    [Fact]
    public async Task Booking_concurrent_duplicate_leaves_single_active_row()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"conc-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        var day = ClinicToday();
        setup.Users.Add(user);
        setup.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Conc Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"cc-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await setup.SaveChangesAsync();
        setup.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        setup.QueueSessions.Add(new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Open
        });
        await setup.SaveChangesAsync();

        var command = new CreateAppointmentCommand(
            clinicId, user.Id, day, AppointmentMethod.WalkIn, 0, false, null);

        async Task<Exception?> AttemptAsync()
        {
            try
            {
                await using var db = CreateContext();
                var handler = CreateHandler(db);
                await handler.HandleAsync(command);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        outcomes.Count(e => e is null).Should().Be(1);
        outcomes.Count(e => e is DuplicateActiveBookingException).Should().Be(1);

        await using var verify = CreateContext();
        var active = await verify.Appointments.CountAsync(a =>
            a.ClinicId == clinicId
            && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Present));
        active.Should().Be(1);
    }

    [Fact]
    public async Task Booking_rejects_day_that_is_not_clinic_today()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"day-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Day Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"dy-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var tomorrow = ClinicToday().AddDays(1);

        var act = async () => await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId, user.Id, tomorrow, AppointmentMethod.WalkIn, 0, false, null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*clinic today*");
    }

    [Fact]
    public async Task Booking_blocked_when_session_is_closed()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"closed-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        var day = ClinicToday();
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Closed Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"cl-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        db.QueueSessions.Add(new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = day,
            Status = QueueSessionStatus.Closed
        });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var act = async () => await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId, user.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    [Fact]
    public async Task Booking_enqueues_head_to_clinic_when_new_booker_is_within_threshold()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"htc-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "HTC Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"ht-{Guid.NewGuid():N}"[..20],
            HeadToClinicThreshold = 4,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var result = await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId,
            user.Id,
            ClinicToday(),
            AppointmentMethod.WalkIn,
            PossibleUrgentFlag: false,
            DependentId: null));

        var outbox = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.RecipientUserId == user.Id)
            .ToListAsync();

        outbox.Should().Contain(n => n.Type == NotificationType.BookingConfirmation);
        outbox.Should().Contain(n => n.Type == NotificationType.HeadToClinic);
        outbox.Single(n => n.Type == NotificationType.HeadToClinic)
            .PayloadJson.Should().Contain(result.AppointmentId.ToString());
        outbox.Single(n => n.Type == NotificationType.HeadToClinic)
            .PayloadJson.Should().Contain("headToClinic");
    }

    [Fact]
    public async Task Booking_does_not_reenqueue_head_to_clinic_for_peers_already_notified()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var firstUser = new User
        {
            EmailOrPhone = $"htc1-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        var secondUser = new User
        {
            EmailOrPhone = $"htc2-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.AddRange(firstUser, secondUser);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "HTC Peer Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"hp-{Guid.NewGuid():N}"[..20],
            HeadToClinicThreshold = 4,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.AddRange(
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = firstUser.Id,
                Role = ClinicRole.Patient,
                Status = MembershipStatus.Active
            },
            new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = secondUser.Id,
                Role = ClinicRole.Patient,
                Status = MembershipStatus.Active
            });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var day = ClinicToday();

        await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId, firstUser.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        await handler.HandleAsync(new CreateAppointmentCommand(
            clinicId, secondUser.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        var firstHeadToClinic = await db.OutboxNotifications.CountAsync(n =>
            n.ClinicId == clinicId
            && n.RecipientUserId == firstUser.Id
            && n.Type == NotificationType.HeadToClinic);

        firstHeadToClinic.Should().Be(1);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
            {
                return pg;
            }
        }

        return null;
    }
}
