using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClinicQueue.IntegrationTests.Security;

/// <summary>
/// Security pass (SC-012, SC-013 / T082): headers, CORS posture, TLS edge config,
/// rate limiting wiring, and no committed secrets.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class SecurityPassTests(Infrastructure.PostgresFixture fixture)
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
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.clinicqueue.test");
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
                    ["OpenApi:EnableUi"] = "true",
                    ["Cors:AllowedOrigins:0"] = "https://app.clinicqueue.test"
                });
            });
        }
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task Cors_does_not_reflect_arbitrary_origins()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/status");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            because: "CORS must not echo untrusted origins (SC-012)");
    }

    [Fact]
    public async Task Login_rate_limiting_policy_is_enforced()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 12; i++)
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                emailOrPhone = "rate-limit@test.local",
                password = "not-a-real-password"
            });
        }

        last.Should().NotBeNull();
        last!.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Nginx_enforces_tls12_plus_and_security_headers()
    {
        var nginxPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker", "nginx.conf"));
        if (!File.Exists(nginxPath))
        {
            nginxPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), "docker", "nginx.conf"));
        }

        File.Exists(nginxPath).Should().BeTrue($"expected nginx.conf at {nginxPath}");
        var nginx = File.ReadAllText(nginxPath);
        nginx.Should().Contain("ssl_protocols       TLSv1.2 TLSv1.3");
        nginx.Should().Contain("Strict-Transport-Security");
        nginx.Should().Contain("Content-Security-Policy");
        nginx.Should().Contain("return 301 https://");
    }

    [Fact]
    public void Source_tree_has_no_hardcoded_production_secrets()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        if (!Directory.Exists(Path.Combine(repoRoot, "src")))
        {
            repoRoot = Directory.GetCurrentDirectory();
        }

        var secretPatterns = new[]
        {
            new Regex(@"-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled),
            new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),
            new Regex(@"(?i)(api[_-]?key|secret|password)\s*=\s*[""'](?!\$\{)[^""']{16,}[""']", RegexOptions.Compiled)
        };

        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "backups", "TestResults"
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => skipDirs.Contains(p)))
            {
                continue;
            }

            var ext = Path.GetExtension(file);
            if (ext is not (".cs" or ".ts" or ".tsx" or ".json" or ".yml" or ".yaml" or ".env" or ".sh" or ".md" or ".conf"))
            {
                continue;
            }

            // Allow documented placeholders / examples only.
            if (relative.Contains(".env.example", StringComparison.Ordinal)
                || relative.Contains("BACKUP.md", StringComparison.Ordinal)
                || relative.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            foreach (var pattern in secretPatterns)
            {
                if (pattern.IsMatch(text)
                    && !text.Contains("change-me", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("example", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{relative} matches {pattern}");
                }
            }
        }

        offenders.Should().BeEmpty("no real secrets should be committed (SC-012)");
    }
}
