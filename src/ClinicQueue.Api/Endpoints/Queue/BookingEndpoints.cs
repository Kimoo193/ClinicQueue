using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Queue;

public static class BookingEndpoints
{
    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/appointments", CreateAppointmentAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("CreateAppointment");

        group.MapPost("/appointments/on-behalf", OnBehalfBookingAsync)
            .RequireAuthorization(Policies.Receptionist)
            .WithName("OnBehalfBooking");

        group.MapPost("/appointments/{id:guid}/cancel", CancelAppointmentAsync)
            .RequireAuthorization(Policies.PatientOrReceptionist)
            .WithName("CancelAppointment");

        group.MapPatch("/appointments/{id:guid}/type", SetAppointmentTypeAsync)
            .RequireAuthorization(Policies.ClinicStaff)
            .WithName("SetAppointmentType");

        group.MapGet("/patients/me/appointments", ListMyAppointmentsAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("ListMyAppointments");

        group.MapPost("/patients/me/dependents", RegisterDependentAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("RegisterDependent");

        group.MapGet("/patients/me/dependents", ListDependentsAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("ListDependents");

        return group;
    }

    private static async Task<IResult> CreateAppointmentAsync(
        CreateAppointmentRequest request,
        IValidator<CreateAppointmentRequest> validator,
        ICreateAppointmentHandler handler,
        IDependentBookingHandler dependentHandler,
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

        var jwtClinicId = ResolveClinicId(httpContext);
        if (jwtClinicId is null || jwtClinicId.Value != request.ClinicId)
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

        if (!TryParseMethod(request.Method, out var method, out var methodError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Method"] = [methodError]
            });
        }

        try
        {
            CreateAppointmentResult created;
            if (request.DependentId is { } dependentId)
            {
                created = await dependentHandler.HandleAsync(
                    new DependentBookingCommand(
                        request.ClinicId,
                        userId.Value,
                        dependentId,
                        request.Day,
                        method,
                        request.CompanionsCount,
                        request.PossibleUrgentFlag),
                    cancellationToken);
            }
            else
            {
                created = await handler.HandleAsync(
                    new CreateAppointmentCommand(
                        request.ClinicId,
                        userId.Value,
                        request.Day,
                        method,
                        request.CompanionsCount,
                        request.PossibleUrgentFlag,
                        DependentId: null),
                    cancellationToken);
            }

            try
            {
                await TryPublishInitialPositionAsync(
                    db, countAhead, realtime, created, cancellationToken);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("ClinicQueue.Api.Endpoints.Queue.BookingEndpoints")
                    .LogWarning(
                        ex,
                        "Failed to publish initial positionUpdated for appointment {AppointmentId} clinic {ClinicId}",
                        created.AppointmentId,
                        request.ClinicId);
            }

            return Results.Created(
                $"/api/v1/appointments/{created.AppointmentId}",
                new CreateAppointmentResponse(created.AppointmentId, created.TemporaryNumber));
        }
        catch (DuplicateActiveBookingException)
        {
            return Results.Conflict(new
            {
                title = "Duplicate active booking",
                detail = "An active appointment already exists for this patient on the requested day."
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task TryPublishInitialPositionAsync(
        IAppDbContext db,
        ICountAheadService countAhead,
        IQueueRealtimePublisher realtime,
        CreateAppointmentResult created,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == created.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return;
        }

        var clinic = await db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == appointment.ClinicId, cancellationToken);
        if (clinic is null)
        {
            return;
        }

        var peers = await db.Appointments.AsNoTracking()
            .Where(a => a.QueueSessionId == created.QueueSessionId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Absent
                        && a.Status != AppointmentStatus.CarriedOver)
            .ToListAsync(cancellationToken);

        var position = countAhead.ComputeFromSnapshot(
            appointment,
            peers,
            clinic.HeadToClinicThreshold);

        await realtime.PublishPositionAsync(
            appointment.BookedByUserId,
            clinic.Id,
            position.AppointmentId,
            position.CountAhead,
            position.Instruction,
            cancellationToken);
    }

    private static async Task<IResult> OnBehalfBookingAsync(
        OnBehalfBookingRequest request,
        IValidator<OnBehalfBookingRequest> validator,
        IOnBehalfBookingHandler handler,
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

        var jwtClinicId = ResolveClinicId(httpContext);
        if (jwtClinicId is null || jwtClinicId.Value != request.ClinicId)
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

        if (!TryParseMethod(request.Method, out var method, out var methodError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Method"] = [methodError]
            });
        }

        if (!TryParseType(request.Type, out var type, out var typeError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Type"] = [typeError]
            });
        }

        try
        {
            var created = await handler.HandleAsync(
                new OnBehalfBookingCommand(
                    request.ClinicId,
                    userId.Value,
                    request.PatientRef,
                    request.Day,
                    type,
                    method,
                    request.CompanionsCount),
                cancellationToken);

            try
            {
                await TryPublishInitialPositionAsync(
                    db, countAhead, realtime, created, cancellationToken);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("ClinicQueue.Api.Endpoints.Queue.BookingEndpoints")
                    .LogWarning(ex, "Failed to publish position for on-behalf booking {Id}", created.AppointmentId);
            }

            return Results.Created(
                $"/api/v1/appointments/{created.AppointmentId}",
                new CreateAppointmentResponse(created.AppointmentId, created.TemporaryNumber));
        }
        catch (DuplicateActiveBookingException)
        {
            return Results.Conflict(new
            {
                title = "Duplicate active booking",
                detail = "An active appointment already exists for this patient on the requested day."
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CancelAppointmentAsync(
        Guid id,
        ICancelAppointmentHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var isStaff = httpContext.User.IsInRole(Roles.Receptionist)
                      || httpContext.User.IsInRole(Roles.Doctor)
                      || httpContext.User.IsInRole(Roles.ClinicOwner);
        var clinicId = ResolveClinicId(httpContext);

        try
        {
            var result = await handler.HandleAsync(
                new CancelAppointmentCommand(id, userId.Value, clinicId, isStaff),
                cancellationToken);
            return Results.Ok(new CancelAppointmentResponse(result.AppointmentId, result.Status));
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

    private static async Task<IResult> SetAppointmentTypeAsync(
        Guid id,
        SetAppointmentTypeRequest request,
        IValidator<SetAppointmentTypeRequest> validator,
        ISetAppointmentTypeHandler handler,
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

        if (!TryParseType(request.Type, out var type, out var typeError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Type"] = [typeError]
            });
        }

        try
        {
            var result = await handler.HandleAsync(
                new SetAppointmentTypeCommand(id, clinicId.Value, type),
                cancellationToken);
            return Results.Ok(new SetAppointmentTypeResponse(result.AppointmentId, result.Type.ToString()));
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

    private static async Task<IResult> ListMyAppointmentsAsync(
        string? status,
        bool? unified,
        IListPatientAppointmentsHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Default: unified cross-clinic history (FR-006). Pass unified=false to scope to JWT clinic.
        var useUnified = unified ?? true;
        Guid? clinicId = null;
        if (!useUnified)
        {
            clinicId = ResolveClinicId(httpContext);
            if (clinicId is null)
            {
                return Results.Problem(
                    detail: "clinic_id claim is required when unified=false.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var results = await handler.HandleAsync(
            new ListPatientAppointmentsQuery(userId.Value, status, useUnified, clinicId),
            cancellationToken);

        return Results.Ok(results);
    }

    private static async Task<IResult> RegisterDependentAsync(
        RegisterDependentRequest request,
        IValidator<RegisterDependentRequest> validator,
        IRegisterDependentHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var jwtClinicId = ResolveClinicId(httpContext);
        if (jwtClinicId is null || jwtClinicId.Value != request.ClinicId)
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

        try
        {
            var result = await handler.HandleAsync(
                new RegisterDependentCommand(
                    request.ClinicId,
                    userId.Value,
                    request.Name,
                    request.DateOfBirth,
                    request.RelationshipToGuardian),
                cancellationToken);

            return Results.Created(
                $"/api/v1/patients/me/dependents/{result.DependentId}",
                new
                {
                    dependentId = result.DependentId,
                    name = result.Name,
                    dateOfBirth = result.DateOfBirth,
                    relationshipToGuardian = result.RelationshipToGuardian
                });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ListDependentsAsync(
        Guid? clinicId,
        IListDependentsHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var results = await handler.HandleAsync(userId.Value, clinicId, cancellationToken);
        return Results.Ok(results);
    }

    private static bool TryParseMethod(string method, out AppointmentMethod parsed, out string error)
    {
        switch (method.Trim())
        {
            case "WalkIn" or "Walkin":
                parsed = AppointmentMethod.WalkIn;
                error = string.Empty;
                return true;
            case "PhoneDeposit" or "Phone-Deposit":
                parsed = AppointmentMethod.PhoneDeposit;
                error = string.Empty;
                return true;
            default:
                parsed = default;
                error = "Method must be WalkIn or PhoneDeposit.";
                return false;
        }
    }

    private static bool TryParseType(string type, out AppointmentType parsed, out string error)
    {
        switch (type.Trim())
        {
            case "Normal":
                parsed = AppointmentType.Normal;
                error = string.Empty;
                return true;
            case "Urgent":
                parsed = AppointmentType.Urgent;
                error = string.Empty;
                return true;
            default:
                parsed = default;
                error = "Type must be Normal or Urgent.";
                return false;
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
