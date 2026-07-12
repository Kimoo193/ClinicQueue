using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Queue;

public static class ArrivalEndpoints
{
    public static RouteGroupBuilder MapArrivalEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/appointments/{id:guid}/confirm-arrival", ConfirmArrivalAsync)
            .RequireAuthorization(Policies.Receptionist)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("ConfirmArrival")
            .WithSummary("Confirm patient arrival and assign a real queue number");

        group.MapPost("/appointments/{id:guid}/call-next", CallNextAsync)
            .RequireAuthorization(Policies.Receptionist)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("CallNext")
            .WithSummary("Call the next confirmed patient into the clinic");

        group.MapPost("/appointments/instant-entry", InstantEntryAsync)
            .RequireAuthorization(Policies.Receptionist)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("InstantEntry")
            .WithSummary("Insert an urgent walk-in with immediate real number");

        group.MapGet("/appointments/today", ListTodayAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("ListTodayAppointments")
            .WithSummary("List today's appointments for the active clinic");

        group.MapPost("/patients/{membershipId:guid}/restrict-booking", RestrictBookingAsync)
            .RequireAuthorization(Policies.Receptionist)
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Sensitive)
            .WithName("RestrictPatientBooking")
            .WithSummary("Restrict booking for a patient past the absence threshold");
        return group;
    }

    private static async Task<IResult> ConfirmArrivalAsync(
        Guid id,
        IAssignRealNumberHandler handler,
        IAppDbContext db,
        ICountAheadService countAhead,
        IQueueRealtimePublisher realtime,
        ILoggerFactory loggerFactory,
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

        try
        {
            var result = await handler.HandleAsync(
                new AssignRealNumberCommand(id, clinicId.Value),
                cancellationToken);

            await TryPublishSessionPositionsAsync(
                db,
                countAhead,
                realtime,
                clinicId.Value,
                loggerFactory,
                cancellationToken);

            return Results.Ok(new ConfirmArrivalResponse(
                result.AppointmentId,
                result.RealNumber,
                result.Status.ToString(),
                result.WaitingForUrgentRatio));
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> CallNextAsync(
        Guid id,
        ICallNextHandler handler,
        IAppDbContext db,
        ICountAheadService countAhead,
        IQueueRealtimePublisher realtime,
        ILoggerFactory loggerFactory,
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

        try
        {
            var result = await handler.HandleAsync(
                new CallNextCommand(id, clinicId.Value),
                cancellationToken);

            await TryPublishSessionPositionsAsync(
                db,
                countAhead,
                realtime,
                clinicId.Value,
                loggerFactory,
                cancellationToken);

            return Results.Ok(new CallNextResponse(result.AppointmentId, result.Status.ToString()));
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
    }

    private static async Task<IResult> InstantEntryAsync(
        InstantEntryRequest request,
        IValidator<InstantEntryRequest> validator,
        IInstantEntryHandler handler,
        IAppDbContext db,
        ICountAheadService countAhead,
        IQueueRealtimePublisher realtime,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var clinicId = ResolveClinicId(httpContext);
        if (clinicId is null || clinicId.Value != request.ClinicId)
        {
            return Results.Problem(
                detail: "clinic_id in the request must match the authenticated JWT claim.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var clinic = await db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clinicId.Value, cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound();
        }

        try
        {
            var result = await handler.HandleAsync(
                new InstantEntryCommand(
                    request.ClinicId,
                    userId.Value,
                    request.PatientRef,
                    request.Reason,
                    ClinicQueue.Application.Abstractions.ClinicClock.Today(clinic.Timezone)),
                cancellationToken);

            await TryPublishSessionPositionsAsync(
                db,
                countAhead,
                realtime,
                clinicId.Value,
                loggerFactory,
                cancellationToken);

            return Results.Ok(new InstantEntryResponse(
                result.AppointmentId,
                result.RealNumber,
                result.Status.ToString()));
        }
        catch (DuplicateActiveBookingException)
        {
            return Results.Conflict(new
            {
                title = "Duplicate active booking",
                detail = "An active appointment already exists for this patient on the requested day."
            });
        }
        catch (InvalidOperationException ex)
        {
            return MapDomainError(ex);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Reason"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> RestrictBookingAsync(
        Guid membershipId,
        IAbsencePolicyService absencePolicy,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var clinicId = ResolveClinicId(httpContext);
        var userId = ResolveUserId(httpContext);
        if (clinicId is null || userId is null)
        {
            return Results.Problem(
                detail: "clinic_id claim and an authenticated user are required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await absencePolicy.RestrictBookingAsync(
                clinicId.Value,
                membershipId,
                userId.Value,
                cancellationToken);
            return Results.Ok(result);
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

    private static async Task<IResult> ListTodayAsync(
        IAppDbContext db,
        IQueueSessionService sessions,
        IAbsencePolicyService absencePolicy,
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
            return Results.Ok(Array.Empty<ReceptionAppointmentDto>());
        }

        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == session.Id)
            .OrderBy(a => a.RealNumber ?? int.MaxValue)
            .ThenBy(a => a.TemporaryNumber)
            .Take(100)
            .Select(a => new
            {
                a.Id,
                a.PatientMembershipId,
                a.BookedByUserId,
                Type = a.Type.ToString(),
                Method = a.Method.ToString(),
                Status = a.Status.ToString(),
                a.TemporaryNumber,
                a.RealNumber,
                a.IsInstantEntry,
                a.PossibleUrgentFlag,
                a.CompanionsCount,
                DepositConfirmed = a.DepositConfirmedAt != null
            })
            .ToListAsync(cancellationToken);

        var membershipIds = rows
            .Where(r => r.PatientMembershipId is not null)
            .Select(r => r.PatientMembershipId!.Value)
            .Distinct()
            .ToList();

        var flags = await absencePolicy.EvaluateManyAsync(
            clinicId.Value,
            membershipIds,
            cancellationToken);

        // Resolve a human label (patient email/phone) per membership so the console shows who
        // the patient is rather than a truncated GUID. Scoped to this clinic's memberships by RLS.
        var labelByMembership = membershipIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ClinicMemberships.AsNoTracking()
                .Where(m => membershipIds.Contains(m.Id))
                .Join(
                    db.Users.AsNoTracking(),
                    m => m.UserId,
                    u => u.Id,
                    (m, u) => new { m.Id, u.EmailOrPhone })
                .ToDictionaryAsync(x => x.Id, x => x.EmailOrPhone, cancellationToken);

        var appointments = rows.Select(a =>
        {
            AbsenceFlagResult? flag = null;
            string? patientLabel = null;
            if (a.PatientMembershipId is { } mid)
            {
                flags.TryGetValue(mid, out flag);
                labelByMembership.TryGetValue(mid, out patientLabel);
            }

            return new ReceptionAppointmentDto(
                a.Id,
                a.PatientMembershipId,
                a.BookedByUserId,
                a.Type,
                a.Method,
                a.Status,
                a.TemporaryNumber,
                a.RealNumber,
                a.IsInstantEntry,
                a.PossibleUrgentFlag,
                a.CompanionsCount,
                flag?.IsFlagged ?? false,
                flag?.AbsenceCount ?? 0,
                flag?.MembershipStatus,
                a.DepositConfirmed,
                patientLabel);
        }).ToList();

        return Results.Ok(appointments);
    }

    private static async Task TryPublishSessionPositionsAsync(
        IAppDbContext db,
        ICountAheadService countAhead,
        IQueueRealtimePublisher realtime,
        Guid clinicId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await db.Clinics.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
            if (clinic is null)
            {
                return;
            }

            var day = ClinicQueue.Application.Abstractions.ClinicClock.Today(clinic.Timezone);
            var session = await db.QueueSessions.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ClinicId == clinicId && s.OperatingDate == day,
                    cancellationToken);
            if (session is null)
            {
                return;
            }

            var peers = await db.Appointments.AsNoTracking()
                .Where(a => a.QueueSessionId == session.Id
                            && a.Status != AppointmentStatus.Cancelled
                            && a.Status != AppointmentStatus.Completed
                            && a.Status != AppointmentStatus.Absent
                            && a.Status != AppointmentStatus.CarriedOver)
                .ToListAsync(cancellationToken);

            foreach (var appointment in peers.Where(a =>
                         a.Status is AppointmentStatus.Booked
                             or AppointmentStatus.Present
                             or AppointmentStatus.Inside))
            {
                var position = countAhead.ComputeFromSnapshot(
                    appointment,
                    peers,
                    clinic.HeadToClinicThreshold);
                await realtime.PublishPositionAsync(
                    appointment.BookedByUserId,
                    clinicId,
                    position.AppointmentId,
                    position.CountAhead,
                    position.Instruction,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("ClinicQueue.Api.Endpoints.Queue.ArrivalEndpoints")
                .LogWarning(ex, "Failed to publish position updates for clinic {ClinicId}", clinicId);
        }
    }

    private static IResult MapDomainError(InvalidOperationException ex)
    {
        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (ex.Message.Contains("paused", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
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
