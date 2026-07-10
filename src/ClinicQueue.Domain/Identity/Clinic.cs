using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

public class Clinic : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Specialty { get; set; } = string.Empty;

    public string? ContactInfo { get; set; }

    public string? LogoUrl { get; set; }

    public string? PrimaryColor { get; set; }

    public string? SecondaryColor { get; set; }

    /// <summary>Normal patients confirmed before inserting one urgent (default 2).</summary>
    public int UrgentRatio { get; set; } = 2;

    /// <summary>Count-ahead threshold for "head to clinic" (default 4).</summary>
    public int HeadToClinicThreshold { get; set; } = 4;

    public int AutoAbsenceTimeoutMinutes { get; set; } = 15;

    public int RepeatedAbsenceThreshold { get; set; } = 2;

    public string Timezone { get; set; } = "Africa/Cairo";

    public string? WorkingHoursJson { get; set; }

    public string SubdomainSlug { get; set; } = string.Empty;

    public string? QrCodeUrl { get; set; }

    public ClinicStatus Status { get; set; } = ClinicStatus.Active;

    /// <summary>Average of post-visit ratings (FR-095); null when no ratings yet.</summary>
    public double? AggregateRating { get; set; }

    public ICollection<ClinicMembership> Memberships { get; set; } = new List<ClinicMembership>();

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
