using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.DayClose;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Safety net for FR-065: auto-carries confirmed-deposit no-shows that remain Booked in a Closed
/// session (e.g. a session closed out-of-band) to the next operating day, without manual action.
/// Normal day-close already carries them, so this job is idempotent and usually a no-op.
/// </summary>
public sealed class DepositCarryOverJob
{
    private readonly IAppDbContext _db;
    private readonly ICarryOverService _carryOver;
    private readonly ILogger<DepositCarryOverJob> _logger;

    public DepositCarryOverJob(
        IAppDbContext db,
        ICarryOverService carryOver,
        ILogger<DepositCarryOverJob> logger)
    {
        _db = db;
        _carryOver = carryOver;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var noShows = await (
            from a in _db.Appointments
            join s in _db.QueueSessions on a.QueueSessionId equals s.Id
            where s.Status == QueueSessionStatus.Closed
                  && a.Method == AppointmentMethod.PhoneDeposit
                  && a.DepositConfirmedAt != null
                  && a.Status == AppointmentStatus.Booked
            select new { Appointment = a, Session = s }
        ).ToListAsync(cancellationToken);

        if (noShows.Count == 0)
        {
            return;
        }

        foreach (var group in noShows.GroupBy(x => x.Session))
        {
            var session = group.Key;

            var closing = await _db.Appointments
                .Where(a => a.QueueSessionId == session.Id
                            && a.Status != AppointmentStatus.Completed
                            && a.Status != AppointmentStatus.Cancelled)
                .ToListAsync(cancellationToken);

            var nextSession = await _carryOver.GetOrCreateOpenSessionAsync(
                session.ClinicId,
                session.OperatingDate.AddDays(1),
                cancellationToken);

            foreach (var item in group.OrderBy(x => x.Appointment.TemporaryNumber))
            {
                _carryOver.CarryOver(item.Appointment, nextSession, closing);
                _logger.LogInformation(
                    "Deposit no-show {AppointmentId} auto-carried from closed session {SessionId}",
                    item.Appointment.Id,
                    session.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
