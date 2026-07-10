using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.DayClose;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Auditing;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Day close lists unserved patients and auto-carries deposit no-shows; carry-over places
/// patients at the front of the next day preserving prior-day relative order (FR-060–FR-065).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class CarryOverTests(Infrastructure.PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private CloseDaySessionHandler CloseHandler(AppDbContext db) =>
        new(db, new CarryOverService(db), new EfAuditWriter(db), new ClinicAccessGate(db));

    private static CarryOverHandler CarryHandler(AppDbContext db) =>
        new(db, new CarryOverService(db));

    [Fact]
    public async Task Close_auto_carries_deposit_no_show_then_manual_carry_leads_next_day_in_prior_order()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var seed = await SeedClosingSessionAsync(db);
        var actor = Guid.NewGuid();

        // Close: deposit no-show (patient C) auto-carries; A, B, D remain to decide manually.
        var closed = await CloseHandler(db).HandleAsync(
            new CloseDaySessionCommand(seed.ClinicId, seed.SessionId, actor));

        closed.Status.Should().Be(nameof(QueueSessionStatus.Closed));
        closed.AutoCarriedOverCount.Should().Be(1);
        closed.UnservedAppointments.Select(u => u.AppointmentId)
            .Should().BeEquivalentTo(new[] { seed.ApptA, seed.ApptB, seed.ApptD });

        // Carry the remainder out of prior-day order to prove ordering is standing-based, not click-based.
        await CarryHandler(db).CarryOverAsync(new CarryOverAppointmentCommand(seed.ClinicId, seed.ApptD, actor));
        await CarryHandler(db).CarryOverAsync(new CarryOverAppointmentCommand(seed.ClinicId, seed.ApptA, actor));
        await CarryHandler(db).CarryOverAsync(new CarryOverAppointmentCommand(seed.ClinicId, seed.ApptB, actor));

        // Next-day queue: A(1), B(2), C(3), D(4) — prior-day order preserved, ahead of new bookings.
        var nextSession = await db.QueueSessions.AsNoTracking()
            .SingleAsync(s => s.ClinicId == seed.ClinicId && s.OperatingDate == Day.AddDays(1));

        var nextDay = await db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == nextSession.Id)
            .OrderBy(a => a.TemporaryNumber)
            .ToListAsync();

        nextDay.Should().HaveCount(4);
        nextDay.Should().OnlyContain(a => a.Status == AppointmentStatus.Booked && a.RealNumber == null);
        nextDay.Select(a => a.TemporaryNumber).Should().Equal(1, 2, 3, 4);
        nextDay.Select(a => a.PatientMembershipId).Should().Equal(
            seed.MembershipA, seed.MembershipB, seed.MembershipC, seed.MembershipD);

        // Sources are now CarriedOver; a new next-day booking would land after the carried band.
        var sources = await db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == seed.SessionId
                        && new[] { seed.ApptA, seed.ApptB, seed.ApptC, seed.ApptD }.Contains(a.Id))
            .ToListAsync();
        sources.Should().OnlyContain(a => a.Status == AppointmentStatus.CarriedOver);
        nextDay.Max(a => a.TemporaryNumber).Should().Be(4);

        var carryNotifications = await db.OutboxNotifications.AsNoTracking()
            .CountAsync(n => n.ClinicId == seed.ClinicId && n.Type == NotificationType.CarryOver);
        carryNotifications.Should().Be(4);
    }

    private async Task<Seed> SeedClosingSessionAsync(AppDbContext db)
    {
        var clinicId = Guid.NewGuid();
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Carry Clinic",
            Specialty = "GP",
            SubdomainSlug = $"co-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });

        var session = new QueueSession { ClinicId = clinicId, OperatingDate = Day, Status = QueueSessionStatus.Open };
        db.QueueSessions.Add(session);
        await db.SaveChangesAsync();

        var (memA, apptA) = await AddPatientAppointmentAsync(db, clinicId, session.Id, temp: 1, realNumber: 1,
            AppointmentStatus.Present, AppointmentMethod.WalkIn);
        var (memB, apptB) = await AddPatientAppointmentAsync(db, clinicId, session.Id, temp: 2, realNumber: 2,
            AppointmentStatus.Absent, AppointmentMethod.WalkIn);
        var (memC, apptC) = await AddPatientAppointmentAsync(db, clinicId, session.Id, temp: 5, realNumber: null,
            AppointmentStatus.Booked, AppointmentMethod.PhoneDeposit, depositConfirmed: true);
        var (memD, apptD) = await AddPatientAppointmentAsync(db, clinicId, session.Id, temp: 6, realNumber: null,
            AppointmentStatus.Booked, AppointmentMethod.WalkIn);

        // A served patient must never carry over.
        await AddPatientAppointmentAsync(db, clinicId, session.Id, temp: 3, realNumber: 3,
            AppointmentStatus.Completed, AppointmentMethod.WalkIn);

        return new Seed(clinicId, session.Id, memA, memB, memC, memD, apptA, apptB, apptC, apptD);
    }

    private static async Task<(Guid MembershipId, Guid AppointmentId)> AddPatientAppointmentAsync(
        AppDbContext db,
        Guid clinicId,
        Guid sessionId,
        int temp,
        int? realNumber,
        AppointmentStatus status,
        AppointmentMethod method,
        bool depositConfirmed = false)
    {
        var user = new User { EmailOrPhone = $"p-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(user);
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

        var appointment = new Appointment
        {
            ClinicId = clinicId,
            QueueSessionId = sessionId,
            PatientMembershipId = membership.Id,
            BookedByUserId = user.Id,
            TemporaryNumber = temp,
            RealNumber = realNumber,
            Status = status,
            Method = method,
            DepositConfirmedAt = depositConfirmed ? DateTime.UtcNow : null
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (membership.Id, appointment.Id);
    }

    private sealed record Seed(
        Guid ClinicId,
        Guid SessionId,
        Guid MembershipA,
        Guid MembershipB,
        Guid MembershipC,
        Guid MembershipD,
        Guid ApptA,
        Guid ApptB,
        Guid ApptC,
        Guid ApptD);
}
