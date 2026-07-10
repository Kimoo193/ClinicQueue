using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

public class Subscription : ClinicScopedEntity
{
    public Clinic Clinic { get; set; } = null!;

    public string PlanName { get; set; } = string.Empty;

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTime RenewalDate { get; set; }
}
