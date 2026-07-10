using ClinicQueue.Domain.Common;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Isolation;

/// <summary>
/// Tenant-isolation harness (constitution Principle I / SC-005).
/// Extended per entity in later phases.
/// </summary>
[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class TenantIsolationTests(Infrastructure.PostgresFixture fixture)
{
    private sealed class FixedTenantProvider(Guid? clinicId) : ITenantProvider
    {
        public Guid? ClinicId { get; } = clinicId;
        public bool HasTenant => ClinicId.HasValue;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task BaseRepository_does_not_return_other_clinic_memberships()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var userA = new User { EmailOrPhone = $"a-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        var userB = new User { EmailOrPhone = $"b-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        setup.Users.AddRange(userA, userB);
        setup.Clinics.AddRange(
            new Clinic { Id = clinicA, Name = "A", Specialty = "OB", SubdomainSlug = $"a-{Guid.NewGuid():N}"[..20] },
            new Clinic { Id = clinicB, Name = "B", Specialty = "OB", SubdomainSlug = $"b-{Guid.NewGuid():N}"[..20] });
        await setup.SaveChangesAsync();

        setup.ClinicMemberships.AddRange(
            new ClinicMembership { ClinicId = clinicA, UserId = userA.Id, Role = ClinicRole.Patient },
            new ClinicMembership { ClinicId = clinicB, UserId = userB.Id, Role = ClinicRole.Patient });
        await setup.SaveChangesAsync();

        await using var tenantAContext = CreateContext();
        var repoA = new BaseRepository<ClinicMembership>(tenantAContext, new FixedTenantProvider(clinicA));
        var visible = await repoA.QueryTenantScoped().ToListAsync();

        visible.Should().OnlyContain(m => m.ClinicId == clinicA);
        visible.Should().NotContain(m => m.ClinicId == clinicB);
    }

    [Fact]
    public async Task Tampered_clinic_id_claim_does_not_grant_cross_tenant_reads()
    {
        // Simulates a caller presenting clinic B's id while data belongs to A:
        // repository filter uses only ITenantProvider (JWT-derived), never request input.
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var user = new User { EmailOrPhone = $"t-{Guid.NewGuid():N}@test.local", PasswordHash = "x" };
        setup.Users.Add(user);
        setup.Clinics.AddRange(
            new Clinic { Id = clinicA, Name = "A", Specialty = "OB", SubdomainSlug = $"ta-{Guid.NewGuid():N}"[..20] },
            new Clinic { Id = clinicB, Name = "B", Specialty = "OB", SubdomainSlug = $"tb-{Guid.NewGuid():N}"[..20] });
        await setup.SaveChangesAsync();

        var membershipA = new ClinicMembership { ClinicId = clinicA, UserId = user.Id, Role = ClinicRole.Patient };
        setup.ClinicMemberships.Add(membershipA);
        await setup.SaveChangesAsync();

        await using var tamperedContext = CreateContext();
        var repo = new BaseRepository<ClinicMembership>(tamperedContext, new FixedTenantProvider(clinicB));
        var leaked = await repo.GetByIdAsync(membershipA.Id);

        leaked.Should().BeNull();
    }
}
