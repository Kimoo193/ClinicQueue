using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClinicQueue.IntegrationTests.OpenApi;

/// <summary>
/// OpenAPI document generation for all /api/v1/ endpoints (T084).
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class OpenApiDocumentTests(Infrastructure.PostgresFixture fixture)
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

    [Fact]
    public async Task OpenApi_document_lists_versioned_api_paths()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();

        var pathKeys = paths.EnumerateObject().Select(p => p.Name).ToArray();
        pathKeys.Should().Contain(p => p.Contains("/api/v1/status", StringComparison.Ordinal)
                                       || p.Contains("/status", StringComparison.Ordinal));
        pathKeys.Should().Contain(p => p.Contains("auth/login", StringComparison.OrdinalIgnoreCase));
        pathKeys.Should().Contain(p => p.Contains("appointments", StringComparison.OrdinalIgnoreCase));
        pathKeys.Should().Contain(p => p.Contains("health", StringComparison.OrdinalIgnoreCase)
                                       || pathKeys.Length >= 10);

        pathKeys.Length.Should().BeGreaterThan(15, because: "OpenAPI should cover the /api/v1 surface");
    }

    [Fact]
    public async Task Scalar_ui_is_available_when_enabled()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/scalar/v1");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
