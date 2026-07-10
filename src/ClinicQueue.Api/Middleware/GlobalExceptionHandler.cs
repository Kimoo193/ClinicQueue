using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClinicQueue.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception");

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc7807",
            Instance = httpContext.Request.Path
        };

        if (env.IsDevelopment())
        {
            problem.Detail = exception.Message;
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

public static class PaginationDefaults
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        var p = page is null or < 1 ? 1 : page.Value;
        var size = pageSize is null or < 1 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize);
        return (p, size);
    }
}
