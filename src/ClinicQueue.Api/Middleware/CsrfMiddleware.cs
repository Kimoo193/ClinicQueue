using ClinicQueue.Infrastructure.Identity;

namespace ClinicQueue.Api.Middleware;

/// <summary>
/// Double-submit CSRF: state-changing requests must send X-CSRF-Token matching the cq_csrf cookie.
/// </summary>
public sealed class CsrfMiddleware
{
    public const string HeaderName = "X-CSRF-Token";

    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private readonly RequestDelegate _next;

    public CsrfMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCsrf(context.Request))
        {
            var cookie = context.Request.Cookies[TokenService.CsrfCookieName];
            var header = context.Request.Headers[HeaderName].FirstOrDefault();

            if (string.IsNullOrEmpty(cookie)
                || string.IsNullOrEmpty(header)
                || !FixedTimeEquals(cookie, header))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "CSRF validation failed",
                    detail = "Missing or mismatched X-CSRF-Token header.",
                    status = StatusCodes.Status403Forbidden
                });
                return;
            }
        }

        await _next(context);
    }

    private static bool RequiresCsrf(HttpRequest request)
    {
        if (!UnsafeMethods.Contains(request.Method))
        {
            return false;
        }

        // Cookie-authenticated API surface only; auth bootstrap endpoints set the cookie.
        if (!request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        if (request.Path.StartsWithSegments("/api/v1/auth/login")
            || request.Path.StartsWithSegments("/api/v1/auth/register")
            || request.Path.StartsWithSegments("/api/v1/auth/refresh"))
        {
            return false;
        }

        // CSRF protects cookie-based sessions; skip when no access cookie is present
        // (e.g. pure Bearer clients). Booking uses cookies from the web app.
        return request.Cookies.ContainsKey(TokenService.AccessCookieName);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
