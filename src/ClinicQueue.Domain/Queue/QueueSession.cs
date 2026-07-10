using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Queue;

/// <summary>One operating day's queue for a clinic (FR-020).</summary>
public class QueueSession : ClinicScopedEntity
{
    public DateOnly OperatingDate { get; set; }

    public QueueSessionStatus Status { get; set; } = QueueSessionStatus.Open;

    public string? PauseReason { get; set; }

    public DateTime? PausedAt { get; set; }

    public Guid? PausedByUserId { get; set; }

    /// <summary>Normals confirmed since last urgent insertion (FR-024).</summary>
    public int NormalSinceLastUrgent { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
