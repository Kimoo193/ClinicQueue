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
    private const int PerClinicBatchSize = 50;

    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<OutboxDispatchJob> _logger;

    // Resolved by PrivilegedJobActivator inside a BYPASSRLS scope.
    public OutboxDispatchJob(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ILogger<OutboxDispatchJob> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches pending outbox rows.
    /// <para>
    /// Why per-clinic batches (not one global Take(50) sweep): this job runs under BYPASSRLS,
    /// so the database will not stop a bulk update from touching every tenant. Discovering
    /// which clinics have work is a cheap Distinct on ClinicId; every subsequent read and
    /// SaveChanges operates on rows already loaded with an explicit ClinicId predicate.
    /// Downstream channels (e.g. Web Push) also filter subscriptions by
    /// <c>(UserId, ClinicId)</c>. A global pending sweep would be fine for throughput only if
    /// every mutation stayed ClinicId-predicated — per-clinic batches make that the default
    /// structure rather than a discipline reviewers must remember.
    /// </para>
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var db = _db;
        var dispatcher = _dispatcher;

        var clinicIds = await db.OutboxNotifications.AsNoTracking()
            .Where(n => n.Status == OutboxNotificationStatus.Pending && n.Attempts < MaxAttempts)
            .Select(n => n.ClinicId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var clinicId in clinicIds)
        {
            await ProcessClinicBatchAsync(db, dispatcher, clinicId, cancellationToken);
        }
    }

    private async Task ProcessClinicBatchAsync(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        var pending = await db.OutboxNotifications
            .Where(n => n.ClinicId == clinicId
                        && n.Status == OutboxNotificationStatus.Pending
                        && n.Attempts < MaxAttempts)
            .OrderBy(n => n.CreatedAt)
            .Take(PerClinicBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            if (notification.ClinicId != clinicId)
            {
                throw new InvalidOperationException(
                    $"OutboxDispatchJob refused a notification whose ClinicId {notification.ClinicId} " +
                    $"does not match batch clinic {clinicId}.");
            }

            notification.Attempts += 1;
            notification.LastAttemptAt = DateTime.UtcNow;

            try
            {
                await dispatcher.DispatchAsync(notification, cancellationToken);
                notification.Status = OutboxNotificationStatus.Delivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Outbox dispatch attempt {Attempt} failed for {Id} (clinic {ClinicId})",
                    notification.Attempts,
                    notification.Id,
                    clinicId);
                if (notification.Attempts >= MaxAttempts)
                {
                    notification.Status = OutboxNotificationStatus.Failed;
                }
                else
                {
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
