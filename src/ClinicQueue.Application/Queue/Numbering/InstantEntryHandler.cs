using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Numbering;

public sealed record InstantEntryCommand(
    Guid ClinicId,
    Guid ActorUserId,
    Guid PatientMembershipId,
    string Reason,
    DateOnly Day);

public sealed record InstantEntryResult(
    Guid AppointmentId,
    int RealNumber,
    AppointmentStatus Status);

public interface IInstantEntryHandler
{
    Task<InstantEntryResult> HandleAsync(
        InstantEntryCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Instant Entry: mandatory reason, audit-logged, ratio-neutral real-number insert (FR-031).
/// </summary>
public sealed class InstantEntryHandler : IInstantEntryHandler
{
    private static readonly AppointmentStatus[] ActiveStatuses =
    [
        AppointmentStatus.Booked,
        AppointmentStatus.Present
    ];

    private const string ActiveMembershipIndexHint = "active_membership";

    private readonly IAppDbContext _db;
    private readonly IQueueSessionService _sessions;
    private readonly IUrgentInjectionService _injection;
    private readonly IQueueNotificationRules _notificationRules;
    private readonly IAuditWriter _audit;
    private readonly IClinicAccessGate _accessGate;

    public InstantEntryHandler(
        IAppDbContext db,
        IQueueSessionService sessions,
        IUrgentInjectionService injection,
        IQueueNotificationRules notificationRules,
        IAuditWriter audit,
        IClinicAccessGate accessGate)
    {
        _db = db;
        _sessions = sessions;
        _injection = injection;
        _notificationRules = notificationRules;
        _audit = audit;
        _accessGate = accessGate;
    }

    public async Task<InstantEntryResult> HandleAsync(
        InstantEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException("Instant Entry reason is mandatory (FR-031).", nameof(command));
        }

        await _accessGate.EnsureClinicActiveAsync(command.ClinicId, cancellationToken);

        if (_db is not DbContext ef)
        {
            throw new InvalidOperationException("InstantEntry requires an EF Core DbContext.");
        }

        var membership = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.Id == command.PatientMembershipId
                     && m.ClinicId == command.ClinicId
                     && m.Role == ClinicRole.Patient,
                cancellationToken)
            ?? throw new InvalidOperationException("Patient membership not found.");

        var session = await _sessions.GetOrOpenForBookingAsync(
            command.ClinicId,
            command.Day,
            cancellationToken);

        var duplicate = await _db.Appointments.AnyAsync(
            a => a.ClinicId == command.ClinicId
                 && a.QueueSessionId == session.Id
                 && ActiveStatuses.Contains(a.Status)
                 && a.PatientMembershipId == membership.Id,
            cancellationToken);

        if (duplicate)
        {
            throw new DuplicateActiveBookingException(membership.Id, command.Day);
        }

        await using var tx = await ef.Database.BeginTransactionAsync(cancellationToken);
        await QueueSessionLock.AcquireAsync(ef, session.Id, cancellationToken);

        // Re-load session under lock.
        session = await _db.QueueSessions.FirstAsync(s => s.Id == session.Id, cancellationToken);
        if (session.Status is QueueSessionStatus.Closed or QueueSessionStatus.Paused)
        {
            throw new InvalidOperationException($"Queue session is {session.Status}; Instant Entry blocked.");
        }

        var clinic = await _db.Clinics.FirstAsync(c => c.Id == command.ClinicId, cancellationToken);
        var previousInstructions = await _notificationRules.SnapshotBookedInstructionsAsync(
            command.ClinicId,
            session.Id,
            clinic.HeadToClinicThreshold,
            cancellationToken);

        var highest = await _db.Appointments
            .Where(a => a.QueueSessionId == session.Id && a.RealNumber != null)
            .MaxAsync(a => (int?)a.RealNumber, cancellationToken) ?? 0;

        var nextTemp = await _db.Appointments
            .Where(a => a.QueueSessionId == session.Id)
            .Select(a => (int?)a.TemporaryNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var decision = _injection.Decide(new UrgentInjectionRequest(
            AppointmentType.Normal,
            IsInstantEntry: true,
            session.NormalSinceLastUrgent,
            clinic.UrgentRatio,
            highest));

        // Ratio-neutral: counter must stay unchanged.
        session.NormalSinceLastUrgent = decision.NewNormalSinceLastUrgent;

        var appointment = new Appointment
        {
            ClinicId = command.ClinicId,
            QueueSessionId = session.Id,
            PatientMembershipId = membership.Id,
            BookedByUserId = membership.UserId,
            Type = AppointmentType.Normal,
            Method = AppointmentMethod.WalkIn,
            TemporaryNumber = nextTemp + 1,
            RealNumber = decision.RealNumber,
            Status = AppointmentStatus.Present,
            IsInstantEntry = true,
            InstantEntryReason = command.Reason.Trim()
        };

        _db.Appointments.Add(appointment);

        _audit.Append(
            command.ClinicId,
            command.ActorUserId,
            "InstantEntry",
            nameof(Appointment),
            appointment.Id,
            JsonSerializer.Serialize(new
            {
                reason = appointment.InstantEntryReason,
                realNumber = appointment.RealNumber,
                patientMembershipId = membership.Id
            }, QueueNotificationRules.CamelCaseJson));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            PostgresUniqueViolation.Matches(ex, ActiveMembershipIndexHint))
        {
            // The pre-lock duplicate check has a race window; the partial unique index is the
            // real guard. Translate it to the same 409 the endpoint maps for booking (FR-041a).
            throw new DuplicateActiveBookingException(membership.Id, command.Day);
        }

        await _notificationRules.EnqueueHeadToClinicTransitionsAsync(
            command.ClinicId,
            session.Id,
            clinic.HeadToClinicThreshold,
            previousInstructions,
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new InstantEntryResult(
            appointment.Id,
            appointment.RealNumber!.Value,
            appointment.Status);
    }
}

/// <summary>
/// Application-layer audit append that participates in the caller's DbContext unit of work
/// (same transaction as Instant Entry). Implemented by Infrastructure.
/// </summary>
public interface IAuditWriter
{
    void Append(
        Guid clinicId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? metadataJson = null);
}
