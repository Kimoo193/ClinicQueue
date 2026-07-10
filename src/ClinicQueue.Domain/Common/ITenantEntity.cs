namespace ClinicQueue.Domain.Common;

/// <summary>
/// Backward-compatible alias for <see cref="IClinicScoped"/>.
/// Prefer <see cref="IClinicScoped"/> in new code.
/// </summary>
public interface ITenantEntity : IClinicScoped;
