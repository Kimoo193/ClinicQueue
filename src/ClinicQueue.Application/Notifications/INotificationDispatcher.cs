using ClinicQueue.Domain.Notifications;

namespace ClinicQueue.Application.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    Task SendAsync(OutboxNotification notification, CancellationToken cancellationToken = default);
}

public interface INotificationDispatcher
{
    Task DispatchAsync(OutboxNotification notification, CancellationToken cancellationToken = default);
}
