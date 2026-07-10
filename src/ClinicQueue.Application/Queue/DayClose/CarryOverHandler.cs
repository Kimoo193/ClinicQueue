using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.DayClose;

public sealed record CarryOverAppointmentCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid ActorUserId);

public sealed record CancelUnservedCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid ActorUserId);

public interface ICarryOverHandler
{
    Task<CarryOverResponse> CarryOverAsync(
        CarryOverAppointmentCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelUnservedResponse> CancelAsync(
        CancelUnservedCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Receptionist manual carry-over / cancel of an unserved appointment at day close
/// (FR-062–FR-064).
/// </summary>
public sealed class CarryOverHandler : ICarryOverHandler
{
    private readonly IAppDbContext _db;
    private readonly ICarryOverService _carryOver;

    public CarryOverHandler(IAppDbContext db, ICarryOverService carryOver)
    {
        _db = db;
        _carryOver = carryOver;
    }

    public async Task<CarryOverResponse> CarryOverAsync(
        CarryOverAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadUnservedAsync(command.ClinicId, command.AppointmentId, cancellationToken);

        var session = await _db.QueueSessions
            .FirstAsync(s => s.Id == source.QueueSessionId, cancellationToken);

        // Include already-carried rows so ranks stay stable and collision-free across carries.
        var closing = await _db.Appointments
            .Where(a => a.QueueSessionId == source.QueueSessionId
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var nextSession = await _carryOver.GetOrCreateOpenSessionAsync(
            command.ClinicId,
            session.OperatingDate.AddDays(1),
            cancellationToken);

        var outcome = _carryOver.CarryOver(source, nextSession, closing);
        await _db.SaveChangesAsync(cancellationToken);

        return new CarryOverResponse(
            source.Id,
            source.Status.ToString(),
            outcome.NextDayPosition);
    }

    public async Task<CancelUnservedResponse> CancelAsync(
        CancelUnservedCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadUnservedAsync(command.ClinicId, command.AppointmentId, cancellationToken);

        source.Status = AppointmentStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);

        return new CancelUnservedResponse(source.Id, source.Status.ToString());
    }

    private async Task<Appointment> LoadUnservedAsync(
        Guid clinicId,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == appointmentId && a.ClinicId == clinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (!DayCloseStatuses.IsUnserved(appointment.Status))
        {
            throw new InvalidOperationException(
                $"Only an unserved appointment can be carried over or cancelled (status {appointment.Status}).");
        }

        return appointment;
    }
}
