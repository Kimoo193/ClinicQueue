using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record CreateAppointmentCommand(
    Guid ClinicId,
    Guid BookedByUserId,
    DateOnly Day,
    AppointmentMethod Method,
    bool PossibleUrgentFlag,
    Guid? DependentId,
    string? DepositProofUrl = null);

public sealed record CreateAppointmentResult(
    Guid AppointmentId,
    int TemporaryNumber,
    Guid QueueSessionId);

public interface ICreateAppointmentHandler
{
    Task<CreateAppointmentResult> HandleAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class CreateAppointmentHandler : ICreateAppointmentHandler
{
    private const int MaxTemporaryNumberAttempts = 3;
    private const string TemporaryNumberConstraintHint = "TemporaryNumber";
    private const string ActiveMembershipIndexHint = "active_membership";
    private const string ActiveDependentIndexHint = "active_dependent";

    private static readonly AppointmentStatus[] ActiveStatuses =
    [
        AppointmentStatus.Booked,
        AppointmentStatus.Present
    ];

    private readonly IAppDbContext _db;
    private readonly IQueueSessionService _sessions;
    private readonly IQueueNotificationRules _notificationRules;
    private readonly IClinicAccessGate _accessGate;

    public CreateAppointmentHandler(
        IAppDbContext db,
        IQueueSessionService sessions,
        IQueueNotificationRules notificationRules,
        IClinicAccessGate accessGate)
    {
        _db = db;
        _sessions = sessions;
        _notificationRules = notificationRules;
        _accessGate = accessGate;
    }

    public async Task<CreateAppointmentResult> HandleAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        await _accessGate.EnsureCanAcceptNewBookingsAsync(command.ClinicId, cancellationToken);

        var clinic = await _db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.ClinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        EnsureBookingDayIsClinicToday(clinic.Timezone, command.Day);

        var membershipId = await ResolvePatientMembershipIdAsync(command, cancellationToken);
        var patientOfRecordKey = command.DependentId ?? membershipId
            ?? throw new InvalidOperationException("Patient of record could not be resolved.");

        var session = await _sessions.GetOrOpenForBookingAsync(
            command.ClinicId,
            command.Day,
            cancellationToken);

        await EnsureNoDuplicateActiveBookingAsync(
            command,
            session.Id,
            membershipId,
            patientOfRecordKey,
            cancellationToken);

        return await PersistAppointmentWithTemporaryNumberAsync(
            command,
            session.Id,
            membershipId,
            patientOfRecordKey,
            clinic.HeadToClinicThreshold,
            cancellationToken);
    }

    private static void EnsureBookingDayIsClinicToday(string timezone, DateOnly day)
    {
        var today = ClinicClock.Today(timezone);
        if (day != today)
        {
            throw new InvalidOperationException(
                $"Booking day must be clinic today ({today:yyyy-MM-dd} in {timezone}).");
        }
    }

