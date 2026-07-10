namespace ClinicQueue.Domain.Common;

/// <summary>
/// Marks an entity as belonging to a specific clinic (tenant).
/// Every entity implementing this MUST be filtered by ClinicId in the repository layer.
/// </summary>
public interface IClinicScoped
{
    Guid ClinicId { get; set; }
}
