using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Identity;

namespace ClinicQueue.Domain.Medical;

/// <summary>Per-clinic medical summary for a patient (FR-080).</summary>
public class PatientProfile : ClinicScopedEntity
{
    public Guid ClinicMembershipId { get; set; }

    public ClinicMembership ClinicMembership { get; set; } = null!;

    public string? BloodType { get; set; }

    public string? ChronicConditions { get; set; }

    public string? Allergies { get; set; }

    public string? CurrentMedications { get; set; }
}