    private async Task<Guid?> ResolvePatientMembershipIdAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.DependentId is { } dependentId)
        {
            _ = await _db.Dependents
                .FirstOrDefaultAsync(
                    d => d.Id == dependentId
                         && d.ClinicId == command.ClinicId
                         && d.GuardianUserId == command.BookedByUserId,
                    cancellationToken)
                ?? throw new InvalidOperationException("Dependent not found for this guardian/clinic.");
            return null;
        }

        var membership = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.ClinicId == command.ClinicId
                     && m.UserId == command.BookedByUserId
                     && m.Role == ClinicRole.Patient
                     && m.Status != MembershipStatus.Restricted,
                cancellationToken)
            ?? throw new InvalidOperationException("Active patient membership required to book.");

        if (membership.Status != MembershipStatus.Active)
        {
            throw new InvalidOperationException("Booking is restricted for this patient.");
        }

        return membership.Id;
    }

    private async Task EnsureNoDuplicateActiveBookingAsync(
        CreateAppointmentCommand command,
        Guid sessionId,
        Guid? membershipId,
        Guid patientOfRecordKey,
        CancellationToken cancellationToken)
    {
        var duplicate = await _db.Appointments.AnyAsync(
            a => a.ClinicId == command.ClinicId
                 && a.QueueSessionId == sessionId
                 && ActiveStatuses.Contains(a.Status)
                 && ((command.DependentId != null && a.DependentId == command.DependentId)
                     || (command.DependentId == null && a.PatientMembershipId == membershipId)),
            cancellationToken);

        if (duplicate)
        {
            throw new DuplicateActiveBookingException(patientOfRecordKey, command.Day);
        }
    }

    private async Task<CreateAppointmentResult> PersistAppointmentWithTemporaryNumberAsync(
        CreateAppointmentCommand command,
        Guid sessionId,
        Guid? membershipId,
        Guid patientOfRecordKey,
        int headToClinicThreshold,
        CancellationToken cancellationToken)
    {
        // Snapshot peer instructions before insert so FR-071 can detect WaitHome → HeadToClinic
        // transitions caused by this booking (and notify the new booker when already in range).
        var previousInstructions = await _notificationRules.SnapshotBookedInstructionsAsync(
            command.ClinicId,
            sessionId,
            headToClinicThreshold,
            cancellationToken);

        // Unique (QueueSessionId, TemporaryNumber) can collide under concurrent booking;
        // retry with a fresh Max rather than returning a 500.
        for (var attempt = 1; attempt <= MaxTemporaryNumberAttempts; attempt++)
        {
            var nextTemp = await _db.Appointments
                .Where(a => a.QueueSessionId == sessionId)
                .Select(a => (int?)a.TemporaryNumber)
                .MaxAsync(cancellationToken) ?? 0;

            var appointment = new Appointment
            {
                ClinicId = command.ClinicId,
                QueueSessionId = sessionId,
                PatientMembershipId = membershipId,
                DependentId = command.DependentId,
                BookedByUserId = command.BookedByUserId,
                Type = AppointmentType.Normal,
                PossibleUrgentFlag = command.PossibleUrgentFlag,
                Method = command.Method,
                TemporaryNumber = nextTemp + 1,
                Status = AppointmentStatus.Booked,
                DepositProofUrl = command.Method == AppointmentMethod.PhoneDeposit
                    ? command.DepositProofUrl
                    : null
            };

            var outbox = new OutboxNotification
            {
                ClinicId = command.ClinicId,
                RecipientUserId = command.BookedByUserId,
                Type = NotificationType.BookingConfirmation,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    appointmentId = appointment.Id,
                    temporaryNumber = appointment.TemporaryNumber,
                    day = command.Day.ToString("yyyy-MM-dd"),
                    clinicId = command.ClinicId
                }, QueueNotificationRules.CamelCaseJson),
                Status = OutboxNotificationStatus.Pending
            };

            _db.Appointments.Add(appointment);
            _db.OutboxNotifications.Add(outbox);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);

                await _notificationRules.EnqueueHeadToClinicTransitionsAsync(
                    command.ClinicId,
                    sessionId,
                    headToClinicThreshold,
                    previousInstructions,
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);

                return new CreateAppointmentResult(appointment.Id, appointment.TemporaryNumber, sessionId);
            }
            catch (DbUpdateException ex) when (
                PostgresUniqueViolation.Matches(ex, ActiveMembershipIndexHint)
                || PostgresUniqueViolation.Matches(ex, ActiveDependentIndexHint))
            {
                DetachFailedBookingAttempt(appointment, outbox);
                throw new DuplicateActiveBookingException(patientOfRecordKey, command.Day);
            }
            catch (DbUpdateException ex) when (
                attempt < MaxTemporaryNumberAttempts
                && PostgresUniqueViolation.Matches(ex, TemporaryNumberConstraintHint))
            {
                DetachFailedBookingAttempt(appointment, outbox);
            }
        }

        throw new InvalidOperationException("Could not assign a temporary number; please retry.");
    }

    private void DetachFailedBookingAttempt(Appointment appointment, OutboxNotification outbox)
    {
        // Prefer Detach over Remove — these rows were never committed.
        if (_db is DbContext ctx)
        {
            ctx.Entry(appointment).State = EntityState.Detached;
            ctx.Entry(outbox).State = EntityState.Detached;
            return;
        }

        _db.Appointments.Remove(appointment);
        _db.OutboxNotifications.Remove(outbox);
    }
}

public sealed class DuplicateActiveBookingException : Exception
{
    public Guid PatientOfRecordKey { get; }
    public DateOnly Day { get; }

    public DuplicateActiveBookingException(Guid patientOfRecordKey, DateOnly day)
        : base("An active appointment already exists for this patient on the requested day (FR-041a).")
    {
        PatientOfRecordKey = patientOfRecordKey;
        Day = day;
    }
}
