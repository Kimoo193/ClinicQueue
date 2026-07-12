using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record OnBehalfBookingCommand(
    Guid ClinicId,
    Guid ReceptionistUserId,
    Guid PatientMembershipId,
    DateOnly Day,
    AppointmentType Type,
    AppointmentMethod Method);

public interface IOnBehalfBookingHandler
{
    Task<CreateAppointmentResult> HandleAsync(
        OnBehalfBookingCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Receptionist books on behalf of a patient (FR-043); may set Normal/Urgent.</summary>
public sealed class OnBehalfBookingHandler : IOnBehalfBookingHandler
{
    private readonly ICreateAppointmentHandler _create;
    private readonly IAppDbContext _db;

    public OnBehalfBookingHandler(ICreateAppointmentHandler create, IAppDbContext db)
    {
        _create = create;
        _db = db;
    }

    public async Task<CreateAppointmentResult> HandleAsync(
        OnBehalfBookingCommand command,
        CancellationToken cancellationToken = default)
    {
        var membership = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.Id == command.PatientMembershipId
                     && m.ClinicId == command.ClinicId
                     && m.Role == ClinicRole.Patient
                     && m.Status != MembershipStatus.Restricted,
                cancellationToken)
            ?? throw new InvalidOperationException("Patient membership not found.");

        if (membership.Status != MembershipStatus.Active)
        {
            throw new InvalidOperationException("Booking is restricted for this patient.");
        }

        // Book as the patient (patient-of-record + BookedByUserId = patient) so list/me works.
        var created = await _create.HandleAsync(
            new CreateAppointmentCommand(
                command.ClinicId,
                membership.UserId,
                command.Day,
                command.Method,
                PossibleUrgentFlag: false,
                DependentId: null),
            cancellationToken);

        if (command.Type == AppointmentType.Urgent)
        {
            var appointment = await _db.Appointments
                .FirstAsync(a => a.Id == created.AppointmentId, cancellationToken);
            appointment.Type = AppointmentType.Urgent;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return created;
    }
}

public sealed record SetAppointmentTypeCommand(
    Guid AppointmentId,
    Guid ClinicId,
    AppointmentType Type);

public sealed record SetAppointmentTypeResult(Guid AppointmentId, AppointmentType Type);

public interface ISetAppointmentTypeHandler
{
    Task<SetAppointmentTypeResult> HandleAsync(
        SetAppointmentTypeCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Receptionist/Doctor sets Normal/Urgent (FR-023, FR-041) — patients cannot.</summary>
public sealed class SetAppointmentTypeHandler : ISetAppointmentTypeHandler
{
    private readonly IAppDbContext _db;

    public SetAppointmentTypeHandler(IAppDbContext db) => _db = db;

    public async Task<SetAppointmentTypeResult> HandleAsync(
        SetAppointmentTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.Status is AppointmentStatus.Completed
            or AppointmentStatus.Cancelled
            or AppointmentStatus.Absent
            or AppointmentStatus.CarriedOver)
        {
            throw new InvalidOperationException(
                $"Cannot change type for status {appointment.Status}.");
        }

        // Real numbers already assigned keep relative order; type change only affects
        // future injection for not-yet-numbered patients.
        if (appointment.RealNumber is not null && command.Type != appointment.Type)
        {
            throw new InvalidOperationException(
                "Cannot change type after a real number has been assigned.");
        }

        appointment.Type = command.Type;
        await _db.SaveChangesAsync(cancellationToken);
        return new SetAppointmentTypeResult(appointment.Id, appointment.Type);
    }
}
