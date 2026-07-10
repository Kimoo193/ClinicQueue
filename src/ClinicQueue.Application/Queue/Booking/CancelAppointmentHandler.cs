using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record CancelAppointmentCommand(
    Guid AppointmentId,
    Guid ActorUserId,
    Guid? ActorClinicId,
    bool IsStaff);

public sealed record CancelAppointmentResult(Guid AppointmentId, string Status);

public interface ICancelAppointmentHandler
{
    Task<CancelAppointmentResult> HandleAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Cancels an upcoming appointment (FR-044). Patients cancel own Booked rows; staff cancel within JWT clinic.</summary>
public sealed class CancelAppointmentHandler : ICancelAppointmentHandler
{
    private static readonly AppointmentStatus[] CancellableStatuses =
    [
        AppointmentStatus.Booked,
        AppointmentStatus.Present
    ];

    private readonly IAppDbContext _db;

    public CancelAppointmentHandler(IAppDbContext db) => _db = db;

    public async Task<CancelAppointmentResult> HandleAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (!CancellableStatuses.Contains(appointment.Status))
        {
            throw new InvalidOperationException(
                $"Cannot cancel appointment in status {appointment.Status}.");
        }

        if (command.IsStaff)
        {
            if (command.ActorClinicId is null || appointment.ClinicId != command.ActorClinicId.Value)
            {
                throw new UnauthorizedAccessException(
                    "Staff may only cancel appointments in their clinic.");
            }
        }
        else if (appointment.BookedByUserId != command.ActorUserId)
        {
            throw new UnauthorizedAccessException(
                "Patients may only cancel their own appointments.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);

        return new CancelAppointmentResult(appointment.Id, nameof(AppointmentStatus.Cancelled));
    }
}
