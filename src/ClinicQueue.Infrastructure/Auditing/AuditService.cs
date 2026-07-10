using ClinicQueue.Domain.Common;
using ClinicQueue.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace ClinicQueue.Infrastructure.Auditing;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Retention in days; constitution requires ≥ 2 years (730 days).</summary>
    public int RetentionDays { get; set; } = 730;
}

public interface IAuditService
{
    Task WriteAsync(
        Guid clinicId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Append-only audit writer. No update/delete API is exposed.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly AuditOptions _options;

    public AuditService(AppDbContext db, IOptions<AuditOptions> options)
    {
        _db = db;
        _options = options.Value;
        if (_options.RetentionDays < 730)
        {
            throw new InvalidOperationException("Audit retention must be ≥ 730 days (2 years).");
        }
    }

    public async Task WriteAsync(
        Guid clinicId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
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

        await _db.SaveChangesAsync(cancellationToken);
    }
}
