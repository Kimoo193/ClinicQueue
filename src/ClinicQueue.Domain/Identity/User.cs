using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

/// <summary>
/// Global platform identity — not clinic-scoped (Clarification Q1).
/// </summary>
public class User : BaseEntity
{
    public string EmailOrPhone { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public bool IsSuperAdmin { get; set; }

    public ICollection<ClinicMembership> Memberships { get; set; } = new List<ClinicMembership>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public bool IsLocked(DateTime utcNow) =>
        LockedUntil.HasValue && LockedUntil.Value > utcNow;
}
