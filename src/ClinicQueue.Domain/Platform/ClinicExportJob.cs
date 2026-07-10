using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Platform;

public enum ClinicExportJobStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>Async clinic self-service data export job (FR-105).</summary>
public class ClinicExportJob : ClinicScopedEntity
{
    public Guid RequestedByUserId { get; set; }

    public ClinicExportJobStatus Status { get; set; } = ClinicExportJobStatus.Pending;

    public DateTime? CompletedAt { get; set; }

    /// <summary>JSON payload of patients, appointments, and visits for this clinic only.</summary>
    public string? PayloadJson { get; set; }

    public string? Error { get; set; }
}
