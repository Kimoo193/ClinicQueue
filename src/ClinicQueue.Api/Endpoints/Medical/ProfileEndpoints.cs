using System.Security.Claims;
using ClinicQueue.Application.Medical;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Medical;

public static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder group)
    {
        // Patient (self), Doctor, and ClinicOwner may read; only the patient may update (FR-080).
        group.MapGet("/patients/{membershipId:guid}/profile", GetProfileAsync)
            .RequireAuthorization(Policies.PatientOrDoctor)
            .WithName("GetPatientProfile");

        group.MapPatch("/patients/{membershipId:guid}/profile", UpdateProfileAsync)
            .RequireAuthorization(Policies.PatientOrDoctor)
            .WithName("UpdatePatientProfile");

        return group;
    }

    private static async Task<IResult> GetProfileAsync(
        Guid membershipId,
        IGetPatientProfileHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ctx = ResolveActor(httpContext);
        if (ctx is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.HandleAsync(
                new GetPatientProfileQuery(ctx.Value.ClinicId, membershipId, ctx.Value.UserId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (MedicalAccessException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> UpdateProfileAsync(
        Guid membershipId,
        UpdatePatientProfileRequest request,
        IValidator<UpdatePatientProfileRequest> validator,
        IUpdatePatientProfileHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var ctx = ResolveActor(httpContext);
        if (ctx is null)
        {
            return MissingClaims();
        }

        try
        {
            var result = await handler.HandleAsync(
                new UpdatePatientProfileCommand(
                    ctx.Value.ClinicId,
                    membershipId,
                    ctx.Value.UserId,
                    request.BloodType,
                    request.ChronicConditions,
                    request.Allergies,
                    request.CurrentMedications),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (MedicalAccessException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
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
