using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Numbering;

public sealed record AssignRealNumberCommand(Guid AppointmentId, Guid ClinicId);

public sealed record AssignRealNumberResult(
    Guid AppointmentId,
    int? RealNumber,
    AppointmentStatus Status,
    bool WaitingForUrgentRatio);

public interface IAssignRealNumberHandler
{
    Task<AssignRealNumberResult> HandleAsync(
        AssignRealNumberCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Transactional real-number assignment with <c>SELECT … FOR UPDATE</c> on the
/// session row + appointment <see cref="ClinicQueue.Domain.Common.ClinicScopedEntity.RowVersion"/>
/// (constitution Principle II, FR-022, FR-030).
/// </summary>
public sealed class AssignRealNumberHandler : IAssignRealNumberHandler
{
    private readonly IAppDbContext _db;
    private readonly IUrgentInjectionService _injection;
    private readonly IQueueNotificationRules _notificationRules;
    private readonly IClinicAccessGate _accessGate;

    public AssignRealNumberHandler(
        IAppDbContext db,
        IUrgentInjectionService injection,
        IQueueNotificationRules notificationRules,
        IClinicAccessGate accessGate)
    {
        _db = db;
        _injection = injection;
        _notificationRules = notificationRules;
        _accessGate = accessGate;
    }

    public async Task<AssignRealNumberResult> HandleAsync(
        AssignRealNumberCommand command,
        CancellationToken cancellationToken = default)
    {
        await _accessGate.EnsureClinicActiveAsync(command.ClinicId, cancellationToken);

        if (_db is not DbContext ef)
        {
            throw new InvalidOperationException("AssignRealNumber requires an EF Core DbContext.");
        }

        await using var tx = await ef.Database.BeginTransactionAsync(cancellationToken);

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.RealNumber is not null)
        {
            throw new InvalidOperationException("Real number already assigned.");
        }

        if (appointment.Status is not AppointmentStatus.Booked
            and not AppointmentStatus.Present)
        {
            throw new InvalidOperationException(
                $"Cannot confirm arrival for status {appointment.Status}.");
        }

        // Present without RealNumber is only valid for Urgents waiting on the ratio.
        if (appointment.Status == AppointmentStatus.Present
            && appointment.Type != AppointmentType.Urgent)
        {
            throw new InvalidOperationException("Arrival already confirmed.");
        }

        await QueueSessionLock.AcquireAsync(ef, appointment.QueueSessionId, cancellationToken);

        var session = await _db.QueueSessions
            .FirstAsync(s => s.Id == appointment.QueueSessionId, cancellationToken);

        if (session.Status == QueueSessionStatus.Closed)
        {
            throw new InvalidOperationException("Queue session is closed.");
        }

        if (session.Status == QueueSessionStatus.Paused)
        {
            throw new InvalidOperationException("Queue session is paused; confirmations are blocked.");
        }

        var clinic = await _db.Clinics
            .FirstAsync(c => c.Id == command.ClinicId, cancellationToken);

        var previousInstructions = await _notificationRules.SnapshotBookedInstructionsAsync(
            command.ClinicId,
            session.Id,
            clinic.HeadToClinicThreshold,
            cancellationToken);

        var highest = await _db.Appointments
            .Where(a => a.QueueSessionId == session.Id && a.RealNumber != null)
            .MaxAsync(a => (int?)a.RealNumber, cancellationToken) ?? 0;

        var decision = _injection.Decide(new UrgentInjectionRequest(
            appointment.Type,
            IsInstantEntry: false,
            session.NormalSinceLastUrgent,
            clinic.UrgentRatio,
            highest));

        appointment.Status = AppointmentStatus.Present;
        session.NormalSinceLastUrgent = decision.NewNormalSinceLastUrgent;

        if (decision.AssignRealNumber)
        {
            appointment.RealNumber = decision.RealNumber;
            highest = decision.RealNumber!.Value;
        }

        // After a Normal confirmation, inject the oldest waiting Urgent if the ratio is met.
        if (appointment.Type == AppointmentType.Normal
            && _injection.ShouldInjectWaitingUrgent(session.NormalSinceLastUrgent, clinic.UrgentRatio))
        {
            highest = await InjectWaitingUrgentAsync(
                session,
                clinic.UrgentRatio,
                highest,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _notificationRules.EnqueueHeadToClinicTransitionsAsync(
            command.ClinicId,
            session.Id,
            clinic.HeadToClinicThreshold,
            previousInstructions,
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new AssignRealNumberResult(
            appointment.Id,
            appointment.RealNumber,
            appointment.Status,
            WaitingForUrgentRatio: appointment.Type == AppointmentType.Urgent
                                   && appointment.RealNumber is null);
    }

    private async Task<int> InjectWaitingUrgentAsync(
        QueueSession session,
        int urgentRatio,
        int highest,
        CancellationToken cancellationToken)
    {
        var waiting = await _db.Appointments
            .Where(a => a.QueueSessionId == session.Id
                        && a.Type == AppointmentType.Urgent
                        && a.Status == AppointmentStatus.Present
                        && a.RealNumber == null
                        && !a.IsInstantEntry)
            .OrderBy(a => a.UpdatedAt ?? a.CreatedAt)
            .ThenBy(a => a.TemporaryNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (waiting is null)
        {
            return highest;
        }

        var inject = _injection.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            session.NormalSinceLastUrgent,
            urgentRatio,
            highest));

        if (!inject.AssignRealNumber)
        {
            return highest;
        }

        waiting.RealNumber = inject.RealNumber;
        session.NormalSinceLastUrgent = inject.NewNormalSinceLastUrgent;
        return inject.RealNumber!.Value;
    }
}

/// <summary>Shared FOR UPDATE lock helper for queue numbering transactions.</summary>
internal static class QueueSessionLock
{
    public static async Task AcquireAsync(
        DbContext db,
        Guid queueSessionId,
        CancellationToken cancellationToken)
    {
        // Constitution Principle II: serialize real-number assignment per session.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM queue_sessions WHERE "Id" = {queueSessionId} FOR UPDATE""",
            cancellationToken);
    }
}
