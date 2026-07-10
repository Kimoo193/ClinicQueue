using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Notifications;

public interface IQueueNotificationRules
{
    /// <summary>
    /// Persists HeadToClinic outbox rows for appointments whose instruction just crossed
    /// into head-to-clinic (FR-071). Caller must SaveChanges in the same transaction.
    /// </summary>
    Task EnqueueHeadToClinicTransitionsAsync(
        Guid clinicId,
        Guid queueSessionId,
        int headToClinicThreshold,
        IReadOnlyDictionary<Guid, QueueInstruction>? previousInstructions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Snapshots the current head-to-clinic instruction for every Booked appointment in a
    /// session, so a later <see cref="EnqueueHeadToClinicTransitionsAsync"/> call can detect
    /// WaitHome → HeadToClinic transitions (FR-071). Capture this before mutating the queue.
    /// </summary>
    Task<Dictionary<Guid, QueueInstruction>> SnapshotBookedInstructionsAsync(
        Guid queueSessionId,
        int headToClinicThreshold,
        CancellationToken cancellationToken = default);
}

public sealed class QueueNotificationRules : IQueueNotificationRules
{
    internal static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAppDbContext _db;
    private readonly ICountAheadService _countAhead;

    public QueueNotificationRules(IAppDbContext db, ICountAheadService countAhead)
    {
        _db = db;
        _countAhead = countAhead;
    }

    public async Task EnqueueHeadToClinicTransitionsAsync(
        Guid clinicId,
        Guid queueSessionId,
        int headToClinicThreshold,
        IReadOnlyDictionary<Guid, QueueInstruction>? previousInstructions,
        CancellationToken cancellationToken = default)
    {
        // Peers must include Present/Inside so confirmed patients count toward "ahead".
        var sessionAppointments = await _db.Appointments
            .Where(a => a.QueueSessionId == queueSessionId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Absent
                        && a.Status != AppointmentStatus.CarriedOver)
            .ToListAsync(cancellationToken);

        var booked = sessionAppointments
            .Where(a => a.Status == AppointmentStatus.Booked)
            .ToList();

        foreach (var appointment in booked)
        {
            var result = _countAhead.ComputeFromSnapshot(
                appointment,
                sessionAppointments,
                headToClinicThreshold);
            var previous = previousInstructions?.GetValueOrDefault(appointment.Id);

            if (result.Instruction != QueueInstruction.HeadToClinic)
            {
                continue;
            }

            if (previous == QueueInstruction.HeadToClinic)
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(new
            {
                appointmentId = appointment.Id,
                countAhead = result.CountAhead,
                instruction = "headToClinic"
            }, CamelCaseJson);

            _db.OutboxNotifications.Add(new OutboxNotification
            {
                ClinicId = clinicId,
                RecipientUserId = appointment.BookedByUserId,
                Type = NotificationType.HeadToClinic,
                PayloadJson = payload,
                Status = OutboxNotificationStatus.Pending
            });
        }
    }

    public async Task<Dictionary<Guid, QueueInstruction>> SnapshotBookedInstructionsAsync(
        Guid queueSessionId,
        int headToClinicThreshold,
        CancellationToken cancellationToken = default)
    {
        var peers = await _db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == queueSessionId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Absent
                        && a.Status != AppointmentStatus.CarriedOver)
            .ToListAsync(cancellationToken);

        var snapshot = new Dictionary<Guid, QueueInstruction>();
        foreach (var appointment in peers.Where(a => a.Status == AppointmentStatus.Booked))
        {
            snapshot[appointment.Id] = _countAhead
                .ComputeFromSnapshot(appointment, peers, headToClinicThreshold)
                .Instruction;
        }

        return snapshot;
    }
}
