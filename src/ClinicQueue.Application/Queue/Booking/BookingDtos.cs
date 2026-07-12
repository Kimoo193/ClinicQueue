using FluentValidation;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record CreateAppointmentRequest(
    Guid ClinicId,
    DateOnly Day,
    string Method,
    bool PossibleUrgentFlag = false,
    Guid? DependentId = null,
    string? DepositProofUrl = null);

public sealed record CreateAppointmentResponse(Guid AppointmentId, int TemporaryNumber);

public sealed record PatientAppointmentDto(
    Guid AppointmentId,
    Guid ClinicId,
    string Status,
    int TemporaryNumber,
    int? RealNumber,
    int? CountAhead,
    string? Instruction,
    Guid? DependentId = null);

public sealed record CancelAppointmentResponse(Guid AppointmentId, string Status);

public sealed record OnBehalfBookingRequest(
    Guid ClinicId,
    Guid PatientRef,
    DateOnly Day,
    string Type,
    string Method);

public sealed record SetAppointmentTypeRequest(string Type);

public sealed record SetAppointmentTypeResponse(Guid AppointmentId, string Type);

public sealed record InstantEntryRequest(
    Guid ClinicId,
    Guid PatientRef,
    string Reason);

public sealed record ConfirmArrivalResponse(
    Guid AppointmentId,
    int? RealNumber,
    string Status,
    bool WaitingForUrgentRatio = false);

public sealed record CallNextResponse(Guid AppointmentId, string Status);

public sealed record InstantEntryResponse(
    Guid AppointmentId,
    int RealNumber,
    string Status);

public sealed record ReceptionAppointmentDto(
    Guid AppointmentId,
    Guid? PatientMembershipId,
    Guid BookedByUserId,
    string Type,
    string Method,
    string Status,
    int TemporaryNumber,
    int? RealNumber,
    bool IsInstantEntry,
    bool PossibleUrgentFlag,
    bool RepeatedAbsenceFlag = false,
    int AbsenceCount = 0,
    string? MembershipStatus = null,
    bool DepositConfirmed = false,
    // Human identifier (patient email/phone) so reception can recognise who is next,
    // not a raw membership GUID. Null for dependent bookings with no linked user.
    string? PatientLabel = null);

public sealed class CreateAppointmentRequestValidator : AbstractValidator<CreateAppointmentRequest>
{
    public CreateAppointmentRequestValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Day).NotEmpty();
        RuleFor(x => x.Method)
            .NotEmpty()
            .Must(m => m is "WalkIn" or "Walkin" or "PhoneDeposit" or "Phone-Deposit")
            .WithMessage("Method must be WalkIn or PhoneDeposit.");
    }
}

public sealed class OnBehalfBookingRequestValidator : AbstractValidator<OnBehalfBookingRequest>
{
    public OnBehalfBookingRequestValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PatientRef).NotEmpty();
        RuleFor(x => x.Day).NotEmpty();
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => t is "Normal" or "Urgent")
            .WithMessage("Type must be Normal or Urgent.");
        RuleFor(x => x.Method)
            .NotEmpty()
            .Must(m => m is "WalkIn" or "Walkin" or "PhoneDeposit" or "Phone-Deposit")
            .WithMessage("Method must be WalkIn or PhoneDeposit.");
    }
}

public sealed class SetAppointmentTypeRequestValidator : AbstractValidator<SetAppointmentTypeRequest>
{
    public SetAppointmentTypeRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => t is "Normal" or "Urgent")
            .WithMessage("Type must be Normal or Urgent.");
    }
}

public sealed class InstantEntryRequestValidator : AbstractValidator<InstantEntryRequest>
{
    public InstantEntryRequestValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PatientRef).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
