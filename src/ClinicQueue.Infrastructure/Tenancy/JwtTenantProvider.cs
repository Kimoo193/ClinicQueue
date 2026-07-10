using System.Security.Claims;
using ClinicQueue.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace ClinicQueue.Infrastructure.Tenancy;

/// <summary>
/// Resolves clinic_id exclusively from the signed JWT claim — never from body/query/header.
/// </summary>
public sealed class JwtTenantProvider : ITenantProvider
{
    public const string ClinicIdClaimType = "clinic_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? ClinicId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var raw = user.FindFirstValue(ClinicIdClaimType);
            return Guid.TryParse(raw, out var clinicId) ? clinicId : null;
        }
    }

    public bool HasTenant => ClinicId.HasValue;
}
