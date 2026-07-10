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
    public static IServiceCollection AddClinicQueueRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitingPolicies.Login, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
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
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}
