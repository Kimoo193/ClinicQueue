using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.DayClose;

public sealed record CarryOverOutcome(Guid NewAppointmentId, int NextDayPosition);

/// <summary>
/// Shared carry-over core (FR-062–FR-065): re-enters an unserved appointment at the front of
/// the next day's queue, preserving prior-day relative order, and notifies the patient (FR-074).
/// Stages changes on the caller's unit of work — the caller owns SaveChanges/transaction.
/// </summary>
public interface ICarryOverService
{
    /// <summary>Returns the next operating day's Open session for the clinic, creating one if absent.</summary>
    Task<QueueSession> GetOrCreateOpenSessionAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a carry-over of <paramref name="source"/> into <paramref name="nextSession"/>.
    /// <paramref name="closingSessionAppointments"/> is the closing session's non-terminal set,
    /// used to derive the stable prior-day rank so batch and per-appointment carries agree.
    /// </summary>
    CarryOverOutcome CarryOver(
        Appointment source,
        QueueSession nextSession,
        IReadOnlyList<Appointment> closingSessionAppointments);
}

public sealed class CarryOverService : ICarryOverService
{
    private readonly IAppDbContext _db;

    public CarryOverService(IAppDbContext db) => _db = db;

    public async Task<QueueSession> GetOrCreateOpenSessionAsync(
        Guid clinicId,
        DateOnly operatingDate,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.QueueSessions
            .FirstOrDefaultAsync(
                s => s.ClinicId == clinicId && s.OperatingDate == operatingDate,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var session = new QueueSession
        {
            ClinicId = clinicId,
            OperatingDate = operatingDate,
            Status = QueueSessionStatus.Open
        };
        _db.QueueSessions.Add(session);
        return session;
    }

    public CarryOverOutcome CarryOver(
        Appointment source,
        QueueSession nextSession,
        IReadOnlyList<Appointment> closingSessionAppointments)
    {
        if (source.Status is AppointmentStatus.Completed
            or AppointmentStatus.Cancelled
            or AppointmentStatus.CarriedOver)
        {
            throw new InvalidOperationException(
                $"Appointment cannot be carried over from status {source.Status}.");
        }

        var position = PriorDayRank(source, closingSessionAppointments);

        var carried = new Appointment
        {
            ClinicId = source.ClinicId,
            QueueSessionId = nextSession.Id,
            PatientMembershipId = source.PatientMembershipId,
            DependentId = source.DependentId,
            BookedByUserId = source.BookedByUserId,
            Type = source.Type,
            Method = source.Method,
            CompanionsCount = source.CompanionsCount,
            PossibleUrgentFlag = source.PossibleUrgentFlag,
            TemporaryNumber = position,
            Status = AppointmentStatus.Booked
        };
        _db.Appointments.Add(carried);

        source.Status = AppointmentStatus.CarriedOver;

        _db.OutboxNotifications.Add(new OutboxNotification
        {
            ClinicId = source.ClinicId,
            RecipientUserId = source.BookedByUserId,
            Type = NotificationType.CarryOver,
            PayloadJson = JsonSerializer.Serialize(new
            {
                previousAppointmentId = source.Id,
                appointmentId = carried.Id,
                operatingDate = nextSession.OperatingDate.ToString("yyyy-MM-dd"),
                nextDayPosition = position
            }, QueueNotificationRules.CamelCaseJson),
            Status = OutboxNotificationStatus.Pending
        });

        return new CarryOverOutcome(carried.Id, position);
    }

    /// <summary>
    /// 1-based standing the appointment held when its day closed: confirmed patients by
    /// <see cref="Appointment.RealNumber"/>, unconfirmed after them by temporary number. Stable
    /// across sequential carries because it counts intrinsic standing, not current status.
    /// </summary>
    private static int PriorDayRank(Appointment source, IReadOnlyList<Appointment> closing)
    {
        static int Standing(Appointment a) => a.RealNumber ?? int.MaxValue;
        var sourceStanding = Standing(source);

        var earlier = closing.Count(a =>
            a.Id != source.Id
            && a.Status != AppointmentStatus.Completed
            && a.Status != AppointmentStatus.Cancelled
            && (Standing(a) < sourceStanding
                || (Standing(a) == sourceStanding && a.TemporaryNumber < source.TemporaryNumber)));

        return earlier + 1;
    }
}
