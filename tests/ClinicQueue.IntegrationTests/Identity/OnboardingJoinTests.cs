using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Identity;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Identity;

/// <summary>
/// Onboarding write path under PRODUCTION conditions: the HTTP pipeline runs as the non-owner
/// <c>clinicqueue_app</c> role (RLS ENFORCED) via <see cref="Infrastructure.AppRoleApiFactory"/>.
/// A user who just registered has NO clinic_id claim, so <c>app.current_clinic</c> is empty.
/// POST /clinics/{id}/join must still create the Patient membership + audit row — which is only
/// possible because the endpoint routes those writes through the BYPASSRLS PrivilegedDbContext.
/// This test FAILS (HTTP 500) if that write path is regressed back onto the RLS-enforced app role.
/// It proves nothing under a superuser connection, so it deliberately drives the app role.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class OnboardingJoinTests(Infrastructure.PostgresFixture fixture)
{
    private const string Password = "Password123!";

    // Owner connection — used only to migrate + seed. The API under test never uses it.
    private AppDbContext OwnerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Register_then_join_creates_patient_membership_as_the_app_role()
    {
        Guid clinicId;
        await using (var db = OwnerContext())
        {
            await db.Database.MigrateAsync();
            clinicId = Guid.NewGuid();
            db.Clinics.Add(new Clinic
            {
                Id = clinicId,
                Name = "Onboarding Clinic",
                Specialty = "GP",
                SubdomainSlug = $"join-{Guid.NewGuid():N}"[..18],
                Timezone = "Africa/Cairo",
                Status = ClinicStatus.Active,
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new Infrastructure.AppRoleApiFactory(fixture);
        // HandleCookies=false → drive Bearer tokens explicitly (skips CSRF, which only guards cookies).
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Bare registration (no clinicId) → global identity with zero memberships.
        var email = $"join-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { emailOrPhone = email, password = Password });
        register.StatusCode.Should().Be(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());

        // Login with no clinic → identity-only token (no clinic_id claim), exactly the onboarding state.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = ExtractCookie(login, TokenService.AccessCookieName);

        // Join as Patient. Under the app role app.current_clinic is empty; only the privileged
        // write path lets the membership + audit rows through RLS.
        using var joinRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/clinics/{clinicId}/join");
        joinRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var join = await client.SendAsync(joinRequest);

        join.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the onboarding join must succeed under the RLS-enforced app role; body={await join.Content.ReadAsStringAsync()}");

        var body = await join.Content.ReadFromJsonAsync<JoinResponse>();
        body.Should().NotBeNull();
        body!.Role.Should().Be(nameof(ClinicRole.Patient));
        body.MembershipId.Should().NotBeEmpty();

        // The row is really persisted and pinned to the target clinic.
        await using (var verify = OwnerContext())
        {
            var membership = await verify.ClinicMemberships.AsNoTracking()
                .SingleOrDefaultAsync(m => m.Id == body.MembershipId);
            membership.Should().NotBeNull();
            membership!.ClinicId.Should().Be(clinicId);
            membership.Role.Should().Be(ClinicRole.Patient);
            membership.Status.Should().Be(MembershipStatus.Active);

            var audited = await verify.AuditLogs.AsNoTracking()
                .AnyAsync(a => a.ClinicId == clinicId && a.Action == "PatientJoinedClinic");
            audited.Should().BeTrue("the join must write a clinic-scoped audit row via the privileged path");
        }
    }

    private sealed record JoinResponse(Guid ClinicId, string ClinicName, Guid MembershipId, string Role);

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
}
