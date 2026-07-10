using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record ListPatientAppointmentsQuery(
    Guid UserId,
    string? StatusFilter,
    bool UnifiedAcrossClinics = true,
    Guid? ClinicId = null);

public interface IListPatientAppointmentsHandler
{
    Task<IReadOnlyList<PatientAppointmentDto>> HandleAsync(
        ListPatientAppointmentsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Patient appointment history (Upcoming / Completed / Cancelled).
/// Unified mode spans all clinics for one global identity (FR-006, FR-045).
/// </summary>
public sealed class ListPatientAppointmentsHandler : IListPatientAppointmentsHandler
{
    private readonly IAppDbContext _db;
    private readonly ICountAheadService _countAhead;

    public ListPatientAppointmentsHandler(IAppDbContext db, ICountAheadService countAhead)
    {
        _db = db;
        _countAhead = countAhead;
    }

    public async Task<IReadOnlyList<PatientAppointmentDto>> HandleAsync(
        ListPatientAppointmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        var appointmentsQuery = _db.Appointments.AsNoTracking()
            .Where(a => a.BookedByUserId == query.UserId);

        if (!query.UnifiedAcrossClinics)
        {
            if (query.ClinicId is null)
            {
                throw new InvalidOperationException(
                    "ClinicId is required when unified history is disabled.");
            }

            appointmentsQuery = appointmentsQuery.Where(a => a.ClinicId == query.ClinicId.Value);
        }

        appointmentsQuery = ApplyStatusFilter(appointmentsQuery, query.StatusFilter);

        var appointments = await appointmentsQuery
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0)
        {
            return Array.Empty<PatientAppointmentDto>();
        }

        var clinicIds = appointments.Select(a => a.ClinicId).Distinct().ToList();
        var clinics = await _db.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var sessionIds = appointments
            .Where(a => a.Status is AppointmentStatus.Booked
                or AppointmentStatus.Present
                or AppointmentStatus.Inside)
            .Select(a => a.QueueSessionId)
            .Distinct()
            .ToList();

        var peersBySession = new Dictionary<Guid, List<Appointment>>();
        if (sessionIds.Count > 0)
        {
            var peers = await _db.Appointments.AsNoTracking()
                .Where(a => sessionIds.Contains(a.QueueSessionId)
                            && a.Status != AppointmentStatus.Cancelled
                            && a.Status != AppointmentStatus.Completed
                            && a.Status != AppointmentStatus.Absent
                            && a.Status != AppointmentStatus.CarriedOver)
                .ToListAsync(cancellationToken);

            peersBySession = peers
                .GroupBy(a => a.QueueSessionId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        return appointments.Select(appointment =>
        {
            var threshold = clinics.TryGetValue(appointment.ClinicId, out var clinic)
                ? clinic.HeadToClinicThreshold
                : 4;
            return ToDto(appointment, threshold, peersBySession);
        }).ToList();
    }

    private static IQueryable<Appointment> ApplyStatusFilter(
        IQueryable<Appointment> query,
        string? status)
    {
        var filter = (status ?? "upcoming").Trim().ToLowerInvariant();
        return filter switch
        {
            "completed" => query.Where(a => a.Status == AppointmentStatus.Completed),
            "cancelled" => query.Where(a => a.Status == AppointmentStatus.Cancelled),
            _ => query.Where(a => a.Status == AppointmentStatus.Booked
                                  || a.Status == AppointmentStatus.Present
                                  || a.Status == AppointmentStatus.Inside
                                  || a.Status == AppointmentStatus.CarriedOver)
        };
    }

    private PatientAppointmentDto ToDto(
        Appointment appointment,
        int headToClinicThreshold,
        IReadOnlyDictionary<Guid, List<Appointment>> peersBySession)
    {
        int? ahead = null;
        string? instruction = null;
        if (appointment.Status is AppointmentStatus.Booked
                or AppointmentStatus.Present
                or AppointmentStatus.Inside
            && peersBySession.TryGetValue(appointment.QueueSessionId, out var peers))
        {
            var position = _countAhead.ComputeFromSnapshot(
                appointment, peers, headToClinicThreshold);
            ahead = position.CountAhead;
            instruction = position.Instruction == QueueInstruction.WaitHome
                ? "waitHome"
                : "headToClinic";
        }

        return new PatientAppointmentDto(
            appointment.Id,
            appointment.ClinicId,
            appointment.Status.ToString(),
            appointment.TemporaryNumber,
            appointment.RealNumber,
            ahead,
            instruction,
            appointment.DependentId);
    }
}
