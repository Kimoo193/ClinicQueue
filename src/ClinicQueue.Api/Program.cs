using Asp.Versioning;
using ClinicQueue.Api.Authorization;
using ClinicQueue.Api.Endpoints;
using ClinicQueue.Api.Endpoints.Identity;
using ClinicQueue.Api.Endpoints.Medical;
using ClinicQueue.Api.Endpoints.Notifications;
using ClinicQueue.Api.Endpoints.Platform;
using ClinicQueue.Api.Endpoints.Queue;
using ClinicQueue.Api.Hubs;
using ClinicQueue.Api.Middleware;
using ClinicQueue.Api.Endpoints.Reporting;
using ClinicQueue.Application.Identity;
using ClinicQueue.Application.Identity.Auth;
using ClinicQueue.Application.Identity.Clinics;
using ClinicQueue.Application.Medical;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Platform.Clinics;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Emergency;
using ClinicQueue.Application.Reporting;
using ClinicQueue.Infrastructure;
using ClinicQueue.Infrastructure.Jobs;
using ClinicQueue.Infrastructure.Notifications;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Secrets (connection strings, JWT signing key, VAPID keys) come from environment /
// Docker secrets in Production (see docker/.env.example) and from user-secrets in
// Development. Never commit real values. CreateBuilder already loads environment
// variables and, in Development, user-secrets — no extra provider registration needed.

// Behind nginx (docker/nginx.conf): honor X-Forwarded-* so RemoteIpAddress and
// Request.Scheme reflect the real client, not the proxy — required for the per-IP
// login rate limiter (SC-013) and correct HTTPS redirect. The API is only reachable
// via nginx on the internal Docker network, so the single upstream proxy is trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddClinicQueueCors(builder.Configuration);
builder.Services.AddClinicQueueRateLimiting();
builder.Services.AddClinicQueueInfrastructure(builder.Configuration);
builder.Services.AddClinicQueueAuthorization();
// Replace Infrastructure's null publisher now that SignalR is registered.
builder.Services.RemoveAll<IQueueRealtimePublisher>();
builder.Services.AddSingleton<IQueueRealtimePublisher, SignalRQueueRealtimePublisher>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateClinicRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateAppointmentRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<OnBehalfBookingRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<SetAppointmentTypeRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<InstantEntryRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<UpsertPushSubscriptionRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<PauseQueueSessionRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateVisitRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<UpdateVisitRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<IssuePrescriptionRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<ClinicSearchRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterDependentRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<UpdateSubscriptionRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<SubmitRatingRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<InviteReceptionistRequestValidator>();
// ClaimAccountRequestValidator lives in this (Api) assembly, not Application — the calls
// above only scan Application's assembly, so this one needs its own registration.
builder.Services.AddValidatorsFromAssemblyContaining<AuthEndpoints.ClaimAccountRequestValidator>();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? string.Empty;

var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(connectionString))
{
    healthChecks.AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);
}

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    healthChecks.AddRedis(redisConnection, name: "redis", tags: ["ready"]);
}

var app = builder.Build();

// Must run first so downstream middleware sees the real client IP/scheme.
app.UseForwardedHeaders();
app.UseClinicQueueSecurityHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();

// Single-origin SPA hosting (see docker/Dockerfile.render.api): serves the built React app
// from wwwroot when present, so HttpOnly auth cookies and relative /api, /hubs calls stay
// same-origin. No-op locally where wwwroot doesn't exist — docker-compose serves the SPA via
// its own nginx/web container instead.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsExtensions.DefaultPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

// OpenAPI document + Scalar UI: off in Production unless OpenApi:EnableUi=true
// (expose only behind auth/VPN). Avoids unauthenticated API surface disclosure.
if (app.Environment.IsDevelopment()
    || builder.Configuration.GetValue("OpenApi:EnableUi", false))
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("ClinicQueue API");
        options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
    });
}

app.MapHealthEndpoints();

var versioned = app.NewVersionedApi();
var v1 = versioned.MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0);

v1.MapGet("/status", () => Results.Ok(new
{
    service = "ClinicQueue.Api",
    version = "1.0",
    utc = DateTime.UtcNow
}))
.WithName("GetStatus")
.WithSummary("Service status heartbeat")
.AllowAnonymous();

v1.MapAuthEndpoints();
v1.MapClinicEndpoints();
v1.MapMembershipEndpoints();
v1.MapOfflinePatientEndpoints();
v1.MapClinicSearchEndpoints();
v1.MapBookingEndpoints();
v1.MapArrivalEndpoints();
v1.MapEmergencyEndpoints();
v1.MapDayCloseEndpoints();
v1.MapMedicalEndpoints();
v1.MapProfileEndpoints();
v1.MapPushSubscriptionEndpoints();
v1.MapClinicAdminEndpoints();
v1.MapAnalyticsEndpoints();
v1.MapReportEndpoints();
v1.MapRatingEndpoints();

app.MapHub<QueueHub>("/hubs/queue");

if (app.Services.GetService<IRecurringJobManager>() is not null
    && builder.Configuration.GetValue("Hangfire:Enabled", false))
{
    RecurringJob.AddOrUpdate<OutboxDispatchJob>(
        "outbox-dispatch",
        job => job.ProcessPendingAsync(CancellationToken.None),
        "*/10 * * * * *");

    RecurringJob.AddOrUpdate<AutoAbsenceJob>(
        "auto-absence",
        job => job.ProcessAsync(CancellationToken.None),
        "*/30 * * * * *");

    RecurringJob.AddOrUpdate<DepositCarryOverJob>(
        "deposit-carry-over",
        job => job.ProcessAsync(CancellationToken.None),
        "*/5 * * * *");

    RecurringJob.AddOrUpdate<RenewalNoticeJob>(
        "subscription-renewal-notice",
        job => job.ProcessAsync(CancellationToken.None),
        "0 8 * * *");

    RecurringJob.AddOrUpdate<BookingReminderJob>(
        "booking-day-reminder",
        job => job.ProcessAsync(CancellationToken.None),
        "0 * * * *");
}

// Explicit opt-in only (Database:ApplyMigrationsOnStartup). Compose local sets this on `api`.
// Do not enable in real production without an ops decision — prefer reviewed migrate jobs.
if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await app.Services.ApplyMigrationsAsync(allowInProduction: true);
}

// Falls back to the SPA shell for any path that didn't match an API/hub endpoint above, so
// client-side routing works. No-op when wwwroot has no index.html (plain local dotnet run).
// Excludes /api and /hubs so a bad path there still 404s instead of returning the SPA shell —
// matches the local nginx setup, where those prefixes route to the api upstream, never `web`.
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/hubs"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    context.Response.ContentType = "text/html";
    return context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
});

app.Run();

public partial class Program;
