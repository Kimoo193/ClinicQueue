using ClinicQueue.Application.Queue.Positioning;

namespace ClinicQueue.Application.Notifications;

/// <summary>No-op publisher used until the API host registers the SignalR implementation.</summary>
public sealed class NullQueueRealtimePublisher : IQueueRealtimePublisher
{
    public Task PublishNotificationAsync(
        Guid recipientUserId,
        object payload,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishPositionAsync(
        Guid recipientUserId,
        Guid clinicId,
        Guid appointmentId,
        int countAhead,
        QueueInstruction instruction,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
