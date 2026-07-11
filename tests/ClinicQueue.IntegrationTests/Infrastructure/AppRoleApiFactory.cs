using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClinicQueue.IntegrationTests.Infrastructure;

/// <summary>
/// Request-path host wired to connect EXACTLY as production does. The API under test only ever
/// touches the NON-OWNER app role, so any missing tenant scope surfaces here just as it would in
/// production instead of being masked by a superuser connection:
/// <list type="bullet">
///   <item><c>Default</c>    → <c>clinicqueue_app</c>       (NOSUPERUSER, NOBYPASSRLS) — every HTTP request, RLS ENFORCED.</item>
///   <item><c>Privileged</c> → <c>clinicqueue_bypassrls</c> (BYPASSRLS)               — SuperAdmin/platform, jobs, onboarding writes.</item>
///   <item><c>Migrations</c> → <c>clinicqueue</c>            (owner)                   — DDL only.</item>
/// </list>
/// Tests still SEED with the owner connection (<see cref="PostgresFixture.ConnectionString"/>), but
/// the HTTP pipeline never uses it. See CLAUDE.md ("Integration tests MUST connect as the non-owner
/// app role"): a request-path test that runs as superuser proves nothing about RLS.
/// </summary>
public sealed class AppRoleApiFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-32chars!!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting outranks appsettings.Development.json, whose local Default connection string
        // would otherwise point the API at the wrong database.
        builder.UseSetting("ConnectionStrings:Default", fixture.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Privileged", fixture.BypassConnectionString);
        builder.UseSetting("ConnectionStrings:Migrations", fixture.ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", "ClinicQueue");
        builder.UseSetting("Jwt:Audience", "ClinicQueue");
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("OpenApi:EnableUi", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://app.clinicqueue.test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = fixture.AppConnectionString,
                ["ConnectionStrings:Privileged"] = fixture.BypassConnectionString,
                ["ConnectionStrings:Migrations"] = fixture.ConnectionString,
                ["ConnectionStrings:Redis"] = "",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = "ClinicQueue",
                ["Jwt:Audience"] = "ClinicQueue",
                ["Hangfire:Enabled"] = "false",
                ["Database:ApplyMigrationsOnStartup"] = "false",
            });
        });
    }
}
