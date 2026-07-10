namespace ClinicQueue.Application.Queue.DayClose;

public sealed record UnservedAppointmentDto(
    Guid AppointmentId,
    Guid? PatientMembershipId,
    Guid? DependentId,
    string Type,
    string Method,
    string Status,
    int TemporaryNumber,
    int? RealNumber,
    bool DepositConfirmed);

public sealed record CloseDaySessionResponse(
    Guid QueueSessionId,
    string Status,
    int AutoCarriedOverCount,
    IReadOnlyList<UnservedAppointmentDto> UnservedAppointments);

public sealed record CarryOverResponse(
    Guid AppointmentId,
    string Status,
    int NextDayPosition);

public sealed record CancelUnservedResponse(
    Guid AppointmentId,
    string Status);

public sealed record ConfirmDepositResponse(
    Guid AppointmentId,
    DateTime DepositConfirmedAt);
