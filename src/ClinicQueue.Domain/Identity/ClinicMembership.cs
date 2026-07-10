using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

public class ClinicMembership : ClinicScopedEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Clinic Clinic { get; set; } = null!;

    public ClinicRole Role { get; set; }

    public Guid? InvitedByUserId { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
}
