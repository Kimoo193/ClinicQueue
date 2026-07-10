using Asp.Versioning;
using ClinicQueue.Api.Authorization;
using ClinicQueue.Api.Endpoints;
using ClinicQueue.Api.Endpoints.Identity;
using ClinicQueue.Api.Endpoints.Medical;
using ClinicQueue.Api.Endpoints.Notifications;
using ClinicQueue.Api.Endpoints.Queue;
using ClinicQueue.Api.Hubs;
using ClinicQueue.Api.Middleware;
using ClinicQueue.Application.Identity.Auth;
using ClinicQueue.Application.Identity.Clinics;
using ClinicQueue.Application.Medical;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.Emergency;
using ClinicQueue.Infrastructure;
using ClinicQueue.Infrastructure.Jobs;
using ClinicQueue.Infrastructure.Notifications;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
.AllowAnonymous();

v1.MapAuthEndpoints();
v1.MapClinicEndpoints();
v1.MapClinicSearchEndpoints();
v1.MapBookingEndpoints();
v1.MapArrivalEndpoints();
v1.MapEmergencyEndpoints();
v1.MapMedicalEndpoints();
v1.MapPushSubscriptionEndpoints();

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
}

// Explicit migration apply for Development only — never auto-apply on Production
if (app.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await app.Services.ApplyMigrationsAsync();
}

app.Run();

public partial class Program;
