using ClinicQueue.Domain.Identity;

namespace ClinicQueue.Application.Medical;

/// <summary>
/// Medical least-privilege matrix (constitution Principle IV, Clarification Q4, FR-085).
/// </summary>
public static class MedicalAccessPolicy
{
    public static bool CanReadClinical(ClinicRole role) =>
        role is ClinicRole.Doctor or ClinicRole.ClinicOwner;

    public static bool CanWriteClinical(ClinicRole role, Guid actorUserId, Guid treatingDoctorUserId) =>
        role == ClinicRole.Doctor && actorUserId == treatingDoctorUserId;

    /// <summary>
    /// Create is allowed for Doctor role only (actor becomes treating doctor).
    /// ClinicOwner cannot create even when authorized for other doctor ops.
    /// </summary>
    public static bool CanCreateVisit(ClinicRole role) => role == ClinicRole.Doctor;

    public static void EnsureCanRead(ClinicRole role)
    {
        if (!CanReadClinical(role))
        {
            throw new MedicalAccessException(
                "Receptionist and non-clinical roles cannot access diagnosis, notes, or prescriptions (FR-085).");
        }
    }

    public static void EnsureCanCreate(ClinicRole role)
    {
        if (!CanCreateVisit(role))
        {
            throw new MedicalAccessException(
                "Only the treating Doctor may create a Visit (Clarification Q4, FR-081).");
        }
    }

    public static void EnsureCanWrite(ClinicRole role, Guid actorUserId, Guid treatingDoctorUserId)
    {
        if (!CanWriteClinical(role, actorUserId, treatingDoctorUserId))
        {
            throw new MedicalAccessException(
                "Only the treating Doctor may edit clinical content; ClinicOwner is read-only (Clarification Q4).");
        }
    }

    /// <summary>PatientProfile read: the patient themselves, or clinical staff (FR-080).</summary>
    public static void EnsureCanReadProfile(bool isSelf, bool isClinicalStaff)
    {
        if (!isSelf && !isClinicalStaff)
        {
            throw new MedicalAccessException(
                "Only the patient, treating doctor, or clinic owner may view this profile (FR-080).");
        }
    }

    /// <summary>PatientProfile update: patient (self) only — no staff edit (FR-080).</summary>
    public static void EnsureCanUpdateProfile(bool isSelf)
    {
        if (!isSelf)
        {
            throw new MedicalAccessException("Only the patient may update their own profile (FR-080).");
        }
    }
}
