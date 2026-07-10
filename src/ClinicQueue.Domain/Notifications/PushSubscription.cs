using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Notifications;

/// <summary>
/// Browser Web Push subscription for a user within a clinic (VAPID).
/// </summary>
public class PushSubscription : ClinicScopedEntity
{
    public Guid UserId { get; set; }

    /// <summary>Push service endpoint URL (unique per browser subscription).</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string P256dh { get; set; } = string.Empty;

    public string Auth { get; set; } = string.Empty;

    public string? UserAgent { get; set; }
}
