using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClinicQueue.IntegrationTests.Identity;

/// <summary>
/// Cross-clinic role isolation (Golden Rule #1 / constitution Principle I): a user with different
/// roles in different clinics must never exercise a role outside the clinic that grants it, tokens
/// are tenant-scoped, revocation takes effect immediately, and clinic_id cannot be tampered.
/// Drives the real HTTP + authorization pipeline via <see cref="WebApplicationFactory{Program}"/>.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class CrossClinicRoleTests(Infrastructure.PostgresFixture fixture)
{
    private const string SigningKey = "integration-test-signing-key-32chars!!";
    private const string Password = "Password123!";
    private const string ReceptionistEndpoint = "/api/v1/appointments/today"; // RequireAuthorization(Receptionist)

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // UseSetting has higher priority than appsettings.Development.json, whose local
            // Default connection string would otherwise point the API at the wrong database.
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Hangfire:Enabled", "false");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
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
                });
            });
        }
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private sealed record Seed(string Email, Guid UserId, Guid ClinicA, Guid ClinicB, Guid ReceptionistMembershipA);

    /// <summary>User is Receptionist in clinic A and Patient in clinic B.</summary>
    private async Task<Seed> SeedReceptionistInAPatientInBAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var email = $"cross-{Guid.NewGuid():N}@test.local";
        var user = new User { EmailOrPhone = email, PasswordHash = new PasswordHasher().Hash(Password) };
        db.Users.Add(user);

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        db.Clinics.AddRange(
            new Clinic { Id = clinicA, Name = "Clinic A", Specialty = "OB", SubdomainSlug = $"a-{Guid.NewGuid():N}"[..18], Timezone = "Africa/Cairo" },
            new Clinic { Id = clinicB, Name = "Clinic B", Specialty = "OB", SubdomainSlug = $"b-{Guid.NewGuid():N}"[..18], Timezone = "Africa/Cairo" });
        await db.SaveChangesAsync();

        var receptionistA = new ClinicMembership
        {
            ClinicId = clinicA,
            UserId = user.Id,
            Role = ClinicRole.Receptionist,
            Status = MembershipStatus.Active,
        };
        db.ClinicMemberships.AddRange(
            receptionistA,
            new ClinicMembership
            {
                ClinicId = clinicB,
                UserId = user.Id,
                Role = ClinicRole.Patient,
                Status = MembershipStatus.Active,
            });
        await db.SaveChangesAsync();

        return new Seed(email, user.Id, clinicA, clinicB, receptionistA.Id);
    }

    private static ApiFactory Factory(Infrastructure.PostgresFixture fixture) => new(fixture.ConnectionString);

    private static HttpClient Client(ApiFactory factory) =>
        // HandleCookies=false so we drive Bearer tokens explicitly (and skip CSRF, which only
        // guards cookie-authenticated requests).
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> LoginAccessTokenAsync(HttpClient client, string email, Guid? clinicId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            emailOrPhone = email,
            password = Password,
            clinicId,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"valid credentials must authenticate; body={body}");
        return ExtractCookie(response, TokenService.AccessCookieName);
    }

    private static async Task<HttpResponseMessage> GetWithBearerAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Wrong_tenant_token_cannot_call_receptionist_endpoint()
    {
        var seed = await SeedReceptionistInAPatientInBAsync();
        await using var factory = Factory(fixture);
        using var client = Client(factory);

        // Token scoped to clinic B, where the user is only a Patient.
        var tokenB = await LoginAccessTokenAsync(client, seed.Email, seed.ClinicB);
        var response = await GetWithBearerAsync(client, ReceptionistEndpoint, tokenB);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "a token scoped to clinic B (Patient) must not exercise a Receptionist endpoint");
    }

    [Fact]
    public async Task Switch_clinic_grants_the_role_held_in_the_target_clinic()
    {
        var seed = await SeedReceptionistInAPatientInBAsync();
        await using var factory = Factory(fixture);
        using var client = Client(factory);

        // Start scoped to B (Patient) → blocked.
        var tokenB = await LoginAccessTokenAsync(client, seed.Email, seed.ClinicB);
        (await GetWithBearerAsync(client, ReceptionistEndpoint, tokenB)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // Switch to clinic A, where the user is a Receptionist.
        using var switchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-clinic")
        {
            Content = JsonContent.Create(new { clinicId = seed.ClinicA }),
        };
        switchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var switchResponse = await client.SendAsync(switchRequest);
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the user is an active member of clinic A");

        var tokenA = ExtractCookie(switchResponse, TokenService.AccessCookieName);
        var allowed = await GetWithBearerAsync(client, ReceptionistEndpoint, tokenA);
        allowed.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "Receptionist@A may call the endpoint");
        allowed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoking_membership_immediately_blocks_a_still_valid_token()
    {
        var seed = await SeedReceptionistInAPatientInBAsync();
        await using var factory = Factory(fixture);
        using var client = Client(factory);

        var tokenA = await LoginAccessTokenAsync(client, seed.Email, seed.ClinicA);
        (await GetWithBearerAsync(client, ReceptionistEndpoint, tokenA)).StatusCode
            .Should().NotBe(HttpStatusCode.Forbidden, "Receptionist@A is allowed before revocation");

        // Revoke the receptionist membership (token is NOT expired).
        await using (var db = CreateContext())
        {
            var membership = await db.ClinicMemberships.SingleAsync(m => m.Id == seed.ReceptionistMembershipA);
            membership.Status = MembershipStatus.Restricted;
            await db.SaveChangesAsync();
        }

        var afterRevoke = await GetWithBearerAsync(client, ReceptionistEndpoint, tokenA);
        afterRevoke.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "request-time membership validation must block the revoked role even though the token is still valid");
    }

    [Fact]
    public async Task Tampering_clinic_id_in_the_token_is_rejected()
    {
        var seed = await SeedReceptionistInAPatientInBAsync();
        await using var factory = Factory(fixture);
        using var client = Client(factory);

        var tokenA = await LoginAccessTokenAsync(client, seed.Email, seed.ClinicA);

        // Re-point clinic_id to B without re-signing → signature no longer matches.
        var tampered = TamperClinicId(tokenA, seed.ClinicB);
        var response = await GetWithBearerAsync(client, ReceptionistEndpoint, tampered);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "a token with a tampered clinic_id fails signature validation");
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        foreach (var header in response.Headers.GetValues("Set-Cookie"))
        {
            if (header.StartsWith(name + "=", StringComparison.Ordinal))
            {
                var value = header[(name.Length + 1)..];
                var end = value.IndexOf(';');
                return end >= 0 ? value[..end] : value;
            }
        }

        throw new InvalidOperationException($"Set-Cookie '{name}' not present on response.");
    }

    private static string TamperClinicId(string jwt, Guid newClinicId)
    {
        var parts = jwt.Split('.');
        var payload = JsonNode.Parse(Base64UrlDecode(parts[1]))!;
        payload["clinic_id"] = newClinicId.ToString();
        parts[1] = Base64UrlEncode(payload.ToJsonString());
        return string.Join('.', parts);
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string Base64UrlEncode(string input) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
