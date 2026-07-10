using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record AbsenceFlagResult(
    Guid MembershipId,
    int AbsenceCount,
    int Threshold,
    bool IsFlagged,
    string MembershipStatus);

public sealed record RestrictBookingResult(
    Guid MembershipId,
    string Status);

public interface IAbsencePolicyService
{
    /// <summary>
    /// Counts no-show absences (FR-047): Absent, or CarriedOver after being called,
    /// without a prior cancellation. Flagged when count ≥ clinic threshold (default 2).
    /// </summary>
    Task<AbsenceFlagResult> EvaluateAsync(
        Guid clinicId,
        Guid membershipId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-evaluate flags for today's queue rows (same clinic). Missing memberships are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AbsenceFlagResult>> EvaluateManyAsync(
        Guid clinicId,
        IEnumerable<Guid> membershipIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receptionist restricts future booking for a flagged (or any) patient membership (FR-047).
    /// </summary>
    Task<RestrictBookingResult> RestrictBookingAsync(
        Guid clinicId,
        Guid membershipId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>Repeated-absence flag + booking restriction (FR-047).</summary>
public sealed class AbsencePolicyService : IAbsencePolicyService
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public AbsencePolicyService(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AbsenceFlagResult> EvaluateAsync(
        Guid clinicId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        var map = await EvaluateManyAsync(clinicId, [membershipId], cancellationToken);
        return map.TryGetValue(membershipId, out var result)
            ? result
            : throw new InvalidOperationException("Patient membership not found.");
    }

    public async Task<IReadOnlyDictionary<Guid, AbsenceFlagResult>> EvaluateManyAsync(
        Guid clinicId,
        IEnumerable<Guid> membershipIds,
        CancellationToken cancellationToken = default)
    {
        var ids = membershipIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AbsenceFlagResult>();
        }

        var clinic = await _db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        var threshold = clinic.RepeatedAbsenceThreshold > 0
            ? clinic.RepeatedAbsenceThreshold
            : 2;

        var memberships = await _db.ClinicMemberships.AsNoTracking()
            .Where(m => m.ClinicId == clinicId
                        && m.Role == ClinicRole.Patient
                        && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Status })
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return new Dictionary<Guid, AbsenceFlagResult>();
        }

        var membershipIdSet = memberships.Select(m => m.Id).ToHashSet();

        // Absent no-shows, plus carried-over rows that had been called (auto-absence then carry).
        var absenceRows = await _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.PatientMembershipId != null
                        && membershipIdSet.Contains(a.PatientMembershipId.Value)
                        && (a.Status == AppointmentStatus.Absent
                            || (a.Status == AppointmentStatus.CarriedOver && a.CalledAt != null)))
            .GroupBy(a => a.PatientMembershipId!.Value)
            .Select(g => new { MembershipId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = absenceRows.ToDictionary(x => x.MembershipId, x => x.Count);

        return memberships.ToDictionary(
            m => m.Id,
            m =>
            {
                var count = counts.GetValueOrDefault(m.Id);
                return new AbsenceFlagResult(
                    m.Id,
                    count,
                    threshold,
                    count >= threshold,
                    m.Status.ToString());
            });
    }

    public async Task<RestrictBookingResult> RestrictBookingAsync(
        Guid clinicId,
        Guid membershipId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var membership = await _db.ClinicMemberships
            .FirstOrDefaultAsync(
                m => m.Id == membershipId
                     && m.ClinicId == clinicId
                     && m.Role == ClinicRole.Patient,
                cancellationToken)
            ?? throw new InvalidOperationException("Patient membership not found.");

        if (membership.Status == MembershipStatus.Restricted)
        {
            return new RestrictBookingResult(membership.Id, membership.Status.ToString());
        }

        membership.Status = MembershipStatus.Restricted;

        _audit.Append(
            clinicId,
            actorUserId,
            "BookingRestrict",
            nameof(ClinicMembership),
            membership.Id,
            JsonSerializer.Serialize(
                new { membershipId = membership.Id, status = MembershipStatus.Restricted.ToString() },
                QueueNotificationRules.CamelCaseJson));

        await _db.SaveChangesAsync(cancellationToken);

        return new RestrictBookingResult(membership.Id, membership.Status.ToString());
    }
}
