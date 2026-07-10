using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Medical;

/// <summary>E-prescription tied to a Visit (FR-082).</summary>
public class Prescription : ClinicScopedEntity
{
    public Guid VisitId { get; set; }

    public Visit Visit { get; set; } = null!;

    public string Drug { get; set; } = string.Empty;

    public string Dose { get; set; } = string.Empty;

    public string Frequency { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;
}
