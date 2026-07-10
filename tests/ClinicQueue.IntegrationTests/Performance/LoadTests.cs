using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClinicQueue.Api.Hubs;
using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Notifications;
using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Persistence;
using ClinicQueue.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.IntegrationTests.Performance;

/// <summary>
/// Performance release gates (SC-006, SC-007, SC-008 / T081).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class LoadTests(Infrastructure.PostgresFixture fixture)
{
    private const string SigningKey = "integration-test-signing-key-32chars!!";

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:Issuer", "ClinicQueue");
            builder.UseSetting("Jwt:Audience", "ClinicQueue");
            builder.UseSetting("Hangfire:Enabled", "false");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("OpenApi:EnableUi", "true");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = connectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:Issuer"] = "ClinicQueue",
                    ["Jwt:Audience"] = "ClinicQueue",
                    ["Hangfire:Enabled"] = "false",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["OpenApi:EnableUi"] = "true"
                });
            });
        }
    }

    private static string CreateAccessToken(Guid userId, Guid clinicId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtTenantProvider.ClinicIdClaimType, clinicId.ToString())
        };
        var token = new JwtSecurityToken(
            issuer: "ClinicQueue",
            audience: "ClinicQueue",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<(Guid ClinicId, Guid UserId, string Token)> SeedPatientAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();
        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"perf-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Perf Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"perf-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();
        db.ClinicMemberships.Add(new ClinicMembership
        {
            ClinicId = clinicId,
            UserId = user.Id,
            Role = ClinicRole.Patient,
            Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();
        return (clinicId, user.Id, CreateAccessToken(user.Id, clinicId, Roles.Patient));
    }

    [Fact]
    public async Task Status_read_p95_is_at_most_300ms()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        // Warm-up
        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync("/api/v1/status")).EnsureSuccessStatusCode();
        }

        var samples = new List<double>(100);
        for (var i = 0; i < 100; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.GetAsync("/api/v1/status");
            sw.Stop();
            response.EnsureSuccessStatusCode();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        p95.Should().BeLessThanOrEqualTo(300, because: "SC-006 requires read p95 ≤ 300ms");
    }

    [Fact]
    public async Task Position_update_reaches_client_within_2_seconds()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (clinicId, userId, token) = await SeedPatientAsync(db);
        var appointmentId = Guid.NewGuid();

        var tcs = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hubUrl = new Uri(factory.Server.BaseAddress!, "/hubs/queue");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<object>("positionUpdated", _ =>
        {
            tcs.TrySetResult(DateTime.UtcNow);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinClinic", clinicId);

        var publisher = scope.ServiceProvider.GetRequiredService<IQueueRealtimePublisher>();
        var sentAt = DateTime.UtcNow;
        await publisher.PublishPositionAsync(
            userId,
            clinicId,
            appointmentId,
            countAhead: 3,
            QueueInstruction.WaitHome);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().Be(tcs.Task, because: "SC-007 requires position update ≤ 2s");
        var receivedAt = await tcs.Task;
        var latency = receivedAt - sentAt;
        latency.TotalSeconds.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Sustains_at_least_500_concurrent_SignalR_connections()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (clinicId, userId, token) = await SeedPatientAsync(db);

        const int target = 500;
        var connections = new List<HubConnection>(target);
        var hubUrl = new Uri(factory.Server.BaseAddress!, "/hubs/queue");

        try
        {
            var connectTasks = Enumerable.Range(0, target).Select(async _ =>
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                        options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    })
                    .Build();
                await connection.StartAsync();
                await connection.InvokeAsync("JoinClinic", clinicId);
                return connection;
            });

            connections.AddRange(await Task.WhenAll(connectTasks));
            connections.Should().HaveCount(target);
            connections.Should().OnlyContain(c => c.State == HubConnectionState.Connected);
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
