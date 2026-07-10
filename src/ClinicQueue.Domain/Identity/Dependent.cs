using ClinicQueue.Domain.Common;

namespace ClinicQueue.Domain.Identity;

public class Dependent : ClinicScopedEntity
{
    public Guid GuardianUserId { get; set; }

    public User Guardian { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string RelationshipToGuardian { get; set; } = string.Empty;
}
