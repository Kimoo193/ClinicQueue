using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Parallel confirm-arrival on the same session never yields duplicate real numbers
/// (SC-011, FR-030 — constitution Principle II release gate).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class QueueConcurrencyTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static AssignRealNumberHandler CreateAssignHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        return new AssignRealNumberHandler(
            db,
            new UrgentInjectionService(),
            new QueueNotificationRules(db, countAhead));
    }

    private static CreateAppointmentHandler CreateBookingHandler(AppDbContext db)
    {
        var countAhead = new CountAheadService(db);
        return new CreateAppointmentHandler(
            db,
            new QueueSessionService(db),
            new QueueNotificationRules(db, countAhead));
    }

    private static DateOnly ClinicToday() => ClinicClock.Today("Africa/Cairo");

    [Fact]
    public async Task Parallel_confirm_arrival_never_duplicates_real_numbers()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var day = ClinicToday();
        var users = Enumerable.Range(0, 8).Select(i => new User
        {
            EmailOrPhone = $"conc-{i}-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        }).ToList();

        setup.Users.AddRange(users);
        setup.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Concurrency Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"cq-{Guid.NewGuid():N}"[..20],
            UrgentRatio = 2,
            Timezone = "Africa/Cairo"
        });
        await setup.SaveChangesAsync();

        var memberships = users.Select(u => new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = u.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        }).ToList();
        setup.ClinicMemberships.AddRange(memberships);
        await setup.SaveChangesAsync();

        var book = CreateBookingHandler(setup);
        var appointmentIds = new List<Guid>();
        foreach (var user in users)
        {
            var created = await book.HandleAsync(new CreateAppointmentCommand(
                clinicId, user.Id, day, AppointmentMethod.WalkIn, 0, false, null));
            appointmentIds.Add(created.AppointmentId);
        }

        async Task<(Guid Id, int? Real, Exception? Error)> ConfirmAsync(Guid appointmentId)
        {
            try
            {
                await using var db = CreateContext();
                var handler = CreateAssignHandler(db);
                var result = await handler.HandleAsync(
                    new AssignRealNumberCommand(appointmentId, clinicId));
                return (appointmentId, result.RealNumber, null);
            }
            catch (Exception ex)
            {
                return (appointmentId, null, ex);
            }
        }

        var outcomes = await Task.WhenAll(appointmentIds.Select(ConfirmAsync));
        outcomes.Select(o => o.Error).Should().OnlyContain(e => e == null);

        var realNumbers = outcomes.Select(o => o.Real).Where(n => n.HasValue).Select(n => n!.Value).ToList();
        realNumbers.Should().HaveCount(appointmentIds.Count);
        realNumbers.Should().OnlyHaveUniqueItems();
        realNumbers.Should().BeEquivalentTo(Enumerable.Range(1, appointmentIds.Count));

        await using var verify = CreateContext();
        var persisted = await verify.Appointments
            .Where(a => a.ClinicId == clinicId && a.RealNumber != null)
            .Select(a => a.RealNumber!.Value)
            .ToListAsync();
        persisted.Should().OnlyHaveUniqueItems();
        persisted.Should().HaveCount(appointmentIds.Count);
    }

    [Fact]
    public async Task Confirm_two_normals_then_urgent_interleaves_after_second()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var day = ClinicToday();
        var n1 = new User { EmailOrPhone = $"n1-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        var n2 = new User { EmailOrPhone = $"n2-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        var u1 = new User { EmailOrPhone = $"u1-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        db.Users.AddRange(n1, n2, u1);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Inject Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"ij-{Guid.NewGuid():N}"[..20],
            UrgentRatio = 2,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        db.ClinicMemberships.AddRange(
            new ClinicMembership { ClinicId = clinicId, UserId = n1.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active },
            new ClinicMembership { ClinicId = clinicId, UserId = n2.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active },
            new ClinicMembership { ClinicId = clinicId, UserId = u1.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();

        var book = CreateBookingHandler(db);
        var a1 = await book.HandleAsync(new CreateAppointmentCommand(clinicId, n1.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        var a2 = await book.HandleAsync(new CreateAppointmentCommand(clinicId, n2.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        var au = await book.HandleAsync(new CreateAppointmentCommand(clinicId, u1.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        var urgent = await db.Appointments.FirstAsync(a => a.Id == au.AppointmentId);
        urgent.Type = AppointmentType.Urgent;
        await db.SaveChangesAsync();

        var assign = CreateAssignHandler(db);
        var r1 = await assign.HandleAsync(new AssignRealNumberCommand(a1.AppointmentId, clinicId));
        var r2 = await assign.HandleAsync(new AssignRealNumberCommand(a2.AppointmentId, clinicId));
        var ru = await assign.HandleAsync(new AssignRealNumberCommand(au.AppointmentId, clinicId));

        r1.RealNumber.Should().Be(1);
        r2.RealNumber.Should().Be(2);
        ru.RealNumber.Should().Be(3);
        ru.WaitingForUrgentRatio.Should().BeFalse();

        var session = await db.QueueSessions.SingleAsync(s => s.ClinicId == clinicId);
        session.NormalSinceLastUrgent.Should().Be(0);
    }

    [Fact]
    public async Task Urgent_before_ratio_waits_then_injects_after_second_normal()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var day = ClinicToday();
        var n1 = new User { EmailOrPhone = $"wn1-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        var n2 = new User { EmailOrPhone = $"wn2-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        var u1 = new User { EmailOrPhone = $"wu1-{Guid.NewGuid():N}@t.local", PasswordHash = "x" };
        db.Users.AddRange(n1, n2, u1);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Wait Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"wt-{Guid.NewGuid():N}"[..20],
            UrgentRatio = 2,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();
        db.ClinicMemberships.AddRange(
            new ClinicMembership { ClinicId = clinicId, UserId = n1.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active },
            new ClinicMembership { ClinicId = clinicId, UserId = n2.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active },
            new ClinicMembership { ClinicId = clinicId, UserId = u1.Id, Role = ClinicRole.Patient, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();

        var book = CreateBookingHandler(db);
        var a1 = await book.HandleAsync(new CreateAppointmentCommand(clinicId, n1.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        var au = await book.HandleAsync(new CreateAppointmentCommand(clinicId, u1.Id, day, AppointmentMethod.WalkIn, 0, false, null));
        var a2 = await book.HandleAsync(new CreateAppointmentCommand(clinicId, n2.Id, day, AppointmentMethod.WalkIn, 0, false, null));

        (await db.Appointments.FirstAsync(a => a.Id == au.AppointmentId)).Type = AppointmentType.Urgent;
        await db.SaveChangesAsync();

        var assign = CreateAssignHandler(db);
        var r1 = await assign.HandleAsync(new AssignRealNumberCommand(a1.AppointmentId, clinicId));
        r1.RealNumber.Should().Be(1);

        var ruWait = await assign.HandleAsync(new AssignRealNumberCommand(au.AppointmentId, clinicId));
        ruWait.RealNumber.Should().BeNull();
        ruWait.WaitingForUrgentRatio.Should().BeTrue();
        ruWait.Status.Should().Be(AppointmentStatus.Present);

        var r2 = await assign.HandleAsync(new AssignRealNumberCommand(a2.AppointmentId, clinicId));
        r2.RealNumber.Should().Be(2);

        // Second normal meets ratio → waiting urgent injected as real 3 in same transaction.
        var urgent = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == au.AppointmentId);
        urgent.RealNumber.Should().Be(3);

        var session = await db.QueueSessions.AsNoTracking().SingleAsync(s => s.ClinicId == clinicId);
        session.NormalSinceLastUrgent.Should().Be(0);
    }
}
