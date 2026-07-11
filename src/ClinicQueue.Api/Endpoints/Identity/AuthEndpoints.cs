using System.Security.Claims;
using ClinicQueue.Application.Identity;
using ClinicQueue.Application.Identity.Auth;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Endpoints.Identity;

public static class AuthEndpoints
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Login)
            .WithName("Login")
            .WithSummary("Authenticate and issue access/refresh cookies");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Login)
            .WithName("Register")
            .WithSummary("Register a new global patient identity");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(Middleware.RateLimitingPolicies.Login)
            .WithName("Refresh")
            .WithSummary("Rotate refresh token and issue a new access cookie");

        auth.MapPost("/switch-clinic", SwitchClinicAsync)
            // Authenticated identity only — NOT a clinic policy: this is how a user obtains a
            // tenant-scoped token for another clinic they are a member of.
            .RequireAuthorization()
            .WithName("SwitchClinic")
            .WithSummary("Re-issue a tenant-scoped token for another clinic the user is a member of");

        auth.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revoke refresh token and clear auth cookies");
        return group;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AppDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var normalized = request.EmailOrPhone.Trim().ToLowerInvariant();
        var exists = await db.Users.AnyAsync(u => u.EmailOrPhone == normalized, cancellationToken);
        if (exists)
        {
            return Results.Conflict(new { title = "Account already exists." });
        }

        var user = new User
        {
            EmailOrPhone = normalized,
            PasswordHash = passwordHasher.Hash(request.Password)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created("/api/v1/auth/register", new AuthUserResponse(user.Id, Array.Empty<string>()));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IValidator<LoginRequest> validator,
        AppDbContext db,
        IMembershipDirectory membershipDirectory,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var normalized = request.EmailOrPhone.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailOrPhone == normalized, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (user.IsLocked(DateTime.UtcNow))
        {
            return Results.Problem(
                title: "Account locked",
                detail: "Too many failed login attempts. Try again later.",
                statusCode: StatusCodes.Status423Locked);
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts += 1;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Unauthorized();
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(cancellationToken);

        // Cross-clinic read via the privileged directory: at login there is no tenant context yet.
        var memberships = await membershipDirectory.GetActiveMembershipsAsync(user.Id, cancellationToken);

        Guid? clinicId;
        List<string> roles;
        if (request.ClinicId is { } requestedClinic)
        {
            // Explicit clinic → only the roles the user actually holds IN THAT clinic.
            roles = memberships
                .Where(m => m.ClinicId == requestedClinic)
                .Select(m => m.Role.ToString())
                .Distinct()
                .ToList();

            if (roles.Count == 0 && !user.IsSuperAdmin)
            {
                // Never mint a token scoped to a clinic the user is not an active member of.
                return Results.Forbid();
            }

            clinicId = requestedClinic;
        }
        else
        {
            var distinctClinics = memberships.Select(m => m.ClinicId).Distinct().ToList();
            if (distinctClinics.Count == 1)
            {
                clinicId = distinctClinics[0];
                roles = memberships
                    .Where(m => m.ClinicId == clinicId)
                    .Select(m => m.Role.ToString())
                    .Distinct()
                    .ToList();
            }
            else
            {
                // 0 or >1 memberships → identity-only token; client picks a clinic via /switch-clinic.
                clinicId = null;
                roles = new List<string>();
            }
        }

        if (user.IsSuperAdmin)
        {
            roles.Add(Roles.SuperAdmin);
        }

        var tokens = await tokenService.IssueAsync(user, clinicId, roles, cancellationToken);
        tokenService.WriteAuthCookies(httpContext.Response, tokens);

        var summaries = memberships
            .Select(m => new ClinicMembershipSummary(m.ClinicId, m.ClinicName, m.Role.ToString()))
            .ToList();

        return Results.Ok(new AuthUserResponse(user.Id, roles, clinicId, summaries));
    }

    private static async Task<IResult> SwitchClinicAsync(
        SwitchClinicRequest request,
        HttpContext httpContext,
        AppDbContext db,
        IMembershipDirectory membershipDirectory,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        var userIdRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Validate against ClinicMembership — only roles backed by an active membership are issued.
        var roles = (await membershipDirectory.GetActiveRolesAsync(userId, request.ClinicId, cancellationToken))
            .ToList();
        if (roles.Count == 0 && !user.IsSuperAdmin)
        {
            return Results.Forbid();
        }

        if (user.IsSuperAdmin)
        {
            roles.Add(Roles.SuperAdmin);
        }

        var tokens = await tokenService.IssueAsync(user, request.ClinicId, roles, cancellationToken);
        tokenService.WriteAuthCookies(httpContext.Response, tokens);

        return Results.Ok(new AuthUserResponse(user.Id, roles, request.ClinicId));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var refresh)
            || string.IsNullOrWhiteSpace(refresh))
        {
            return Results.Unauthorized();
        }

        var pair = await tokenService.RotateRefreshAsync(refresh, cancellationToken);
        if (pair is null)
        {
            tokenService.ClearAuthCookies(httpContext.Response);
            return Results.Unauthorized();
        }

        tokenService.WriteAuthCookies(httpContext.Response, pair);
        return Results.Ok(new { refreshed = true });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var refresh)
            && !string.IsNullOrWhiteSpace(refresh))
        {
            await tokenService.RevokeRefreshAsync(refresh, cancellationToken);
        }

        tokenService.ClearAuthCookies(httpContext.Response);
        return Results.NoContent();
    }
}
