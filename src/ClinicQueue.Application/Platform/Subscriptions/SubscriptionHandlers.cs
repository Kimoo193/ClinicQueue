using System.Text.Json;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Platform.Subscriptions;

public sealed record UpdateSubscriptionCommand(
    Guid ClinicId,
    string PlanName,
    DateTime RenewalDate,
    SubscriptionStatus? Status = null);

public sealed record SubscriptionResultDto(
    Guid SubscriptionId,
    string Status,
    DateTime RenewalDate,
    string PlanName);

public interface IUpdateSubscriptionHandler
{
    Task<SubscriptionResultDto> HandleAsync(
        UpdateSubscriptionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Super-admin subscription plan management (FR-101).</summary>
public sealed class UpdateSubscriptionHandler : IUpdateSubscriptionHandler
{
    private readonly IPrivilegedDbContext _db;

    public UpdateSubscriptionHandler(IPrivilegedDbContext db) => _db = db;

    public async Task<SubscriptionResultDto> HandleAsync(
        UpdateSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.PlanName))
        {
            throw new ArgumentException("PlanName is required.", nameof(command));
        }

        var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == command.ClinicId, cancellationToken);
        if (!clinicExists)
        {
            throw new InvalidOperationException("Clinic not found.");
        }

        var renewalUtc = command.RenewalDate.Kind == DateTimeKind.Utc
            ? command.RenewalDate
            : DateTime.SpecifyKind(command.RenewalDate, DateTimeKind.Utc);

        var subscription = await _db.Subscriptions
            .Where(s => s.ClinicId == command.ClinicId)
            .OrderByDescending(s => s.RenewalDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                ClinicId = command.ClinicId,
                PlanName = command.PlanName.Trim(),
                RenewalDate = renewalUtc,
                Status = command.Status ?? SubscriptionStatus.Active
            };
            _db.Subscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanName = command.PlanName.Trim();
            subscription.RenewalDate = renewalUtc;
            if (command.Status.HasValue)
            {
                subscription.Status = command.Status.Value;
            }
            else if (renewalUtc >= DateTime.UtcNow
                     && subscription.Status == SubscriptionStatus.Expired)
            {
                subscription.Status = SubscriptionStatus.Active;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new SubscriptionResultDto(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.RenewalDate,
            subscription.PlanName);
    }
}

/// <summary>
/// Marks past-due Active subscriptions as Expired and enqueues 7-day renewal notices (FR-102, FR-103).
/// </summary>
public interface ISubscriptionLifecycleService
{
    Task ProcessRenewalsAsync(CancellationToken cancellationToken = default);
}

public sealed class SubscriptionLifecycleService : ISubscriptionLifecycleService
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedDbContext _db;

    public SubscriptionLifecycleService(IPrivilegedDbContext db) => _db = db;

    public async Task ProcessRenewalsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var noticeWindowStart = now.Date;
        var noticeWindowEnd = now.Date.AddDays(1);

        // Expire past-due Active subscriptions (FR-102).
        var expired = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.RenewalDate < now)
            .ToListAsync(cancellationToken);

        foreach (var sub in expired)
        {
            sub.Status = SubscriptionStatus.Expired;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        // 7-day renewal notice: RenewalDate falls on (today + 7 days) in UTC calendar day (FR-103).
        var targetDay = DateOnly.FromDateTime(now.AddDays(7));
        var dayStart = targetDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var dueForNotice = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active
                        && s.RenewalDate >= dayStart
                        && s.RenewalDate < dayEnd)
            .ToListAsync(cancellationToken);

        foreach (var sub in dueForNotice)
        {
            var alreadyNotified = await _db.OutboxNotifications.AnyAsync(
                n => n.ClinicId == sub.ClinicId
                     && n.Type == NotificationType.SubscriptionExpiring
                     && n.CreatedAt >= noticeWindowStart
                     && n.CreatedAt < noticeWindowEnd,
                cancellationToken);

            if (alreadyNotified)
            {
                continue;
            }

            var ownerUserId = await _db.ClinicMemberships.AsNoTracking()
                .Where(m => m.ClinicId == sub.ClinicId
                            && m.Role == ClinicRole.ClinicOwner
                            && m.Status == MembershipStatus.Active)
                .Select(m => (Guid?)m.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (ownerUserId is null)
            {
                continue;
            }

            _db.OutboxNotifications.Add(new OutboxNotification
            {
                ClinicId = sub.ClinicId,
                RecipientUserId = ownerUserId.Value,
                Type = NotificationType.SubscriptionExpiring,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    clinicId = sub.ClinicId,
                    subscriptionId = sub.Id,
                    planName = sub.PlanName,
                    renewalDate = sub.RenewalDate
                }, CamelCaseJson),
                Status = OutboxNotificationStatus.Pending
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
