using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

/// <summary>
/// Hashed refresh token stored server-side; raw token is only in the HttpOnly cookie.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public Guid? ClinicId { get; set; }

    public bool IsActive(DateTime utcNow) =>
        RevokedAt is null && ExpiresAt > utcNow;
}
