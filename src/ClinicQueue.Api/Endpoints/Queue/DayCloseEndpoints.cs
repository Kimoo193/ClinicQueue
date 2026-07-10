using System.Security.Claims;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.DayClose;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Queue;

public static class DayCloseEndpoints
{
    public static RouteGroupBuilder MapDayCloseEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/queue-sessions/{id:guid}/close", CloseDayAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("CloseQueueSession");

        group.MapPost("/appointments/{id:guid}/carry-over", CarryOverAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("CarryOverAppointment");

        group.MapPost("/appointments/{id:guid}/carry-over/cancel", CancelUnservedAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("CancelUnservedAppointment");

        group.MapPost("/appointments/{id:guid}/confirm-deposit", ConfirmDepositAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("ConfirmDeposit");

        return group;
    }

    private static async Task<IResult> CloseDayAsync(
        Guid id,
        ICloseDaySessionHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = ResolveActor(httpContext);
        if (context is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.HandleAsync(
                new CloseDaySessionCommand(context.Value.ClinicId, id, context.Value.UserId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> CarryOverAsync(
        Guid id,
        ICarryOverHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = ResolveActor(httpContext);
        if (context is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.CarryOverAsync(
                new CarryOverAppointmentCommand(context.Value.ClinicId, id, context.Value.UserId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> CancelUnservedAsync(
        Guid id,
        ICarryOverHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = ResolveActor(httpContext);
        if (context is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.CancelAsync(
                new CancelUnservedCommand(context.Value.ClinicId, id, context.Value.UserId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> ConfirmDepositAsync(
        Guid id,
        IConfirmDepositHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = ResolveActor(httpContext);
        if (context is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.HandleAsync(
                new ConfirmDepositCommand(context.Value.ClinicId, id, context.Value.UserId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static IResult MissingClaims() =>
        Results.Problem(
            detail: "clinic_id claim and an authenticated user are required.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult MapDomainError(InvalidOperationException ex)
    {
        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Only", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }

    private static (Guid ClinicId, Guid UserId)? ResolveActor(HttpContext httpContext)
    {
        var clinicRaw = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        var userRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? httpContext.User.FindFirstValue("sub");

        if (Guid.TryParse(clinicRaw, out var clinicId) && Guid.TryParse(userRaw, out var userId))
        {
            return (clinicId, userId);
        }

        return null;
    }
}
