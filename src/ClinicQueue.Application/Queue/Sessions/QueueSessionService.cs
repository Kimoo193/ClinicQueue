using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Sessions;

public interface IQueueSessionService
{
    /// <summary>
    /// Returns today's open (or paused) session for the clinic, creating an Open session
    /// for the operating day when none exists (FR-020). Throws when the day's session is Closed.
    /// New session creation is blocked when the subscription is expired (FR-102).
    /// </summary>
    Task<QueueSession> GetOrOpenForBookingAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default);

    Task<QueueSession?> GetForDateAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default);
}

public sealed class QueueSessionService : IQueueSessionService
{
    private const int MaxOpenAttempts = 3;

    private readonly IAppDbContext _db;
    private readonly IClinicAccessGate _accessGate;

    public QueueSessionService(IAppDbContext db, IClinicAccessGate accessGate)
    {
        _db = db;
        _accessGate = accessGate;
    }

    public async Task<QueueSession?> GetForDateAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default)
    {
        return await _db.QueueSessions
            .FirstOrDefaultAsync(
                s => s.ClinicId == clinicId && s.OperatingDate == operatingDate,
                cancellationToken);
    }

    public async Task<QueueSession> GetOrOpenForBookingAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxOpenAttempts; attempt++)
        {
            var session = await GetForDateAsync(clinicId, operatingDate, cancellationToken);
            if (session is not null)
            {
                return EnsureBookable(session, operatingDate);
            }

            // New QueueSession creation blocked when subscription expired (FR-102).
            await _accessGate.EnsureCanAcceptNewBookingsAsync(clinicId, cancellationToken);

            session = new QueueSession
            {
                ClinicId = clinicId,
                OperatingDate = operatingDate,
                Status = QueueSessionStatus.Open,
                NormalSinceLastUrgent = 0
            };
            _db.QueueSessions.Add(session);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return session;
            }
            catch (DbUpdateException) when (attempt < MaxOpenAttempts)
            {
                // Concurrent first-open for the same day — unique (ClinicId, OperatingDate).
                _db.QueueSessions.Remove(session);
            }
        }

        var raced = await GetForDateAsync(clinicId, operatingDate, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Could not open queue session for {operatingDate:yyyy-MM-dd}.");
        return EnsureBookable(raced, operatingDate);
    }

    private static QueueSession EnsureBookable(QueueSession session, DateOnly operatingDate)
    {
        if (session.Status == QueueSessionStatus.Closed)
        {
            throw new InvalidOperationException(
                $"Queue session for {operatingDate:yyyy-MM-dd} is closed; booking is blocked (FR-020).");
        }

        return session;
    }
}
