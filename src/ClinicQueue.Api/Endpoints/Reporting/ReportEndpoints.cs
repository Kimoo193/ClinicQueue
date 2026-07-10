using System.Security.Claims;
using ClinicQueue.Application.Reporting;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Reporting;

public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/clinics/{clinicId:guid}/reports/volume", GetVolumeAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("ClinicVolumeReport");

        group.MapGet("/clinics/{clinicId:guid}/reports/absence-rate", GetAbsenceRateAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("ClinicAbsenceRateReport");

        group.MapGet("/clinics/{clinicId:guid}/reports/peak-hours", GetPeakHoursAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("ClinicPeakHoursReport");

        group.MapGet("/clinics/{clinicId:guid}/reports/revenue", GetRevenueAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("ClinicDepositRevenueReport");

        return group;
    }

    private static async Task<IResult> GetVolumeAsync(
        Guid clinicId,
        string? period,
        DateTime? from,
        DateTime? to,
        HttpContext httpContext,
        IClinicReportsHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, clinicId);
        if (access is not null)
        {
            return access;
        }

        var result = await handler.GetVolumeAsync(
            new ReportPeriodQuery(clinicId, from, to, period ?? "daily"),
            cancellationToken);

        return Results.Ok(new
        {
            series = result.Series.Select(p => new { date = p.Date, count = p.Count })
        });
    }

    private static async Task<IResult> GetAbsenceRateAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        HttpContext httpContext,
        IClinicReportsHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, clinicId);
        if (access is not null)
        {
            return access;
        }

        var result = await handler.GetAbsenceRateAsync(clinicId, from, to, cancellationToken);
        return Results.Ok(new { absenceRate = result.AbsenceRate });
    }

    private static async Task<IResult> GetPeakHoursAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        HttpContext httpContext,
        IClinicReportsHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, clinicId);
        if (access is not null)
        {
            return access;
        }

        var result = await handler.GetPeakHoursAsync(clinicId, from, to, cancellationToken);
        return Results.Ok(new { peakDays = result.PeakDays, peakHours = result.PeakHours });
    }

    private static async Task<IResult> GetRevenueAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        HttpContext httpContext,
        IClinicReportsHandler handler,
        CancellationToken cancellationToken)
    {
        var access = EnsureClinicAccess(httpContext, clinicId);
        if (access is not null)
        {
            return access;
        }

        var result = await handler.GetDepositRevenueAsync(clinicId, from, to, cancellationToken);
        return Results.Ok(new { depositRevenue = result.DepositRevenue });
    }

    private static IResult? EnsureClinicAccess(HttpContext httpContext, Guid clinicId)
    {
        if (httpContext.User.IsInRole(Roles.SuperAdmin))
        {
            return null;
        }

        var claim = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (!Guid.TryParse(claim, out var jwtClinicId) || jwtClinicId != clinicId)
        {
            return Results.Forbid();
        }

        return null;
    }
}
