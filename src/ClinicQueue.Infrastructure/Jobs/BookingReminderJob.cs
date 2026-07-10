using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Morning-of booking-day reminder: enqueues OutboxNotification rows requesting confirmation
/// for Booked appointments on the clinic's local calendar day (FR-048).
/// </summary>
public sealed class BookingReminderJob
{
    /// <summary>Local clinic hours (inclusive start, exclusive end) treated as "morning".</summary>
    private const int MorningHourStart = 6;
    private const int MorningHourEnd = 12;

    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAppDbContext _db;
    private readonly ILogger<BookingReminderJob> _logger;

    public BookingReminderJob(IAppDbContext db, ILogger<BookingReminderJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <param name="utcNow">Overrides the current instant for deterministic testing; defaults to
    /// <see cref="DateTime.UtcNow"/>, matching <see cref="ClinicClock.Today"/>'s convention.</param>
    public async Task ProcessAsync(CancellationToken cancellationToken = default, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var clinics = await _db.Clinics.AsNoTracking()
            .Select(c => new { c.Id, c.Timezone })
            .ToListAsync(cancellationToken);

        foreach (var clinic in clinics)
        {
            if (!IsMorningLocal(clinic.Timezone, now))
            {
                continue;
            }

            var today = ClinicClock.Today(clinic.Timezone, now);
            await EnqueueRemindersForClinicAsync(clinic.Id, today, now, cancellationToken);
        }
    }

    private async Task EnqueueRemindersForClinicAsync(
        Guid clinicId,
        DateOnly operatingDate,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var session = await _db.QueueSessions.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.ClinicId == clinicId && s.OperatingDate == operatingDate,
                cancellationToken);

        if (session is null)
        {
            return;
        }

        var booked = await _db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == session.Id && a.Status == AppointmentStatus.Booked)
            .Select(a => new { a.Id, a.BookedByUserId, a.TemporaryNumber })
            .ToListAsync(cancellationToken);

        if (booked.Count == 0)
        {
            return;
        }

        // Idempotency: any BookingReminder from the last 24h whose payload cites the appointment.
        // Appointment ids are fresh per booking (never reused across operating days), so an older
        // reminder can never match today's still-Booked ids — bounding the scan avoids re-parsing
        // the clinic's entire notification history on every hourly run.
        var priorPayloads = await _db.OutboxNotifications.AsNoTracking()
            .Where(n => n.ClinicId == clinicId
                        && n.Type == NotificationType.BookingReminder
                        && n.CreatedAt >= utcNow.AddHours(-24))
            .Select(n => n.PayloadJson)
            .ToListAsync(cancellationToken);

        var remindedIds = new HashSet<Guid>();
        foreach (var payload in priorPayloads)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("appointmentId", out var idProp)
                    && Guid.TryParse(idProp.GetString(), out var id))
                {
                    remindedIds.Add(id);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed historical payloads.
            }
        }

        var added = 0;
        foreach (var appointment in booked)
        {
            if (remindedIds.Contains(appointment.Id))
            {
                continue;
            }

            _db.OutboxNotifications.Add(new OutboxNotification
            {
                ClinicId = clinicId,
                RecipientUserId = appointment.BookedByUserId,
                Type = NotificationType.BookingReminder,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    appointmentId = appointment.Id,
                    operatingDate = operatingDate.ToString("yyyy-MM-dd"),
                    temporaryNumber = appointment.TemporaryNumber,
                    message = "Please confirm your appointment today."
                }, CamelCaseJson),
                Status = OutboxNotificationStatus.Pending
            });
            added++;
        }

        if (added == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Enqueued {Count} booking reminders for clinic {ClinicId} on {OperatingDate}",
            added,
            clinicId,
            operatingDate);
    }

    private static bool IsMorningLocal(string timezoneId, DateTime utcNow)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timezoneId) ? "Africa/Cairo" : timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
        return local.Hour >= MorningHourStart && local.Hour < MorningHourEnd;
    }
}
