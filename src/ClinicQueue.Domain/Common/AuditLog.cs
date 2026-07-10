using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Common;

public class AuditLog : ClinicScopedEntity
{
    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; }
}
