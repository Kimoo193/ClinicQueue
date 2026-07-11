using ClinicQueue.Domain.Identity;

namespace ClinicQueue.Application.Identity;

/// <summary>A user's active membership in one clinic.</summary>
public sealed record ClinicMembershipInfo(Guid ClinicId, string ClinicName, ClinicRole Role);

/// <summary>
/// Resolves a user's active <see cref="ClinicMembership"/>s across clinics. Token issuance
/// (login / switch-clinic / refresh) and request-time membership authorization need this
/// cross-tenant, and often with no <c>app.current_clinic</c> set yet (login is anonymous), so
/// it is backed by the privileged (BYPASSRLS) context — never the RLS-scoped app context.
/// A role is ALWAYS resolved per clinic; there is no global clinic role.
/// </summary>
public interface IMembershipDirectory
{
    /// <summary>All active memberships for the user, one per (clinic, role).</summary>
    Task<IReadOnlyList<ClinicMembershipInfo>> GetActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Active role names the user currently holds in a specific clinic (empty if none).</summary>
    Task<IReadOnlyList<string>> GetActiveRolesAsync(
        Guid userId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
}
