namespace ClinicQueue.Infrastructure.Authorization;

public static class Roles
{
    public const string Patient = nameof(Domain.Identity.ClinicRole.Patient);
    public const string Receptionist = nameof(Domain.Identity.ClinicRole.Receptionist);
    public const string Doctor = nameof(Domain.Identity.ClinicRole.Doctor);
    public const string ClinicOwner = nameof(Domain.Identity.ClinicRole.ClinicOwner);
    public const string SuperAdmin = "SuperAdmin";
}

public static class Policies
{
    public const string Patient = "Patient";
    public const string Receptionist = "Receptionist";
    public const string Doctor = "Doctor";
    public const string ClinicOwner = "ClinicOwner";
    public const string SuperAdmin = "SuperAdmin";
    public const string ClinicStaff = "ClinicStaff";
    public const string PatientOrReceptionist = "PatientOrReceptionist";
    public const string PatientOrDoctor = "PatientOrDoctor";
}
