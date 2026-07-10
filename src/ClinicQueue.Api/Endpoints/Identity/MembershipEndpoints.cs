using System.Security.Claims;
using ClinicQueue.Application.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Identity;

public static class MembershipEndpoints
{
    public static RouteGroupBuilder MapMembershipEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/clinics/{clinicId:guid}/memberships/receptionists", InviteReceptionistAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("InviteReceptionist");

        group.MapGet("/clinics/{clinicId:guid}/memberships/receptionists", ListReceptionistsAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("ListReceptionists");

        group.MapDelete("/clinics/{clinicId:guid}/memberships/receptionists/{membershipId:guid}", RevokeReceptionistAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("RevokeReceptionist");

        return group;
    }

    private static async Task<IResult> InviteReceptionistAsync(
        Guid clinicId,
        InviteReceptionistRequest request,
        HttpContext httpContext,
        IValidator<InviteReceptionistRequest> validator,
        IInviteReceptionistHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureOwnerAccess(httpContext, clinicId);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        try
        {
            var result = await handler.HandleAsync(
                new InviteReceptionistCommand(clinicId, access.UserId!.Value, request.Email, request.Name),
                cancellationToken);

            return Results.Created(
                $"/api/v1/clinics/{clinicId}/memberships/{result.MembershipId}",
                new { membershipId = result.MembershipId, userId = result.UserId, name = result.Name });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ListReceptionistsAsync(
        Guid clinicId,
        HttpContext httpContext,
        IListReceptionistsHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureOwnerAccess(httpContext, clinicId);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var rows = await handler.HandleAsync(clinicId, cancellationToken);
        return Results.Ok(rows.Select(r => new
        {
            membershipId = r.MembershipId,
            userId = r.UserId,
            emailOrPhone = r.EmailOrPhone,
            status = r.Status,
            createdAt = r.CreatedAt
        }));
    }

    private static async Task<IResult> RevokeReceptionistAsync(
        Guid clinicId,
        Guid membershipId,
        HttpContext httpContext,
        IRevokeReceptionistHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureOwnerAccess(httpContext, clinicId);
        if (access.Error is not null)
        {
            return access.Error;
        }

        try
        {
            await handler.HandleAsync(
                new RevokeReceptionistCommand(clinicId, access.UserId!.Value, membershipId),
                cancellationToken);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
    }

    private static (Guid? UserId, IResult? Error) EnsureOwnerAccess(HttpContext httpContext, Guid clinicId)
    {
        var userIdRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return (null, Results.Unauthorized());
        }

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
