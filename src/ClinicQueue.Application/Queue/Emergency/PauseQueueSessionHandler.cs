using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Emergency;

public sealed record PauseQueueSessionCommand(
    Guid ClinicId,
    Guid QueueSessionId,
    Guid ActorUserId,
    string Reason);

public interface IPauseQueueSessionHandler
{
    Task<PauseQueueSessionResponse> HandleAsync(
        PauseQueueSessionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pauses an open queue session: positions held, all Booked/Present patients notified,
/// pause audited (FR-050, FR-051, FR-054, FR-073).
/// </summary>
public sealed class PauseQueueSessionHandler : IPauseQueueSessionHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public PauseQueueSessionHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PauseQueueSessionResponse> HandleAsync(
        PauseQueueSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException("Pause reason is required (FR-050).", nameof(command));
        }

        var session = await _db.QueueSessions
            .FirstOrDefaultAsync(
                s => s.Id == command.QueueSessionId && s.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Queue session not found.");

        if (session.Status == QueueSessionStatus.Closed)
        {
            throw new InvalidOperationException("Closed queue session cannot be paused.");
        }

        if (session.Status == QueueSessionStatus.Paused)
        {
            throw new InvalidOperationException("Queue session is already paused.");
        }

        var pausedAt = DateTime.UtcNow;
        session.Status = QueueSessionStatus.Paused;
        session.PauseReason = command.Reason.Trim();
        session.PausedAt = pausedAt;
        session.PausedByUserId = command.ActorUserId;

        await PauseResumeNotifications.EnqueueAsync(
            _db,
            command.ClinicId,
            session.Id,
            action: "pause",
            reason: session.PauseReason,
            cancellationToken);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "QueuePause",
            nameof(QueueSession),
            session.Id,
            JsonSerializer.Serialize(new
            {
                reason = session.PauseReason,
                pausedAt
            }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new PauseQueueSessionResponse(session.Id, session.Status.ToString(), pausedAt);
    }
}
