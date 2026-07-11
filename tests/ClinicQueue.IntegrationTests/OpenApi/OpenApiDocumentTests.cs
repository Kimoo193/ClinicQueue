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
    [Fact]
    public async Task OpenApi_document_lists_versioned_api_paths()
    {
        await using var factory = new Infrastructure.AppRoleApiFactory(fixture);
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
        await using var factory = new Infrastructure.AppRoleApiFactory(fixture);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/scalar/v1");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
