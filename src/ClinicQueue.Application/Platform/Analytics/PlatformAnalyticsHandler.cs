using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Platform.Analytics;

public sealed record PlatformAnalyticsQuery(DateTime? From = null, DateTime? To = null);

public sealed record PlatformUsageDto(
    int ClinicCount,
    int ActiveClinicCount,
    int AppointmentCount,
    int VisitCount,
    int PatientProfileCount,
    int QueueSessionCount);

public sealed record PlatformRevenueDto(
    int ConfirmedDepositCount,
    int ActiveSubscriptionCount,
    int ExpiredSubscriptionCount);

public sealed record PlatformAnalyticsDto(
    PlatformUsageDto Usage,
    PlatformRevenueDto Revenue,
    DateTime? From,
    DateTime? To);

public interface IPlatformAnalyticsHandler
{
    Task<PlatformAnalyticsDto> HandleAsync(
        PlatformAnalyticsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Platform-wide usage and revenue analytics for SuperAdmin (FR-104).</summary>
public sealed class PlatformAnalyticsHandler : IPlatformAnalyticsHandler
{
    private readonly IAppDbContext _db;

    public PlatformAnalyticsHandler(IAppDbContext db) => _db = db;

    public async Task<PlatformAnalyticsDto> HandleAsync(
        PlatformAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var from = NormalizeUtc(query.From);
        var to = NormalizeUtc(query.To);

        var clinics = _db.Clinics.AsNoTracking();
        var clinicCount = await clinics.CountAsync(cancellationToken);
        var activeClinicCount = await clinics.CountAsync(c => c.Status == ClinicStatus.Active, cancellationToken);

        var appointments = _db.Appointments.AsNoTracking().AsQueryable();
        var visits = _db.Visits.AsNoTracking().AsQueryable();
        var profiles = _db.PatientProfiles.AsNoTracking().AsQueryable();
        var sessions = _db.QueueSessions.AsNoTracking().AsQueryable();
        var deposits = _db.Appointments.AsNoTracking()
            .Where(a => a.Method == AppointmentMethod.PhoneDeposit && a.DepositConfirmedAt != null);
        var subscriptions = _db.Subscriptions.AsNoTracking().AsQueryable();

        if (from.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt >= from.Value);
            visits = visits.Where(v => v.CreatedAt >= from.Value);
            profiles = profiles.Where(p => p.CreatedAt >= from.Value);
            sessions = sessions.Where(s => s.CreatedAt >= from.Value);
            deposits = deposits.Where(a => a.DepositConfirmedAt >= from.Value);
            subscriptions = subscriptions.Where(s => s.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt <= to.Value);
            visits = visits.Where(v => v.CreatedAt <= to.Value);
            profiles = profiles.Where(p => p.CreatedAt <= to.Value);
            sessions = sessions.Where(s => s.CreatedAt <= to.Value);
            deposits = deposits.Where(a => a.DepositConfirmedAt <= to.Value);
            subscriptions = subscriptions.Where(s => s.CreatedAt <= to.Value);
        }

        var usage = new PlatformUsageDto(
            clinicCount,
            activeClinicCount,
            await appointments.CountAsync(cancellationToken),
            await visits.CountAsync(cancellationToken),
            await profiles.CountAsync(cancellationToken),
            await sessions.CountAsync(cancellationToken));

        // Deposit amounts are not stored in MVP — revenue is confirmed-deposit volume + subscription status mix.
        var revenue = new PlatformRevenueDto(
            await deposits.CountAsync(cancellationToken),
            await subscriptions.CountAsync(s => s.Status == SubscriptionStatus.Active, cancellationToken),
            await subscriptions.CountAsync(s => s.Status == SubscriptionStatus.Expired, cancellationToken));

        return new PlatformAnalyticsDto(usage, revenue, from, to);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
