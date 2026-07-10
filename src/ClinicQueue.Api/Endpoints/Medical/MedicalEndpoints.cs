using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Medical;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Medical;

public static class MedicalEndpoints
{
    public static RouteGroupBuilder MapMedicalEndpoints(this RouteGroupBuilder group)
    {
        // Doctor policy includes ClinicOwner (read-only enforced in handlers) and excludes Receptionist.
        group.MapPost("/appointments/{id:guid}/visit", CreateVisitAsync)
            .RequireAuthorization(Policies.Doctor)
            .WithName("CreateVisit");

        group.MapGet("/appointments/{id:guid}/visit", GetVisitAsync)
            .RequireAuthorization(Policies.Doctor)
            .WithName("GetVisit");

        group.MapPatch("/visits/{id:guid}", UpdateVisitAsync)
            .RequireAuthorization(Policies.Doctor)
            .WithName("UpdateVisit");

        group.MapPost("/visits/{id:guid}/prescription", IssuePrescriptionAsync)
            .RequireAuthorization(Policies.Doctor)
            .WithName("IssuePrescription");

        return group;
    }

    private static async Task<IResult> CreateVisitAsync(
        Guid id,
        CreateVisitRequest request,
        IValidator<CreateVisitRequest> validator,
        ICreateVisitHandler handler,
        IAppDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var ctx = await ResolveClinicalContextAsync(httpContext, db, cancellationToken);
        if (ctx is null)
        {
            return Results.Problem(
                detail: "clinic_id claim and an active clinical membership are required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await handler.HandleAsync(
                new CreateVisitCommand(
                    ctx.Value.ClinicId,
                    id,
                    ctx.Value.UserId,
                    ctx.Value.Role,
                    request.Diagnosis,
                    request.Notes),
                cancellationToken);
            return Results.Ok(new { visitId = result.VisitId });
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

    private static async Task<IResult> GetVisitAsync(
        Guid id,
        IGetVisitHandler handler,
        IAppDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ctx = await ResolveClinicalContextAsync(httpContext, db, cancellationToken);
        if (ctx is null)
        {
            return Results.Problem(
                detail: "clinic_id claim and an active clinical membership are required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await handler.HandleAsync(
                new GetVisitByAppointmentCommand(
                    ctx.Value.ClinicId,
                    id,
                    ctx.Value.UserId,
                    ctx.Value.Role),
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

    private static async Task<IResult> UpdateVisitAsync(
        Guid id,
        UpdateVisitRequest request,
        IValidator<UpdateVisitRequest> validator,
        IUpdateVisitHandler handler,
        IAppDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var ctx = await ResolveClinicalContextAsync(httpContext, db, cancellationToken);
        if (ctx is null)
        {
            return Results.Problem(
                detail: "clinic_id claim and an active clinical membership are required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await handler.HandleAsync(
                new UpdateVisitCommand(
                    ctx.Value.ClinicId,
                    id,
                    ctx.Value.UserId,
                    ctx.Value.Role,
                    request.Diagnosis,
                    request.Notes),
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

    private static async Task<IResult> IssuePrescriptionAsync(
        Guid id,
        IssuePrescriptionRequest request,
        IValidator<IssuePrescriptionRequest> validator,
        IIssuePrescriptionHandler handler,
        IAppDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var ctx = await ResolveClinicalContextAsync(httpContext, db, cancellationToken);
        if (ctx is null)
        {
            return Results.Problem(
                detail: "clinic_id claim and an active clinical membership are required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await handler.HandleAsync(
                new IssuePrescriptionCommand(
                    ctx.Value.ClinicId,
                    id,
                    ctx.Value.UserId,
                    ctx.Value.Role,
                    request.Drug,
                    request.Dose,
                    request.Frequency,
                    request.Duration),
                cancellationToken);
            return Results.Ok(new { prescriptionId = result.PrescriptionId });
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

    private static async Task<(Guid ClinicId, Guid UserId, ClinicRole Role)?> ResolveClinicalContextAsync(
        HttpContext httpContext,
        IAppDbContext db,
        CancellationToken cancellationToken)
    {
        var clinicId = ResolveClinicId(httpContext);
        var userId = ResolveUserId(httpContext);
        if (clinicId is null || userId is null)
        {
            return null;
        }

        // Prefer the highest clinical role for this clinic (ClinicOwner > Doctor).
        var memberships = await db.ClinicMemberships.AsNoTracking()
            .Where(m => m.ClinicId == clinicId.Value
                        && m.UserId == userId.Value
                        && m.Status == MembershipStatus.Active
                        && (m.Role == ClinicRole.Doctor || m.Role == ClinicRole.ClinicOwner))
            .Select(m => m.Role)
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            // Clinical access is authorized only by an active Doctor/ClinicOwner membership in
            // this clinic — never by the JWT role claim alone. A revoked or inactive membership
            // must not retain access to medical records until token expiry (FR-085, OWASP A01).
            return null;
        }

        // Prefer Doctor when present so a ClinicOwner who is also the treating Doctor can write
        // (Clarification Q4). ClinicOwner-only remains read-only.
        var role = memberships.Contains(ClinicRole.Doctor)
            ? ClinicRole.Doctor
            : ClinicRole.ClinicOwner;
        return (clinicId.Value, userId.Value, role);
    }

    private static IResult MapDomainError(InvalidOperationException ex)
    {
        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
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
