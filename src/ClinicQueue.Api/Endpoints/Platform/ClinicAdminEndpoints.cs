using ClinicQueue.Application.Platform.Clinics;
using ClinicQueue.Application.Platform.Subscriptions;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Platform;

public static class ClinicAdminEndpoints
{
    public static RouteGroupBuilder MapClinicAdminEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        admin.MapGet("/clinics", ListClinicsAsync)
            .RequireAuthorization(Policies.SuperAdmin)
            .WithName("AdminListClinics");

        admin.MapPost("/clinics/{id:guid}/activate", ActivateClinicAsync)
            .RequireAuthorization(Policies.SuperAdmin)
            .WithName("AdminActivateClinic");

        admin.MapPost("/clinics/{id:guid}/suspend", SuspendClinicAsync)
            .RequireAuthorization(Policies.SuperAdmin)
            .WithName("AdminSuspendClinic");

        admin.MapPatch("/clinics/{id:guid}/subscription", UpdateSubscriptionAsync)
            .RequireAuthorization(Policies.SuperAdmin)
            .WithName("AdminUpdateSubscription");

        return group;
    }

    private static async Task<IResult> ListClinicsAsync(
        string? status,
        int? page,
        int? pageSize,
        IListAdminClinicsHandler handler,
        CancellationToken cancellationToken)
    {
        ClinicStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ClinicStatus>(status, ignoreCase: true, out var value))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Status must be Active or Suspended."]
                });
            }

            parsed = value;
        }

        var result = await handler.HandleAsync(
            new ListAdminClinicsQuery(parsed, page ?? 1, pageSize ?? 20),
            cancellationToken);

        return Results.Ok(new
        {
            items = result.Items.Select(i => new
            {
                clinicId = i.ClinicId,
                name = i.Name,
                status = i.Status,
                subscriptionStatus = i.SubscriptionStatus,
                planName = i.PlanName,
                renewalDate = i.RenewalDate
            }),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    private static async Task<IResult> ActivateClinicAsync(
        Guid id,
        IActivateClinicHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return Results.Ok(new { clinicId = result.ClinicId, status = result.Status });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SuspendClinicAsync(
        Guid id,
        ISuspendClinicHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return Results.Ok(new { clinicId = result.ClinicId, status = result.Status });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> UpdateSubscriptionAsync(
        Guid id,
        UpdateSubscriptionRequest request,
        IValidator<UpdateSubscriptionRequest> validator,
        IUpdateSubscriptionHandler handler,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        try
        {
            var result = await handler.HandleAsync(
                new UpdateSubscriptionCommand(id, request.PlanName, request.RenewalDate),
                cancellationToken);
            return Results.Ok(new
            {
                subscriptionId = result.SubscriptionId,
                status = result.Status,
                renewalDate = result.RenewalDate,
                planName = result.PlanName
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PlanName"] = [ex.Message]
            });
        }
    }
}
