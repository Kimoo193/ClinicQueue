using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Identity;
using ClinicQueue.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Infrastructure.Identity;

/// <summary>
/// <see cref="IMembershipDirectory"/> backed by the privileged (BYPASSRLS) context so a user's
/// memberships can be enumerated across clinics during token issuance and authorization, where
/// no single tenant context applies.
/// </summary>
public sealed class MembershipDirectory : IMembershipDirectory
{
    private readonly IPrivilegedDbContext _db;

    public MembershipDirectory(IPrivilegedDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClinicMembershipInfo>> GetActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _db.ClinicMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .OrderBy(m => m.Clinic.Name)
            .Select(m => new ClinicMembershipInfo(m.ClinicId, m.Clinic.Name, m.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveRolesAsync(
        Guid userId,
        Guid clinicId,
        CancellationToken cancellationToken = default) =>
        await _db.ClinicMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                        && m.ClinicId == clinicId
                        && m.Status == MembershipStatus.Active)
            .Select(m => m.Role.ToString())
            .Distinct()
            .ToListAsync(cancellationToken);
}
