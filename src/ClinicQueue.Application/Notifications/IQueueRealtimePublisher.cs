using ClinicQueue.Application.Queue.Positioning;

namespace ClinicQueue.Application.Notifications;

/// <summary>Realtime fan-out for queue notifications and live position updates (FR-027).</summary>
public interface IQueueRealtimePublisher
{
    Task PublishNotificationAsync(
        Guid recipientUserId,
        object payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a position update to the appointment owner only (not clinic-wide).
    /// </summary>
    Task PublishPositionAsync(
        Guid recipientUserId,
        Guid clinicId,
        Guid appointmentId,
        int countAhead,
        QueueInstruction instruction,
        CancellationToken cancellationToken = default);
}
