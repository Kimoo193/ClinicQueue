using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Emergency;

public sealed record ResumeQueueSessionCommand(
    Guid ClinicId,
    Guid QueueSessionId,
    Guid ActorUserId);

public interface IResumeQueueSessionHandler
{
    Task<ResumeQueueSessionResponse> HandleAsync(
        ResumeQueueSessionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resumes a paused session from the stop point (positions unchanged) and notifies
/// waiting patients (FR-052, FR-053, FR-054, FR-073).
/// </summary>
public sealed class ResumeQueueSessionHandler : IResumeQueueSessionHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public ResumeQueueSessionHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ResumeQueueSessionResponse> HandleAsync(
        ResumeQueueSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.QueueSessions
            .FirstOrDefaultAsync(
                s => s.Id == command.QueueSessionId && s.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Queue session not found.");

        if (session.Status != QueueSessionStatus.Paused)
        {
            throw new InvalidOperationException("Only a paused queue session can be resumed.");
        }

        session.Status = QueueSessionStatus.Open;
        session.PauseReason = null;
        session.PausedAt = null;
        session.PausedByUserId = null;

        await PauseResumeNotifications.EnqueueAsync(
            _db,
            command.ClinicId,
            session.Id,
            action: "resume",
            reason: null,
            cancellationToken);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "QueueResume",
            nameof(QueueSession),
            session.Id,
            JsonSerializer.Serialize(new
            {
                resumedAt = DateTime.UtcNow
            }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new ResumeQueueSessionResponse(session.Id, session.Status.ToString());
    }
}
