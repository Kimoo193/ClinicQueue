using FluentValidation;

namespace ClinicQueue.Application.Medical;

public sealed record CreateVisitRequest(string Diagnosis, string Notes);

public sealed record UpdateVisitRequest(string Diagnosis, string Notes);

public sealed record VisitResponse(
    Guid VisitId,
    Guid AppointmentId,
    Guid TreatingDoctorUserId,
    string Diagnosis,
    string Notes,
    PrescriptionResponse? Prescription);

public sealed record IssuePrescriptionRequest(
    string Drug,
    string Dose,
    string Frequency,
    string Duration);

public sealed record PrescriptionResponse(
    Guid PrescriptionId,
    string Drug,
    string Dose,
    string Frequency,
    string Duration);

public sealed record PatientProfileResponse(
    Guid MembershipId,
    string? BloodType,
    string? ChronicConditions,
    string? Allergies,
    string? CurrentMedications);

public sealed record UpdatePatientProfileRequest(
    string? BloodType,
    string? ChronicConditions,
    string? Allergies,
    string? CurrentMedications);

public sealed record PatientVisitHistoryDto(
    Guid VisitId,
    DateTime Date,
    string Diagnosis,
    string Notes,
    PrescriptionResponse? Prescription);

public sealed class CreateVisitRequestValidator : AbstractValidator<CreateVisitRequest>
{
    public CreateVisitRequestValidator()
    {
        RuleFor(x => x.Diagnosis).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(8000);
    }
}

public sealed class UpdateVisitRequestValidator : AbstractValidator<UpdateVisitRequest>
{
    public UpdateVisitRequestValidator()
    {
        RuleFor(x => x.Diagnosis).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(8000);
    }
}

public sealed class IssuePrescriptionRequestValidator : AbstractValidator<IssuePrescriptionRequest>
{
    public IssuePrescriptionRequestValidator()
    {
        RuleFor(x => x.Drug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Dose).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Frequency).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Duration).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdatePatientProfileRequestValidator : AbstractValidator<UpdatePatientProfileRequest>
{
    public UpdatePatientProfileRequestValidator()
    {
        RuleFor(x => x.BloodType).MaximumLength(20);
        RuleFor(x => x.ChronicConditions).MaximumLength(2000);
        RuleFor(x => x.Allergies).MaximumLength(2000);
        RuleFor(x => x.CurrentMedications).MaximumLength(2000);
    }
}
