using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Notifications;

public sealed record UpsertPushSubscriptionCommand(
    Guid ClinicId,
    Guid UserId,
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent);

public interface IUpsertPushSubscriptionHandler
{
    Task<Guid> HandleAsync(UpsertPushSubscriptionCommand command, CancellationToken cancellationToken = default);
}

public sealed class UpsertPushSubscriptionHandler : IUpsertPushSubscriptionHandler
{
    private readonly IAppDbContext _db;

    public UpsertPushSubscriptionHandler(IAppDbContext db) => _db = db;

    public async Task<Guid> HandleAsync(
        UpsertPushSubscriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Endpoint)
            || string.IsNullOrWhiteSpace(command.P256dh)
            || string.IsNullOrWhiteSpace(command.Auth))
        {
            throw new ArgumentException("Endpoint, p256dh, and auth are required.");
        }

        var existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == command.Endpoint, cancellationToken);

        if (existing is not null)
        {
            // Endpoint is globally unique — reject takeover by a different user.
            if (existing.UserId != command.UserId)
            {
                throw new PushSubscriptionTakeoverException(existing.Endpoint);
            }

            existing.ClinicId = command.ClinicId;
            existing.P256dh = command.P256dh;
            existing.Auth = command.Auth;
            existing.UserAgent = command.UserAgent;
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var row = new PushSubscription
        {
            ClinicId = command.ClinicId,
            UserId = command.UserId,
            Endpoint = command.Endpoint.Trim(),
            P256dh = command.P256dh,
            Auth = command.Auth,
            UserAgent = command.UserAgent
        };
        _db.PushSubscriptions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }
}

public sealed class PushSubscriptionTakeoverException : Exception
{
    public string Endpoint { get; }

    public PushSubscriptionTakeoverException(string endpoint)
        : base("Push subscription endpoint is already registered to another user.")
    {
        Endpoint = endpoint;
    }
}
