using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Marks Inside patients Absent after the clinic's auto-absence window and advances
/// the queue via head-to-clinic outbox transitions (FR-029).
/// </summary>
public sealed class AutoAbsenceJob
{
    private readonly IAppDbContext _db;
    private readonly IQueueNotificationRules _notificationRules;
    private readonly ILogger<AutoAbsenceJob> _logger;

    public AutoAbsenceJob(
        IAppDbContext db,
        IQueueNotificationRules notificationRules,
        ILogger<AutoAbsenceJob> logger)
    {
        _db = db;
        _notificationRules = notificationRules;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var candidates = await (
            from a in _db.Appointments
            join c in _db.Clinics on a.ClinicId equals c.Id
            where a.Status == AppointmentStatus.Inside
                  && a.CalledAt != null
            select new { Appointment = a, c.AutoAbsenceTimeoutMinutes, c.HeadToClinicThreshold }
        ).ToListAsync(cancellationToken);

        var due = candidates
            .Where(x => x.Appointment.CalledAt!.Value.AddMinutes(x.AutoAbsenceTimeoutMinutes) <= now)
            .ToList();

        if (due.Count == 0)
        {
            return;
        }

        foreach (var group in due.GroupBy(x => x.Appointment.QueueSessionId))
        {
            var first = group.First();
            var previous = await _notificationRules.SnapshotBookedInstructionsAsync(
                group.Key,
                first.HeadToClinicThreshold,
                cancellationToken);

            foreach (var item in group)
            {
                item.Appointment.Status = AppointmentStatus.Absent;
                _logger.LogInformation(
                    "Auto-absence marked appointment {AppointmentId} absent after {Minutes}m",
                    item.Appointment.Id,
                    item.AutoAbsenceTimeoutMinutes);
            }

            await _db.SaveChangesAsync(cancellationToken);

            await _notificationRules.EnqueueHeadToClinicTransitionsAsync(
                first.Appointment.ClinicId,
                group.Key,
                first.HeadToClinicThreshold,
                previous,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
