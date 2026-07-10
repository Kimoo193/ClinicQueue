using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Queue;

/// <summary>A patient's booked or walk-in visit for a specific operating day.</summary>
public class Appointment : ClinicScopedEntity
{
    public Guid QueueSessionId { get; set; }

    public QueueSession QueueSession { get; set; } = null!;

    /// <summary>Patient ClinicMembership id, or null when booking for a Dependent.</summary>
    public Guid? PatientMembershipId { get; set; }

    /// <summary>Dependent id when booking on behalf of a registered dependent (FR-046).</summary>
    public Guid? DependentId { get; set; }

    public Guid BookedByUserId { get; set; }

    public AppointmentType Type { get; set; } = AppointmentType.Normal;

    /// <summary>Patient-submitted advisory flag only — does not set Type to Urgent.</summary>
    public bool PossibleUrgentFlag { get; set; }

    public AppointmentMethod Method { get; set; } = AppointmentMethod.WalkIn;

    /// <summary>Assigned at booking (FR-021).</summary>
    public int TemporaryNumber { get; set; }

    /// <summary>Assigned only on arrival confirmation (FR-022); null until then.</summary>
    public int? RealNumber { get; set; }

    public int CompanionsCount { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;

    public bool IsInstantEntry { get; set; }

    public string? InstantEntryReason { get; set; }

    /// <summary>Deposit proof reference (screenshot URL) for a Phone/Deposit booking (FR-042).</summary>
    public string? DepositProofUrl { get; set; }

    /// <summary>UTC when a receptionist confirmed the deposit; null until confirmed (FR-042).</summary>
    public DateTime? DepositConfirmedAt { get; set; }

    /// <summary>UTC when status became Inside (FR-029 auto-absence window).</summary>
    public DateTime? CalledAt { get; set; }

    /// <summary>Patient-of-record key for duplicate same-day checks (FR-041a).</summary>
    public Guid PatientOfRecordKey => DependentId ?? PatientMembershipId
        ?? throw new InvalidOperationException("Appointment must reference a membership or dependent.");
}
