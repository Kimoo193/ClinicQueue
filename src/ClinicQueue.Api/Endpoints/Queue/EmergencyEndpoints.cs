using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Emergency;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Queue;

public static class EmergencyEndpoints
{
    public static RouteGroupBuilder MapEmergencyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/queue-sessions/today", GetTodaySessionAsync)
            .RequireAuthorization(Policies.ClinicStaff)
            .WithName("GetTodayQueueSession");

        group.MapPost("/queue-sessions/{id:guid}/pause", PauseAsync)
            .RequireAuthorization(Policies.ClinicStaff)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("PauseQueueSession")
            .WithSummary("Pause the live queue without losing positions");

        group.MapPost("/queue-sessions/{id:guid}/resume", ResumeAsync)
            .RequireAuthorization(Policies.Doctor)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("ResumeQueueSession")
            .WithSummary("Resume a paused queue from the stop point");
        return group;
    }

    private static async Task<IResult> GetTodaySessionAsync(
        IAppDbContext db,
        IQueueSessionService sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var clinicId = ResolveClinicId(httpContext);
        if (clinicId is null)
        {
            return Results.Problem(
                detail: "clinic_id claim is required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var clinic = await db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clinicId.Value, cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound();
        }

        var day = ClinicQueue.Application.Abstractions.ClinicClock.Today(clinic.Timezone);
        var session = await sessions.GetForDateAsync(clinicId.Value, day, cancellationToken);
        if (session is null)
        {
            return Results.Ok((object?)null);
        }

        return Results.Ok(new
        {
            queueSessionId = session.Id,
            status = session.Status.ToString(),
            pauseReason = session.PauseReason,
            pausedAt = session.PausedAt
        });
    }

    private static async Task<IResult> PauseAsync(
        Guid id,
        PauseQueueSessionRequest request,
        IValidator<PauseQueueSessionRequest> validator,
        IPauseQueueSessionHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var clinicId = ResolveClinicId(httpContext);
        if (clinicId is null)
        {
            return Results.Problem(
                detail: "clinic_id claim is required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await handler.HandleAsync(
                new PauseQueueSessionCommand(clinicId.Value, id, userId.Value, request.Reason),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Reason"] = [ex.Message]
            });
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> ResumeAsync(
        Guid id,
        IResumeQueueSessionHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var clinicId = ResolveClinicId(httpContext);
        if (clinicId is null)
        {
            return Results.Problem(
                detail: "clinic_id claim is required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await handler.HandleAsync(
                new ResumeQueueSessionCommand(clinicId.Value, id, userId.Value),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static IResult MapDomainError(InvalidOperationException ex)
    {
        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Only a paused", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
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
