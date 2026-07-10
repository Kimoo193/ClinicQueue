using System.Security.Claims;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Notifications;

public static class PushSubscriptionEndpoints
{
    public static RouteGroupBuilder MapPushSubscriptionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/push-subscriptions", UpsertAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("UpsertPushSubscription");

        return group;
    }

    private static async Task<IResult> UpsertAsync(
        UpsertPushSubscriptionRequest request,
        IValidator<UpsertPushSubscriptionRequest> validator,
        IUpsertPushSubscriptionHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var jwtClinicId = ResolveClinicId(httpContext);
        if (jwtClinicId is null || jwtClinicId.Value != request.ClinicId)
        {
            return Results.Problem(
                detail: "clinic_id in the request must match the authenticated JWT claim.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var id = await handler.HandleAsync(
                new UpsertPushSubscriptionCommand(
                    request.ClinicId,
                    userId.Value,
                    request.Endpoint,
                    request.P256dh,
                    request.Auth,
                    httpContext.Request.Headers.UserAgent.ToString()),
                cancellationToken);

            return Results.Ok(new { subscriptionId = id });
        }
        catch (PushSubscriptionTakeoverException ex)
        {
            return Results.Conflict(new
            {
                title = "Push subscription endpoint taken",
                detail = ex.Message
            });
        }
    }

    private static Guid? ResolveClinicId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        return Guid.TryParse(raw, out var clinicId) ? clinicId : null;
    }

    private static Guid? ResolveUserId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
