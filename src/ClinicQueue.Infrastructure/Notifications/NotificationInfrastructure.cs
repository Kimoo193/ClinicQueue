using ClinicQueue.Application.Notifications;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Infrastructure.Persistence;
using Hangfire;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainPushSubscription = ClinicQueue.Domain.Notifications.PushSubscription;

namespace ClinicQueue.Infrastructure.Notifications;

public sealed class VapidOptions
{
    public const string SectionName = "Vapid";
    public string Subject { get; set; } = "mailto:ops@clinicqueue.app";
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}

public sealed class SignalRNotificationChannel : INotificationChannel
{
    private readonly IQueueRealtimePublisher _realtime;
    public string Name => "SignalR";

    public SignalRNotificationChannel(IQueueRealtimePublisher realtime) => _realtime = realtime;

    public Task SendAsync(OutboxNotification notification, CancellationToken cancellationToken = default) =>
        _realtime.PublishNotificationAsync(
            notification.RecipientUserId,
            new
            {
                notification.Id,
                notification.Type,
                notification.PayloadJson,
                notification.ClinicId
            },
            cancellationToken);
}

/// <summary>
/// Web Push channel. Delivers via Lib.Net.Http.WebPush when VAPID keys are configured;
/// logs and skips when keys are missing (typical in local Dev).
/// </summary>
public sealed class WebPushNotificationChannel : INotificationChannel
{
    private readonly AppDbContext _db;
    private readonly VapidOptions _options;
    private readonly PushServiceClient _pushClient;
    private readonly ILogger<WebPushNotificationChannel> _logger;

    public string Name => "WebPush";

    public WebPushNotificationChannel(
        AppDbContext db,
        IOptions<VapidOptions> options,
        PushServiceClient pushClient,
        ILogger<WebPushNotificationChannel> logger)
    {
        _db = db;
        _options = options.Value;
        _pushClient = pushClient;
        _logger = logger;
    }

    public async Task SendAsync(OutboxNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicKey) || string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            _logger.LogWarning(
                "Web Push skipped (VAPID keys not configured) for notification {Id}",
                notification.Id);
            return;
        }

        var subscriptions = await _db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == notification.RecipientUserId && s.ClinicId == notification.ClinicId)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogDebug(
                "Web Push skipped (no stored subscription) for notification {Id} user {UserId}",
                notification.Id,
                notification.RecipientUserId);
            return;
        }

        using var vapid = new VapidAuthentication(_options.PublicKey, _options.PrivateKey)
        {
            Subject = string.IsNullOrWhiteSpace(_options.Subject)
                ? "mailto:ops@clinicqueue.app"
                : _options.Subject
        };

        var message = new PushMessage(notification.PayloadJson)
        {
            Topic = notification.Type.ToString(),
            Urgency = PushMessageUrgency.Normal
        };

        foreach (var subscription in subscriptions)
        {
            await DeliverAsync(subscription, message, vapid, notification.Id, cancellationToken);
        }
    }

    private async Task DeliverAsync(
        DomainPushSubscription subscription,
        PushMessage message,
        VapidAuthentication vapid,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var webPushSubscription = new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = subscription.Endpoint
        };
        webPushSubscription.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        webPushSubscription.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        try
        {
            await _pushClient.RequestPushMessageDeliveryAsync(
                webPushSubscription,
                message,
                vapid,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Web Push delivery failed for notification {NotificationId} endpoint {Endpoint}",
                notificationId,
                subscription.Endpoint);
            throw;
        }
    }
}

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IEnumerable<INotificationChannel> channels, ILogger<NotificationDispatcher> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task DispatchAsync(OutboxNotification notification, CancellationToken cancellationToken = default)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.SendAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Channel {Channel} failed for notification {Id}", channel.Name, notification.Id);
                throw;
            }
        }
    }
}

public sealed class OutboxDispatchJob
{
    public const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatchJob> _logger;

    public OutboxDispatchJob(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatchJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var pending = await db.OutboxNotifications
            .Where(n => n.Status == OutboxNotificationStatus.Pending && n.Attempts < MaxAttempts)
            .OrderBy(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            notification.Attempts += 1;
            notification.LastAttemptAt = DateTime.UtcNow;

            try
            {
                await dispatcher.DispatchAsync(notification, cancellationToken);
                notification.Status = OutboxNotificationStatus.Delivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox dispatch attempt {Attempt} failed for {Id}", notification.Attempts, notification.Id);
                if (notification.Attempts >= MaxAttempts)
                {
                    notification.Status = OutboxNotificationStatus.Failed;
                }
                else
                {
                    // Exponential backoff: re-queue via Hangfire with delay 2^attempts minutes
                    var delay = TimeSpan.FromMinutes(Math.Pow(2, notification.Attempts));
                    BackgroundJob.Schedule<OutboxDispatchJob>(
                        j => j.ProcessPendingAsync(CancellationToken.None),
                        delay);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
