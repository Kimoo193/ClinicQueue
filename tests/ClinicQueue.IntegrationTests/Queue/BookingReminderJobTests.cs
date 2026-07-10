using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Jobs;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicQueue.IntegrationTests.Queue;

/// <summary>
/// Morning-of booking-day reminder: enqueued once per Booked appointment during the clinic's
/// local morning window, never duplicated on a later run the same day (FR-048).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class BookingReminderJobTests(Infrastructure.PostgresFixture fixture)
{
    private const string Timezone = "Africa/Cairo";

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static DateTime LocalToUtc(int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 3, 2, hour, 0, 0, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById(Timezone));

    private async Task<(Guid ClinicId, Guid AppointmentId, Guid PatientUserId)> SeedBookedAppointmentAsync(
        AppDbContext db,
        DateOnly operatingDate)
    {
        var clinicId = Guid.NewGuid();
        var patient = new User { EmailOrPhone = $"br-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        db.Users.Add(patient);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Reminder Clinic",
            Specialty = "GP",
            SubdomainSlug = $"br-{Guid.NewGuid():N}"[..20],
            Timezone = Timezone
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
            OperatingDate = operatingDate,
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
            Status = AppointmentStatus.Booked
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (clinicId, appointment.Id, patient.Id);
    }

    [Fact]
    public async Task Enqueues_reminder_during_morning_window_and_stays_idempotent_on_rerun()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var utcMorning = LocalToUtc(hour: 8);
        var operatingDate = ClinicClock.Today(Timezone, utcMorning);
        var (clinicId, _, patientId) = await SeedBookedAppointmentAsync(db, operatingDate);

        var job = new BookingReminderJob(db, NullLogger<BookingReminderJob>.Instance);
        await job.ProcessAsync(utcNow: utcMorning);
        await job.ProcessAsync(utcNow: utcMorning.AddHours(1));

        var reminders = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.Type == NotificationType.BookingReminder)
            .ToListAsync();
        reminders.Should().ContainSingle();
        reminders[0].RecipientUserId.Should().Be(patientId);
    }

    [Fact]
    public async Task Does_not_enqueue_outside_the_local_morning_window()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var utcAfternoon = LocalToUtc(hour: 15);
        var operatingDate = ClinicClock.Today(Timezone, utcAfternoon);
        var (clinicId, _, _) = await SeedBookedAppointmentAsync(db, operatingDate);

        var job = new BookingReminderJob(db, NullLogger<BookingReminderJob>.Instance);
        await job.ProcessAsync(utcNow: utcAfternoon);

        var reminders = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId && n.Type == NotificationType.BookingReminder)
            .ToListAsync();
        reminders.Should().BeEmpty();
    }
}
