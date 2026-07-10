using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Numbering;

public sealed record CallNextCommand(Guid AppointmentId, Guid ClinicId);

public sealed record CallNextResult(Guid AppointmentId, AppointmentStatus Status);

public interface ICallNextHandler
{
    Task<CallNextResult> HandleAsync(
        CallNextCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Moves a Present (numbered) patient to Inside and enqueues YourTurn (FR-028, FR-072).</summary>
public sealed class CallNextHandler : ICallNextHandler
{
    private readonly IAppDbContext _db;
    private readonly IClinicAccessGate _accessGate;

    public CallNextHandler(IAppDbContext db, IClinicAccessGate accessGate)
    {
        _db = db;
        _accessGate = accessGate;
    }

    public async Task<CallNextResult> HandleAsync(
        CallNextCommand command,
        CancellationToken cancellationToken = default)
    {
        await _accessGate.EnsureClinicActiveAsync(command.ClinicId, cancellationToken);

        if (_db is not DbContext ef)
        {
            throw new InvalidOperationException("CallNext requires an EF Core DbContext.");
        }

        await using var tx = await ef.Database.BeginTransactionAsync(cancellationToken);

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.Status != AppointmentStatus.Present)
        {
            throw new InvalidOperationException(
                $"Only Present patients can be called (current: {appointment.Status}).");
        }

        if (appointment.RealNumber is null)
        {
            throw new InvalidOperationException(
                "Cannot call a patient who is still waiting for an urgent-ratio real number.");
        }

        var session = await _db.QueueSessions
            .FirstAsync(s => s.Id == appointment.QueueSessionId, cancellationToken);

        if (session.Status == QueueSessionStatus.Paused)
        {
            throw new InvalidOperationException("Queue session is paused.");
        }

        if (session.Status == QueueSessionStatus.Closed)
        {
            throw new InvalidOperationException("Queue session is closed.");
        }

        appointment.Status = AppointmentStatus.Inside;
        appointment.CalledAt = DateTime.UtcNow;

        _db.OutboxNotifications.Add(new OutboxNotification
        {
            ClinicId = command.ClinicId,
            RecipientUserId = appointment.BookedByUserId,
            Type = NotificationType.YourTurn,
            PayloadJson = JsonSerializer.Serialize(new
            {
                appointmentId = appointment.Id,
                realNumber = appointment.RealNumber,
                instruction = "yourTurn"
            }, QueueNotificationRules.CamelCaseJson),
            Status = OutboxNotificationStatus.Pending
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new CallNextResult(appointment.Id, appointment.Status);
    }
}
