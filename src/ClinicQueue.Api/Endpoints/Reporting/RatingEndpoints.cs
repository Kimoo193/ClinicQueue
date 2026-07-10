using System.Security.Claims;
using ClinicQueue.Application.Reporting;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Reporting;

public static class RatingEndpoints
{
    public static RouteGroupBuilder MapRatingEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/appointments/{id:guid}/rating", SubmitRatingAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("SubmitAppointmentRating");

        group.MapGet("/clinics/{clinicId:guid}/rating", GetClinicRatingAsync)
            .AllowAnonymous()
            .WithName("GetClinicAggregateRating");

        return group;
    }

    private static async Task<IResult> SubmitRatingAsync(
        Guid id,
        SubmitRatingRequest request,
        HttpContext httpContext,
        IValidator<SubmitRatingRequest> validator,
        ISubmitRatingHandler handler,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var userIdRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return Results.Unauthorized();
        }

        var clinicClaim = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (!Guid.TryParse(clinicClaim, out var clinicId))
        {
            return Results.Problem(
                detail: "clinic_id claim is required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await handler.HandleAsync(
                new SubmitRatingCommand(clinicId, id, userId, request.Score, request.Comment),
                cancellationToken);
            return Results.Ok(new { ratingId = result.RatingId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetClinicRatingAsync(
        Guid clinicId,
        IGetClinicRatingHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(clinicId, cancellationToken);
        return Results.Ok(new { aggregateRating = result.AggregateRating });
    }
}
