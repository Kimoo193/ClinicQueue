using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Emergency;

/// <summary>
/// Enqueues a PauseResume outbox row for every still-waiting (Booked/Present) patient in a
/// session — the shared fan-out behind both pause and resume (FR-051, FR-053, FR-073).
/// Caller must SaveChanges in the same transaction.
/// </summary>
internal static class PauseResumeNotifications
{
    public static async Task EnqueueAsync(
        IAppDbContext db,
        Guid clinicId,
        Guid queueSessionId,
        string action,
        string? reason,
        CancellationToken cancellationToken)
    {
        var recipients = await db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == queueSessionId
                        && (a.Status == AppointmentStatus.Booked
                            || a.Status == AppointmentStatus.Present))
            .Select(a => new { a.Id, a.BookedByUserId })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            // Resume carries no reason; keep it out of the payload rather than emitting null.
            object payload = reason is null
                ? new { queueSessionId, appointmentId = recipient.Id, action }
                : new { queueSessionId, appointmentId = recipient.Id, action, reason };

            db.OutboxNotifications.Add(new OutboxNotification
            {
                ClinicId = clinicId,
                RecipientUserId = recipient.BookedByUserId,
                Type = NotificationType.PauseResume,
                PayloadJson = JsonSerializer.Serialize(payload, QueueNotificationRules.CamelCaseJson),
                Status = OutboxNotificationStatus.Pending
            });
        }
    }
}
