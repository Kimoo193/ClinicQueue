using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Queue;

namespace ClinicQueue.Domain.Reporting;

/// <summary>Post-visit patient feedback (FR-094); aggregates to Clinic.AggregateRating (FR-095).</summary>
public class Rating : ClinicScopedEntity
{
    public Guid AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    /// <summary>Score from 1 (worst) to 5 (best).</summary>
    public int Score { get; set; }

    public string? Comment { get; set; }
}
