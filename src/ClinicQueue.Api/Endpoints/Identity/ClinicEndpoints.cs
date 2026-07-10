using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Identity.Auth;
using ClinicQueue.Application.Identity.Clinics;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Auditing;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Persistence;
using ClinicQueue.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Identity;

public static class ClinicEndpoints
{
    public static RouteGroupBuilder MapClinicEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/clinics", CreateClinicAsync)
            .AllowAnonymous()
            .WithName("CreateClinic");

        group.MapPatch("/clinics/{clinicId:guid}/branding", UpdateBrandingAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("UpdateClinicBranding");

        group.MapPatch("/clinics/{clinicId:guid}/settings", UpdateSettingsAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("UpdateClinicSettings");

        group.MapGet("/clinics/{clinicId:guid}/qr-code", GetQrCodeAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("GetClinicQrCode");

        group.MapGet("/patients/me/clinics", ListMyClinicsAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("ListMyClinics");

        return group;
    }

    private static async Task<IResult> CreateClinicAsync(
        CreateClinicRequest request,
        IValidator<CreateClinicRequest> validator,
        AppDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var email = request.AdminEmail.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailOrPhone == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                EmailOrPhone = email,
                PasswordHash = passwordHasher.Hash(request.AdminPassword)
            };
            db.Users.Add(user);
        }

        var clinic = new Clinic
        {
            Name = request.Name.Trim(),
            Specialty = request.Specialty.Trim(),
            ContactInfo = request.Contact,
            SubdomainSlug = SubdomainSlug.FromClinicName(request.Name),
            QrCodeUrl = null
        };
        clinic.QrCodeUrl = $"https://{clinic.SubdomainSlug}.clinicqueue.app/join";

        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(cancellationToken);

        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinic.Id,
            UserId = user.Id,
            Role = ClinicRole.ClinicOwner,
            Status = MembershipStatus.Active
        });

        db.Subscriptions.Add(new Subscription
        {
            ClinicId = clinic.Id,
            PlanName = "trial",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30)
        });

        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/v1/clinics/{clinic.Id}",
            new CreateClinicResponse(clinic.Id, clinic.SubdomainSlug, clinic.QrCodeUrl));
    }

    private static async Task<IResult> UpdateBrandingAsync(
        Guid clinicId,
        UpdateBrandingRequest request,
        HttpContext httpContext,
        IValidator<UpdateBrandingRequest> validator,
        AppDbContext db,
        IAuditService audit,
        CancellationToken cancellationToken)
    {
        var tenantCheck = EnsureJwtClinic(httpContext, clinicId);
        if (tenantCheck is not null)
        {
            return tenantCheck;
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound();
        }

        if (request.LogoUrl is not null) clinic.LogoUrl = request.LogoUrl;
        if (request.PrimaryColor is not null) clinic.PrimaryColor = request.PrimaryColor;
        if (request.SecondaryColor is not null) clinic.SecondaryColor = request.SecondaryColor;

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            clinicId,
            GetUserId(httpContext),
            "clinic.branding.updated",
            nameof(Clinic),
            clinicId,
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            clinicId,
            branding = new { clinic.LogoUrl, clinic.PrimaryColor, clinic.SecondaryColor }
        });
    }

    private static async Task<IResult> UpdateSettingsAsync(
        Guid clinicId,
        UpdateSettingsRequest request,
        HttpContext httpContext,
        IValidator<UpdateSettingsRequest> validator,
        AppDbContext db,
        IAuditService audit,
        CancellationToken cancellationToken)
    {
        var tenantCheck = EnsureJwtClinic(httpContext, clinicId);
        if (tenantCheck is not null)
        {
            return tenantCheck;
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound();
        }

        if (request.UrgentRatio.HasValue) clinic.UrgentRatio = request.UrgentRatio.Value;
        if (request.HeadToClinicThreshold.HasValue) clinic.HeadToClinicThreshold = request.HeadToClinicThreshold.Value;
        if (request.AutoAbsenceTimeoutMinutes.HasValue) clinic.AutoAbsenceTimeoutMinutes = request.AutoAbsenceTimeoutMinutes.Value;
        if (request.RepeatedAbsenceThreshold.HasValue) clinic.RepeatedAbsenceThreshold = request.RepeatedAbsenceThreshold.Value;
        if (request.WorkingHoursJson is not null) clinic.WorkingHoursJson = request.WorkingHoursJson;
        if (request.Timezone is not null) clinic.Timezone = request.Timezone;

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            clinicId,
            GetUserId(httpContext),
            "clinic.settings.updated",
            nameof(Clinic),
            clinicId,
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            clinicId,
            settings = new
            {
                clinic.UrgentRatio,
                clinic.HeadToClinicThreshold,
                clinic.AutoAbsenceTimeoutMinutes,
                clinic.RepeatedAbsenceThreshold,
                clinic.WorkingHoursJson,
                clinic.Timezone
            }
        });
    }

    private static async Task<IResult> GetQrCodeAsync(
        Guid clinicId,
        HttpContext httpContext,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantCheck = EnsureJwtClinic(httpContext, clinicId);
        if (tenantCheck is not null)
        {
            return tenantCheck;
        }

        var clinic = await db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new { clinicId, clinic.SubdomainSlug, clinic.QrCodeUrl });
    }

    private static async Task<IResult> ListMyClinicsAsync(
        HttpContext httpContext,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var clinics = await db.ClinicMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Role == ClinicRole.Patient)
            .Select(m => new { clinicId = m.ClinicId, membershipId = m.Id, status = m.Status.ToString() })
            .ToListAsync(cancellationToken);

        return Results.Ok(clinics);
    }

    private static IResult? EnsureJwtClinic(HttpContext httpContext, Guid clinicId)
    {
        var claim = httpContext.User.FindFirstValue(JwtTenantProvider.ClinicIdClaimType);
        if (!Guid.TryParse(claim, out var jwtClinicId) || jwtClinicId != clinicId)
        {
            // Reject path/body clinic_id that does not match JWT (constitution Principle I)
            return Results.Forbid();
        }

        return null;
    }

    private static Guid? GetUserId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

