using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Hubs;

/// <summary>Live queue position hub — group clinic:{clinicId} for membership; position events are per-user (FR-027).</summary>
[Authorize]
public sealed class QueueHub : Hub
{
    private readonly IAppDbContext _db;

    public QueueHub(IAppDbContext db) => _db = db;

    public static string ClinicGroup(Guid clinicId) => $"clinic:{clinicId}";

    public async Task JoinClinic(Guid clinicId)
    {
        await EnsureClinicMembershipAsync(clinicId);
        await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroup(clinicId));
    }

    public async Task LeaveClinic(Guid clinicId)
    {
        await EnsureClinicMembershipAsync(clinicId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ClinicGroup(clinicId));
    }

    private async Task EnsureClinicMembershipAsync(Guid clinicId)
    {
        var userId = ResolveUserId();
        if (userId is null)
        {
            throw new HubException("Authentication required.");
        }

        if (Context.User?.IsInRole(Roles.SuperAdmin) == true)
        {
            return;
        }

        var jwtClinicRaw = Context.User?.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (Guid.TryParse(jwtClinicRaw, out var jwtClinicId) && jwtClinicId != clinicId)
        {
            throw new HubException("clinic_id claim does not match the requested clinic.");
        }

        var isMember = await _db.ClinicMemberships.AsNoTracking().AnyAsync(
            m => m.ClinicId == clinicId
                 && m.UserId == userId.Value
                 && m.Status == MembershipStatus.Active);

        if (!isMember)
        {
            throw new HubException("Not a member of this clinic.");
        }
    }

    private Guid? ResolveUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}

/// <summary>SignalR-backed realtime publisher registered from the API host.</summary>
public sealed class SignalRQueueRealtimePublisher : IQueueRealtimePublisher
{
    private readonly IHubContext<QueueHub> _hub;
    private readonly ILogger<SignalRQueueRealtimePublisher> _logger;

    public SignalRQueueRealtimePublisher(
        IHubContext<QueueHub> hub,
        ILogger<SignalRQueueRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task PublishNotificationAsync(
        Guid recipientUserId,
        object payload,
        CancellationToken cancellationToken = default) =>
        _hub.Clients
            .User(recipientUserId.ToString())
            .SendAsync("notification", payload, cancellationToken);

    public async Task PublishPositionAsync(
        Guid recipientUserId,
        Guid clinicId,
        Guid appointmentId,
        int countAhead,
        QueueInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Per-user only — clinic-wide fan-out would leak other patients' queue state.
            // User id maps from JWT NameIdentifier (JwtBearer NameClaimType).
            await _hub.Clients
                .User(recipientUserId.ToString())
                .SendAsync(
                    "positionUpdated",
                    new
                    {
                        appointmentId,
                        countAhead,
                        instruction = instruction == QueueInstruction.WaitHome ? "waitHome" : "headToClinic"
                    },
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish positionUpdated for appointment {AppointmentId} clinic {ClinicId} user {UserId}",
                appointmentId,
                clinicId,
                recipientUserId);
            throw;
        }
    }
}
