using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Common;
using ClinicQueue.Infrastructure.Persistence;

namespace ClinicQueue.Infrastructure.Auditing;

/// <summary>
/// Appends audit rows to the ambient <see cref="AppDbContext"/> without saving —
/// callers commit in the same transaction as the state change.
/// </summary>
public sealed class EfAuditWriter : IAuditWriter
{
    private readonly AppDbContext _db;

    public EfAuditWriter(AppDbContext db) => _db = db;

    public void Append(
        Guid clinicId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? metadataJson = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Timestamp = DateTime.UtcNow,
            MetadataJson = metadataJson
        });
    }
}
