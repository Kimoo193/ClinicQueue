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

        var membershipQuery = db.ClinicMemberships.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active);

        if (request.ClinicId.HasValue)
        {
            membershipQuery = membershipQuery.Where(m => m.ClinicId == request.ClinicId.Value);
        }

        var memberships = await membershipQuery.ToListAsync(cancellationToken);
        var roles = memberships.Select(m => m.Role.ToString()).Distinct().ToList();
        if (user.IsSuperAdmin)
        {
            roles.Add(Roles.SuperAdmin);
        }

        Guid? clinicId = request.ClinicId
            ?? memberships.Select(m => (Guid?)m.ClinicId).FirstOrDefault();

        var tokens = await tokenService.IssueAsync(user, clinicId, roles, cancellationToken);
        tokenService.WriteAuthCookies(httpContext.Response, tokens);

        return Results.Ok(new AuthUserResponse(user.Id, roles));
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
