using System.Text;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Identity;
using ClinicQueue.Application.Medical;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Platform;
using ClinicQueue.Application.Platform.Analytics;
using ClinicQueue.Application.Platform.Clinics;
using ClinicQueue.Application.Platform.Export;
using ClinicQueue.Application.Platform.Subscriptions;
using ClinicQueue.Application.Queue.Booking;
using ClinicQueue.Application.Queue.DayClose;
using ClinicQueue.Application.Queue.Emergency;
using ClinicQueue.Application.Queue.Notifications;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Application.Queue.Sessions;
using ClinicQueue.Application.Reporting;
using ClinicQueue.Domain.Common;
using ClinicQueue.Infrastructure.Auditing;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Jobs;
using ClinicQueue.Infrastructure.Notifications;
using ClinicQueue.Infrastructure.Persistence;
using ClinicQueue.Infrastructure.Tenancy;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace ClinicQueue.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddClinicQueueInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<VapidOptions>(configuration.GetSection(VapidOptions.SectionName));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddSingleton<Lib.Net.Http.WebPush.PushServiceClient>();
        services.AddScoped<ITenantProvider, JwtTenantProvider>();
        services.AddScoped<RlsConnectionInterceptor>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<INotificationChannel, SignalRNotificationChannel>();
        services.AddScoped<INotificationChannel, WebPushNotificationChannel>();
        services.AddScoped<OutboxDispatchJob>();
        services.AddScoped<AutoAbsenceJob>();
        services.AddScoped<DepositCarryOverJob>();
        services.AddScoped<RenewalNoticeJob>();
        services.AddScoped<BookingReminderJob>();
        services.AddScoped<IAbsencePolicyService, AbsencePolicyService>();
        services.AddScoped<IClinicAccessGate, ClinicAccessGate>();
        services.AddScoped<IListAdminClinicsHandler, ListAdminClinicsHandler>();
        services.AddScoped<IActivateClinicHandler, ActivateClinicHandler>();
        services.AddScoped<ISuspendClinicHandler, SuspendClinicHandler>();
        services.AddScoped<IUpdateSubscriptionHandler, UpdateSubscriptionHandler>();
        services.AddScoped<ISubscriptionLifecycleService, SubscriptionLifecycleService>();
        services.AddScoped<IPlatformAnalyticsHandler, PlatformAnalyticsHandler>();
        services.AddScoped<IClinicExportHandler, ClinicExportHandler>();
        services.AddScoped<IQueueSessionService, QueueSessionService>();
        services.AddScoped<ICreateAppointmentHandler, CreateAppointmentHandler>();
        services.AddScoped<IOnBehalfBookingHandler, OnBehalfBookingHandler>();
        services.AddScoped<ISetAppointmentTypeHandler, SetAppointmentTypeHandler>();
        services.AddScoped<ICancelAppointmentHandler, CancelAppointmentHandler>();
        services.AddScoped<IListPatientAppointmentsHandler, ListPatientAppointmentsHandler>();
        services.AddScoped<IClinicSearchHandler, ClinicSearchHandler>();
        services.AddScoped<IDependentBookingHandler, DependentBookingHandler>();
        services.AddScoped<IRegisterDependentHandler, RegisterDependentHandler>();
        services.AddScoped<IListDependentsHandler, ListDependentsHandler>();
        services.AddScoped<IUrgentInjectionService, UrgentInjectionService>();
        services.AddScoped<IAssignRealNumberHandler, AssignRealNumberHandler>();
        services.AddScoped<ICallNextHandler, CallNextHandler>();
        services.AddScoped<IInstantEntryHandler, InstantEntryHandler>();
        services.AddScoped<IPauseQueueSessionHandler, PauseQueueSessionHandler>();
        services.AddScoped<IResumeQueueSessionHandler, ResumeQueueSessionHandler>();
        services.AddScoped<ICarryOverService, CarryOverService>();
        services.AddScoped<ICloseDaySessionHandler, CloseDaySessionHandler>();
        services.AddScoped<ICarryOverHandler, CarryOverHandler>();
        services.AddScoped<IConfirmDepositHandler, ConfirmDepositHandler>();
        services.AddScoped<ICreateVisitHandler, CreateVisitHandler>();
        services.AddScoped<IUpdateVisitHandler, UpdateVisitHandler>();
        services.AddScoped<IGetVisitHandler, GetVisitHandler>();
        services.AddScoped<IIssuePrescriptionHandler, IssuePrescriptionHandler>();
        services.AddScoped<IListPatientVisitsHandler, ListPatientVisitsHandler>();
        services.AddScoped<IGetPatientProfileHandler, GetPatientProfileHandler>();
        services.AddScoped<IUpdatePatientProfileHandler, UpdatePatientProfileHandler>();
        services.AddScoped<IClinicReportsHandler, ClinicReportsHandler>();
        services.AddScoped<ISubmitRatingHandler, SubmitRatingHandler>();
        services.AddScoped<IGetClinicRatingHandler, GetClinicRatingHandler>();
        services.AddScoped<IInviteReceptionistHandler, InviteReceptionistHandler>();
        services.AddScoped<IListReceptionistsHandler, ListReceptionistsHandler>();
        services.AddScoped<IRevokeReceptionistHandler, RevokeReceptionistHandler>();
        services.AddScoped<ICountAheadService, CountAheadService>();
        services.AddScoped<IQueueNotificationRules, QueueNotificationRules>();
        services.AddScoped<IUpsertPushSubscriptionHandler, UpsertPushSubscriptionHandler>();
        services.TryAddSingleton<IQueueRealtimePublisher, NullQueueRealtimePublisher>();

        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["ConnectionStrings__Default"]
            ?? string.Empty;

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
            options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var signingKey = configuration["Jwt:SigningKey"]
            ?? configuration["JWT_SIGNING_KEY"]
            ?? jwt.SigningKey;

        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured via environment / user-secrets (≥32 characters).");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    // Pin algorithm — never trust token header alg / never allow "none"
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // SignalR WebSockets cannot send cookies reliably across all hosts;
                        // accept the access_token query string for hub paths only.
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        else if (string.IsNullOrEmpty(context.Token)
                            && context.Request.Cookies.TryGetValue(TokenService.AccessCookieName, out var cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        var redisConnection = configuration.GetConnectionString("Redis")
            ?? configuration["ConnectionStrings__Redis"]
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSignalR().AddStackExchangeRedis(redisConnection, options =>
            {
                options.Configuration.ChannelPrefix =
                    new RedisChannel("clinicqueue", RedisChannel.PatternMode.Literal);
            });
        }
        else
        {
            services.AddSignalR();
        }

        var hangfireEnabled = configuration.GetValue("Hangfire:Enabled", false);
        if (hangfireEnabled && !string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

            if (configuration.GetValue("Hangfire:ServerOnly", false)
                || configuration.GetValue("Hangfire:Enabled", false))
            {
                services.AddHangfireServer();
            }
        }

        return services;
    }

    /// <summary>
    /// Applies EF migrations explicitly. Never call automatically in Production without an ops step.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, bool allowInProduction = false)
    {
        using var scope = services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        if (env.IsProduction() && !allowInProduction)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
