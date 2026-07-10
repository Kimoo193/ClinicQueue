using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.DayClose;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record ConfirmDepositCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid ActorUserId);

public interface IConfirmDepositHandler
{
    Task<ConfirmDepositResponse> HandleAsync(
        ConfirmDepositCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Receptionist manual confirmation of a Phone/Deposit booking (FR-042). Marks the deposit paid
/// so a subsequent no-show auto-carries to the next day at close (FR-065).
/// </summary>
public sealed class ConfirmDepositHandler : IConfirmDepositHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public ConfirmDepositHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ConfirmDepositResponse> HandleAsync(
        ConfirmDepositCommand command,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.Method != AppointmentMethod.PhoneDeposit)
        {
            throw new InvalidOperationException("Only a Phone/Deposit booking can have its deposit confirmed.");
        }

        if (appointment.DepositConfirmedAt is not null)
        {
            throw new InvalidOperationException("Deposit is already confirmed.");
        }

        var confirmedAt = DateTime.UtcNow;
        appointment.DepositConfirmedAt = confirmedAt;

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "DepositConfirm",
            nameof(Appointment),
            appointment.Id,
            JsonSerializer.Serialize(new { confirmedAt }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new ConfirmDepositResponse(appointment.Id, confirmedAt);
    }
}
