using System.Security.Claims;
using ClinicQueue.Application.Platform.Analytics;
using ClinicQueue.Application.Platform.Export;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Platform;

public static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapAnalyticsEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        admin.MapGet("/analytics", GetPlatformAnalyticsAsync)
            .RequireAuthorization(Policies.SuperAdmin)
            .WithName("AdminPlatformAnalytics");

        group.MapPost("/clinics/{id:guid}/export", StartClinicExportAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("StartClinicExport");

        group.MapGet("/clinics/{id:guid}/export/{jobId:guid}", GetClinicExportAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("GetClinicExport");

        return group;
    }

    private static async Task<IResult> GetPlatformAnalyticsAsync(
        DateTime? from,
        DateTime? to,
        IPlatformAnalyticsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PlatformAnalyticsQuery(from, to), cancellationToken);
        return Results.Ok(new
        {
            usage = new
            {
                clinicCount = result.Usage.ClinicCount,
                activeClinicCount = result.Usage.ActiveClinicCount,
                appointmentCount = result.Usage.AppointmentCount,
                visitCount = result.Usage.VisitCount,
                patientProfileCount = result.Usage.PatientProfileCount,
                queueSessionCount = result.Usage.QueueSessionCount
            },
            revenue = new
            {
                confirmedDepositCount = result.Revenue.ConfirmedDepositCount,
                activeSubscriptionCount = result.Revenue.ActiveSubscriptionCount,
                expiredSubscriptionCount = result.Revenue.ExpiredSubscriptionCount
            },
            from = result.From,
            to = result.To
        });
    }

    private static async Task<IResult> StartClinicExportAsync(
        Guid id,
        HttpContext httpContext,
        IClinicExportHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, id);
        if (access.Error is not null)
        {
            return access.Error;
        }

        try
        {
            var result = await handler.StartAsync(
                new StartClinicExportCommand(id, access.UserId!.Value),
                cancellationToken);
            return Results.Accepted(
                $"/api/v1/clinics/{id}/export/{result.ExportJobId}",
                new { exportJobId = result.ExportJobId, status = result.Status });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetClinicExportAsync(
        Guid id,
        Guid jobId,
        HttpContext httpContext,
        IClinicExportHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, id);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var payload = await handler.GetPayloadAsync(id, jobId, cancellationToken);
        if (payload is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(payload);
    }

    private static (Guid? UserId, IResult? Error) EnsureClinicAccess(HttpContext httpContext, Guid clinicId)
    {
        var userIdRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return (null, Results.Unauthorized());
        }

        // SuperAdmin may export any clinic from the platform console; ClinicOwner must match JWT clinic_id.
        if (httpContext.User.IsInRole(Roles.SuperAdmin))
        {
            return (userId, null);
        }

        var claim = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (!Guid.TryParse(claim, out var jwtClinicId) || jwtClinicId != clinicId)
        {
            return (null, Results.Forbid());
        }

        return (userId, null);
    }
}
