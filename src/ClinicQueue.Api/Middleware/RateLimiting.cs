using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicQueue.Api.Middleware;

public static class RateLimitingPolicies
{
    public const string Login = "login";
    public const string Sensitive = "sensitive";
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Login/sensitive fixed-window limits. Production defaults stay tight.
    /// Set env <c>CLINICQUEUE_RELAX_RATE_LIMIT=1</c> for local Playwright (not Development alone —
    /// integration tests run as Development and assert 429 enforcement).
    /// </summary>
    public static IServiceCollection AddClinicQueueRateLimiting(this IServiceCollection services)
    {
        var relaxed = string.Equals(
            Environment.GetEnvironmentVariable("CLINICQUEUE_RELAX_RATE_LIMIT"),
            "1",
            StringComparison.Ordinal);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitingPolicies.Login, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = relaxed ? 1_000 : 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(RateLimitingPolicies.Sensitive, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = relaxed ? 1_000 : 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}
