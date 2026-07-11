using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Marks Inside patients Absent after the clinic's auto-absence window and advances
/// the queue via head-to-clinic outbox transitions (FR-029).
/// <para>
/// Runs under BYPASSRLS (see <see cref="PrivilegedJobActivator"/>). Isolation is therefore
/// by explicit <c>ClinicId</c> predicates on every read/write — never by assuming
/// <c>QueueSessionId</c> uniqueness alone.
/// </para>
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

    /// <param name="utcNow">Overrides the current instant for deterministic testing.</param>
    public async Task ProcessAsync(CancellationToken cancellationToken = default, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        // Enumerate clinics first (same shape as BookingReminderJob): every subsequent query
        // is scoped to one ClinicId so a bulk mutation cannot span tenants.
        var clinics = await _db.Clinics.AsNoTracking()
            .Select(c => new { c.Id, c.AutoAbsenceTimeoutMinutes, c.HeadToClinicThreshold })
            .ToListAsync(cancellationToken);

        foreach (var clinic in clinics)
        {
            await ProcessClinicAsync(
                clinic.Id,
                clinic.AutoAbsenceTimeoutMinutes,
                clinic.HeadToClinicThreshold,
                now,
                cancellationToken);
        }
    }

    /// <summary>
    /// Processes auto-absence for a single clinic. Public so isolation tests can invoke
    /// clinic A's path while clinic B is also due — proving mutations stay ClinicId-scoped.
    /// </summary>
    public async Task ProcessClinicAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default,
        DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var clinic = await _db.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new { c.AutoAbsenceTimeoutMinutes, c.HeadToClinicThreshold })
            .SingleAsync(cancellationToken);

        await ProcessClinicAsync(
            clinicId,
            clinic.AutoAbsenceTimeoutMinutes,
            clinic.HeadToClinicThreshold,
            now,
            cancellationToken);
    }

    private async Task ProcessClinicAsync(
        Guid clinicId,
        int autoAbsenceTimeoutMinutes,
        int headToClinicThreshold,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Explicit ClinicId on the read — do not load Inside appointments globally.
        var candidates = await _db.Appointments
            .Where(a => a.ClinicId == clinicId
                        && a.Status == AppointmentStatus.Inside
                        && a.CalledAt != null)
            .ToListAsync(cancellationToken);

        var due = candidates
            .Where(a => a.CalledAt!.Value.AddMinutes(autoAbsenceTimeoutMinutes) <= utcNow)
            .ToList();

        if (due.Count == 0)
        {
            return;
        }

        foreach (var sessionGroup in due.GroupBy(a => a.QueueSessionId))
        {
            var queueSessionId = sessionGroup.Key;

            // Re-assert tenant on every session batch (session ids are not a substitute for ClinicId).
            if (sessionGroup.Any(a => a.ClinicId != clinicId))
            {
                throw new InvalidOperationException(
                    $"AutoAbsenceJob refused a cross-clinic session batch for clinic {clinicId}.");
            }

            var previous = await _notificationRules.SnapshotBookedInstructionsAsync(
                clinicId,
                queueSessionId,
                headToClinicThreshold,
                cancellationToken);

            foreach (var appointment in sessionGroup)
            {
                // Tracked entities were loaded with ClinicId == clinicId; mutate only those rows.
                appointment.Status = AppointmentStatus.Absent;
                _logger.LogInformation(
                    "Auto-absence marked appointment {AppointmentId} absent for clinic {ClinicId} after {Minutes}m",
                    appointment.Id,
                    clinicId,
                    autoAbsenceTimeoutMinutes);
            }

            await _db.SaveChangesAsync(cancellationToken);

            await _notificationRules.EnqueueHeadToClinicTransitionsAsync(
                clinicId,
                queueSessionId,
                headToClinicThreshold,
                previous,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
