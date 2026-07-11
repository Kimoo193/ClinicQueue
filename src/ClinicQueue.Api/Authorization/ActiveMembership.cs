using System.Security.Claims;
using ClinicQueue.Application.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace ClinicQueue.Api.Authorization;

/// <summary>
/// Requires that the caller's JWT tenant context is still backed by an active
/// <see cref="ClinicQueue.Domain.Identity.ClinicMembership"/> at request time. The signed claim
/// alone is not sufficient authority: a role can be revoked while a token is still valid, and a
/// clinic-scoped role must never be honored outside that clinic.
/// </summary>
public sealed class ActiveMembershipRequirement : IAuthorizationRequirement;

/// <summary>
/// Confirms the (UserId, ClinicId, Role) tuple in the token is a currently-active membership.
/// SuperAdmin (a platform role with no clinic) bypasses. Any clinic-role claim that is no longer
/// backed by an active membership for the token's clinic_id fails the request (→ 403).
/// </summary>
public sealed class ActiveMembershipHandler : AuthorizationHandler<ActiveMembershipRequirement>
{
    private readonly IMembershipDirectory _memberships;

    public ActiveMembershipHandler(IMembershipDirectory memberships) => _memberships = memberships;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveMembershipRequirement requirement)
    {
        var user = context.User;

        // SuperAdmin is the only platform-level role (no ClinicId) — not membership-backed.
        if (user.IsInRole(Roles.SuperAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        var userIdRaw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        var clinicRaw = user.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (!Guid.TryParse(userIdRaw, out var userId) || !Guid.TryParse(clinicRaw, out var clinicId))
        {
            // No tenant context (e.g. identity-only token) cannot satisfy a clinic-scoped policy.
            return;
        }

        var activeRoles = await _memberships.GetActiveRolesAsync(userId, clinicId);
        if (activeRoles.Count == 0)
        {
            // Membership revoked / never existed for this clinic → block even if the token is valid.
            return;
        }

        // Every clinic-role claim in the token must still be an active membership role.
        var claimedClinicRoles = user.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(r => r != Roles.SuperAdmin)
            .ToList();

        if (claimedClinicRoles.Count > 0 && claimedClinicRoles.All(activeRoles.Contains))
        {
            context.Succeed(requirement);
        }
    }
}
