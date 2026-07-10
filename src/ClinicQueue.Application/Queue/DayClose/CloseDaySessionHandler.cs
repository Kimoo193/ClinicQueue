using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.DayClose;

public sealed record CloseDaySessionCommand(
    Guid ClinicId,
    Guid QueueSessionId,
    Guid ActorUserId);

public interface ICloseDaySessionHandler
{
    Task<CloseDaySessionResponse> HandleAsync(
        CloseDaySessionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Closes the day's session, lists remaining unserved patients (FR-060, FR-061), and
/// auto-carries deposit-paid no-shows to the next day at close (FR-065).
/// </summary>
public sealed class CloseDaySessionHandler : ICloseDaySessionHandler
{
    private readonly IAppDbContext _db;
    private readonly ICarryOverService _carryOver;
    private readonly IAuditWriter _audit;

    public CloseDaySessionHandler(IAppDbContext db, ICarryOverService carryOver, IAuditWriter audit)
    {
        _db = db;
        _carryOver = carryOver;
        _audit = audit;
    }

    public async Task<CloseDaySessionResponse> HandleAsync(
        CloseDaySessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.QueueSessions
            .FirstOrDefaultAsync(
                s => s.Id == command.QueueSessionId && s.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Queue session not found.");

        if (session.Status == QueueSessionStatus.Closed)
        {
            throw new InvalidOperationException("Queue session is already closed.");
        }

        session.Status = QueueSessionStatus.Closed;

        // Rank basis = every appointment that held a prior-day standing (all but Completed/Cancelled),
        // so carried rows stay counted and later carries keep a stable, collision-free order.
        var closing = await _db.Appointments
            .Where(a => a.QueueSessionId == session.Id
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var autoCarried = await AutoCarryDepositNoShowsAsync(session, closing, cancellationToken);

        var unserved = closing
            .Where(a => !autoCarried.Contains(a.Id) && DayCloseStatuses.IsUnserved(a.Status))
            .OrderBy(a => a.RealNumber ?? int.MaxValue)
            .ThenBy(a => a.TemporaryNumber)
            .Select(ToUnservedDto)
            .ToList();

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "QueueClose",
            nameof(QueueSession),
            session.Id,
            JsonSerializer.Serialize(new
            {
                unservedCount = unserved.Count,
                autoCarriedCount = autoCarried.Count
            }, QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new CloseDaySessionResponse(
            session.Id,
            session.Status.ToString(),
            autoCarried.Count,
            unserved);
    }

    private async Task<HashSet<Guid>> AutoCarryDepositNoShowsAsync(
        QueueSession session,
        IReadOnlyList<Appointment> closing,
        CancellationToken cancellationToken)
    {
        var depositNoShows = closing
            .Where(a => a.Method == AppointmentMethod.PhoneDeposit
                        && a.DepositConfirmedAt is not null
                        && a.Status == AppointmentStatus.Booked)
            .OrderBy(a => a.RealNumber ?? int.MaxValue)
            .ThenBy(a => a.TemporaryNumber)
            .ToList();

        var carried = new HashSet<Guid>();
        if (depositNoShows.Count == 0)
        {
            return carried;
        }

        var nextSession = await _carryOver.GetOrCreateOpenSessionAsync(
            session.ClinicId,
            session.OperatingDate.AddDays(1),
            cancellationToken);

        foreach (var noShow in depositNoShows)
        {
            _carryOver.CarryOver(noShow, nextSession, closing);
            carried.Add(noShow.Id);
        }

        return carried;
    }

    private static UnservedAppointmentDto ToUnservedDto(Appointment a) =>
        new(
            a.Id,
            a.PatientMembershipId,
            a.DependentId,
            a.Type.ToString(),
            a.Method.ToString(),
            a.Status.ToString(),
            a.TemporaryNumber,
            a.RealNumber,
            a.DepositConfirmedAt is not null);
}
