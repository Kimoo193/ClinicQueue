namespace ClinicQueue.Domain.Common;

/// <summary>
/// Resolves the current tenant (clinic) for the active request.
/// The ClinicId is read ONLY from the authenticated JWT — never from body, query, or header.
/// </summary>
public interface ITenantProvider
{
    /// <summary>The current clinic id, or null when there is no tenant context (e.g. platform admin, pre-auth).</summary>
    Guid? ClinicId { get; }

    /// <summary>True when a tenant context is available.</summary>
    bool HasTenant { get; }
}
