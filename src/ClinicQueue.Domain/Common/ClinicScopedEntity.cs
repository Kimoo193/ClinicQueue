namespace ClinicQueue.Domain.Common;

/// <summary>
/// Base for clinic-scoped entities with optimistic concurrency via <see cref="RowVersion"/>.
/// </summary>
public abstract class ClinicScopedEntity : BaseEntity, IClinicScoped
{
    public Guid ClinicId { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL bytea).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
