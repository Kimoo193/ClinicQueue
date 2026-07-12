using System.Security.Claims;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Identity.Auth;
using ClinicQueue.Application.Identity.Clinics;
using ClinicQueue.Domain.Common;
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

        // Public slug → branding for QR / subdomain entry (FR-007). ClinicId here is display-only;
        // authoritative tenant scope still comes from the JWT after login (constitution Principle I).
        group.MapGet("/clinics/by-slug/{slug}", GetBySlugAsync)
            .AllowAnonymous()
            .WithName("GetClinicBySlug")
            .WithSummary("Resolve a clinic's public branding profile by subdomain slug");

        group.MapGet("/clinics/{clinicId:guid}/branding", GetBrandingAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("GetClinicBranding");

        group.MapPatch("/clinics/{clinicId:guid}/branding", UpdateBrandingAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("UpdateClinicBranding");

        group.MapGet("/clinics/{clinicId:guid}/settings", GetSettingsAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("GetClinicSettings");

        group.MapPatch("/clinics/{clinicId:guid}/settings", UpdateSettingsAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("UpdateClinicSettings");

        group.MapGet("/clinics/{clinicId:guid}/qr-code", GetQrCodeAsync)
            .RequireAuthorization(Policies.ClinicOwner)
            .WithName("GetClinicQrCode");

        group.MapGet("/patients/me/clinics", ListMyClinicsAsync)
            .RequireAuthorization(Policies.Patient)
            .WithName("ListMyClinics");

        // Authenticated identity (possibly no clinic claim yet) joins as Patient after bare register / QR.
        group.MapPost("/clinics/{clinicId:guid}/join", JoinAsPatientAsync)
            .RequireAuthorization()
            .WithName("JoinClinicAsPatient")
            .WithSummary("Create an active Patient membership for the current user in this clinic");

        return group;
    }

    private static async Task<IResult> GetBySlugAsync(
        string slug,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            return Results.NotFound();
        }

        var clinic = await db.Clinics.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.SubdomainSlug == normalized && c.Status == ClinicStatus.Active,
                cancellationToken);

        if (clinic is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new PublicClinicProfileResponse(
            clinic.Id,
            clinic.Name,
            clinic.Specialty,
            clinic.SubdomainSlug,
            clinic.LogoUrl,
            clinic.PrimaryColor,
            clinic.SecondaryColor));
    }

    // WHY PrivilegedDbContext (BYPASSRLS): clinic sign-up is anonymous — there is no authenticated
    // user and therefore no clinic_id claim, so app.current_clinic is empty. The owner membership
    // and the trial subscription are the FIRST rows of a brand-new tenant, and the RLS WITH CHECK
    // would reject them under the app role. This is a legitimate bootstrap write, so it runs on the
    // privileged connection. Do NOT move it back to AppDbContext (production RLS blocks it even
    // though superuser tests pass). Both tenant rows are pinned to the new clinic.Id and re-checked
    // by GuardClinicScope before save.
    private static async Task<IResult> CreateClinicAsync(
        CreateClinicRequest request,
        IValidator<CreateClinicRequest> validator,
        PrivilegedDbContext privileged,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var email = request.AdminEmail.Trim().ToLowerInvariant();
        var user = await privileged.Users.FirstOrDefaultAsync(u => u.EmailOrPhone == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                EmailOrPhone = email,
                PasswordHash = passwordHasher.Hash(request.AdminPassword)
            };
            privileged.Users.Add(user);
        }

        var clinic = new Clinic
        {
            Name = request.Name.Trim(),
            Specialty = request.Specialty.Trim(),
            ContactInfo = request.Contact,
            SubdomainSlug = SubdomainSlug.FromClinicName(request.Name),
            QrCodeUrl = null
        };
        clinic.QrCodeUrl = $"https://app.clinicqueue.app/r/{clinic.SubdomainSlug}";

        privileged.Clinics.Add(clinic);
        await privileged.SaveChangesAsync(cancellationToken);

        var ownerMembership = new ClinicMembership
        {
            ClinicId = clinic.Id,
            UserId = user.Id,
            Role = ClinicRole.ClinicOwner,
            Status = MembershipStatus.Active
        };
        GuardClinicScope(ownerMembership.ClinicId, clinic.Id, nameof(ClinicMembership));
        privileged.ClinicMemberships.Add(ownerMembership);

        var subscription = new Subscription
        {
            ClinicId = clinic.Id,
            PlanName = "trial",
            Status = SubscriptionStatus.Active,
            RenewalDate = DateTime.UtcNow.AddDays(30)
        };
        GuardClinicScope(subscription.ClinicId, clinic.Id, nameof(Subscription));
        privileged.Subscriptions.Add(subscription);

        await privileged.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/v1/clinics/{clinic.Id}",
            new CreateClinicResponse(clinic.Id, clinic.SubdomainSlug, clinic.QrCodeUrl));
    }

    private static async Task<IResult> GetBrandingAsync(
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

        return Results.Ok(new
        {
            clinicId,
            branding = new { clinic.LogoUrl, clinic.PrimaryColor, clinic.SecondaryColor }
        });
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

    private static async Task<IResult> GetSettingsAsync(
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

        return Results.Ok(new
        {
            clinicId,
            name = clinic.Name,
            subdomainSlug = clinic.SubdomainSlug,
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

    // WHY PrivilegedDbContext (BYPASSRLS) and not AppDbContext here:
    // This is an onboarding write. The just-authenticated user has NO clinic_id claim yet — they
    // are joining their first/next clinic — so app.current_clinic is empty. Under the RLS-enforced
    // app role the tenant_isolation WITH CHECK on clinic_memberships/audit_logs evaluates to NULL
    // and REJECTS these INSERTs (and the existence read would fail-closed to zero rows, causing a
    // duplicate insert). It is a legitimate cross-tenant write because the user has no tenant
    // context to scope by, so it must run through the privileged connection. Do NOT "clean this up"
    // back to AppDbContext: production RLS will block it even though superuser-run tests pass.
    // Every row is pinned to the path clinicId and re-checked by GuardClinicScope before save.
    private static async Task<IResult> JoinAsPatientAsync(
        Guid clinicId,
        HttpContext httpContext,
        PrivilegedDbContext privileged,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var clinic = await privileged.Clinics.FirstOrDefaultAsync(
            c => c.Id == clinicId && c.Status == ClinicStatus.Active,
            cancellationToken);
        if (clinic is null)
        {
            return Results.NotFound(new { title = "Clinic not found." });
        }

        var existing = await privileged.ClinicMemberships.FirstOrDefaultAsync(
            m => m.ClinicId == clinicId
                 && m.UserId == userId.Value
                 && m.Role == ClinicRole.Patient,
            cancellationToken);

        if (existing is null)
        {
            existing = new ClinicMembership
            {
                ClinicId = clinicId,
                UserId = userId.Value,
                Role = ClinicRole.Patient,
                Status = MembershipStatus.Active
            };
            GuardClinicScope(existing.ClinicId, clinicId, nameof(ClinicMembership));
            privileged.ClinicMemberships.Add(existing);

            var auditRow = new AuditLog
            {
                ClinicId = clinicId,
                ActorUserId = userId.Value,
                Action = "PatientJoinedClinic",
                EntityType = nameof(ClinicMembership),
                EntityId = existing.Id,
                Timestamp = DateTime.UtcNow
            };
            GuardClinicScope(auditRow.ClinicId, clinicId, nameof(AuditLog));
            privileged.AuditLogs.Add(auditRow);

            await privileged.SaveChangesAsync(cancellationToken);
        }
        else if (existing.Status != MembershipStatus.Active)
        {
            existing.Status = MembershipStatus.Active;
            await privileged.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new
        {
            clinicId = clinic.Id,
            clinicName = clinic.Name,
            membershipId = existing.Id,
            role = ClinicRole.Patient.ToString()
        });
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

    // Defence-in-depth for the privileged (BYPASSRLS) onboarding writes above: the database will
    // NOT stop a row whose ClinicId points at the wrong tenant, so we assert scope in code before
    // save. Mirrors the per-row guard the hardened background jobs use (see OutboxDispatchJob).
    private static void GuardClinicScope(Guid rowClinicId, Guid expectedClinicId, string entity)
    {
        if (rowClinicId != expectedClinicId)
        {
            throw new InvalidOperationException(
                $"Onboarding write refused: {entity}.ClinicId {rowClinicId} does not match the " +
                $"target clinic {expectedClinicId}.");
        }
    }

    private static Guid? GetUserId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

