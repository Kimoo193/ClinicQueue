using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Queue;

namespace ClinicQueue.Domain.Medical;

/// <summary>Clinical outcome of a completed (or in-progress) appointment (FR-081).</summary>
public class Visit : ClinicScopedEntity
{
    public Guid AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    /// <summary>User id of the doctor who created the visit — sole writer (Clarification Q4).</summary>
    public Guid TreatingDoctorUserId { get; set; }

    public string Diagnosis { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public Prescription? Prescription { get; set; }
}
