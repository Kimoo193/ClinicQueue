using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Notifications;

public enum OutboxNotificationStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2
}

public enum NotificationType
{
    BookingConfirmation = 0,
    HeadToClinic = 1,
    YourTurn = 2,
    PauseResume = 3,
    CarryOver = 4,
    SubscriptionExpiring = 5
}

public class OutboxNotification : ClinicScopedEntity
{
    public Guid RecipientUserId { get; set; }

    public NotificationType Type { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public OutboxNotificationStatus Status { get; set; } = OutboxNotificationStatus.Pending;

    public int Attempts { get; set; }

    public DateTime? LastAttemptAt { get; set; }
}
