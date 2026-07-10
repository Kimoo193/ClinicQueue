using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Platform;

/// <summary>
/// Enforces clinic lifecycle (FR-100) and subscription write-restriction with
/// open-session grace (FR-102).
/// </summary>
public interface IClinicAccessGate
{
    /// <summary>Blocks staff/patient write ops when the clinic is Suspended (FR-100).</summary>
    Task EnsureClinicActiveAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocks new bookings and new QueueSession creation when the clinic is suspended
    /// or the subscription is expired (FR-102).
    /// </summary>
    Task EnsureCanAcceptNewBookingsAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the clinic has an Active subscription that has not passed RenewalDate.
    /// Missing subscription is treated as expired (restrict writes).
    /// </summary>
    Task<bool> IsSubscriptionWritableAsync(Guid clinicId, CancellationToken cancellationToken = default);
}

public sealed class ClinicSuspendedException : InvalidOperationException
{
    public ClinicSuspendedException()
        : base("Clinic is suspended; clinic operations are blocked (FR-100).")
    {
    }
}

public sealed class SubscriptionExpiredException : InvalidOperationException
{
    public SubscriptionExpiredException()
        : base(
            "Clinic subscription is expired; new bookings and new sessions are blocked until renewal (FR-102).")
    {
    }
}

public sealed class ClinicAccessGate : IClinicAccessGate
{
    private readonly IAppDbContext _db;

    public ClinicAccessGate(IAppDbContext db) => _db = db;

    public async Task EnsureClinicActiveAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var status = await _db.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => (ClinicStatus?)c.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            throw new InvalidOperationException("Clinic not found.");
        }

        if (status == ClinicStatus.Suspended)
        {
            throw new ClinicSuspendedException();
        }
    }

    public async Task EnsureCanAcceptNewBookingsAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        await EnsureClinicActiveAsync(clinicId, cancellationToken);

        if (!await IsSubscriptionWritableAsync(clinicId, cancellationToken))
        {
            throw new SubscriptionExpiredException();
        }
    }

    public async Task<bool> IsSubscriptionWritableAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.ClinicId == clinicId)
            .OrderByDescending(s => s.RenewalDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            // No subscription row yet — do not treat as expired (FR-102 applies after expiry).
            return true;
        }

        if (subscription.Status is SubscriptionStatus.Expired or SubscriptionStatus.Suspended)
        {
            return false;
        }

        // Active but past renewal → treat as expired (lazy transition).
        return subscription.RenewalDate >= now;
    }
}
