using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Positioning;

public interface ICountAheadService
{
    Task<CountAheadResult> ComputeForAppointmentAsync(
        Guid appointmentId,
        int headToClinicThreshold,
        CancellationToken cancellationToken = default);

    CountAheadResult ComputeFromSnapshot(
        Appointment subject,
        IReadOnlyList<Appointment> sessionAppointments,
        int headToClinicThreshold);
}

public sealed record CountAheadResult(
    Guid AppointmentId,
    int CountAhead,
    QueueInstruction Instruction);

/// <summary>
/// Count-ahead for live tracking (FR-025). Confirmed patients use real-number order;
/// booked patients count confirmed peers plus lower temporary numbers.
/// </summary>
public sealed class CountAheadService : ICountAheadService
{
    private readonly IAppDbContext _db;

    public CountAheadService(IAppDbContext db) => _db = db;

    public async Task<CountAheadResult> ComputeForAppointmentAsync(
        Guid appointmentId,
        int headToClinicThreshold,
        CancellationToken cancellationToken = default)
    {
        var subject = await _db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Appointment {appointmentId} not found.");

        var peers = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.QueueSessionId == subject.QueueSessionId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Absent
                        && a.Status != AppointmentStatus.CarriedOver)
            .ToListAsync(cancellationToken);

        return ComputeFromSnapshot(subject, peers, headToClinicThreshold);
    }

    public CountAheadResult ComputeFromSnapshot(
        Appointment subject,
        IReadOnlyList<Appointment> sessionAppointments,
        int headToClinicThreshold)
    {
        var active = sessionAppointments
            .Where(a => a.Status is AppointmentStatus.Booked or AppointmentStatus.Present or AppointmentStatus.Inside)
            .ToList();

        int countAhead;
        if (subject.RealNumber is { } real)
        {
            countAhead = active.Count(a =>
                a.Id != subject.Id
                && a.RealNumber is { } otherReal
                && otherReal < real
                && a.Status is AppointmentStatus.Present or AppointmentStatus.Inside);
        }
        else
        {
            var confirmedAhead = active.Count(a =>
                a.Id != subject.Id
                && a.Status is AppointmentStatus.Present or AppointmentStatus.Inside);

            var bookedAhead = active.Count(a =>
                a.Id != subject.Id
                && a.Status == AppointmentStatus.Booked
                && a.TemporaryNumber < subject.TemporaryNumber);

            countAhead = confirmedAhead + bookedAhead;
        }

        var instruction = ThresholdLabeling.Label(countAhead, headToClinicThreshold);
        return new CountAheadResult(subject.Id, countAhead, instruction);
    }
}
