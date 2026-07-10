using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicQueue.Api.Middleware;

/// <summary>
/// Adds defense-in-depth security headers on every response (SC-012).
/// TLS termination and HSTS also live in nginx; these cover direct API access
/// (dev / internal) and reinforce the edge configuration.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["X-XSS-Protection"] = "0";

        // API responses are JSON; keep CSP tight. SignalR/Web Push are browser-side.
        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseClinicQueueSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}

public static class CorsExtensions
{
    public const string DefaultPolicy = "ClinicQueueCors";

    /// <summary>
    /// Same-origin SPA is served via nginx; CORS is locked to configured origins
    /// (empty = same-origin only / no wildcard) for SC-012.
    /// </summary>
    public static IServiceCollection AddClinicQueueCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy(DefaultPolicy, policy =>
            {
                if (origins.Length == 0)
                {
                    // Same-origin via nginx reverse proxy — no cross-origin browser calls.
                    policy.SetIsOriginAllowed(_ => false);
                }
                else
                {
                    policy.WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
            });
        });

        return services;
    }
}
