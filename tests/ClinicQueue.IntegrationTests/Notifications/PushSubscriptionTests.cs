using ClinicQueue.Application.Notifications;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Domain.Notifications;
using ClinicQueue.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.IntegrationTests.Notifications;

[Collection(Infrastructure.PostgresCollection.Name)]
public sealed class PushSubscriptionTests(Infrastructure.PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Upsert_updates_same_user_endpoint()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var user = new User
        {
            EmailOrPhone = $"push-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.Add(user);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Push Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"ps-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        const string endpoint = "https://push.example/endpoint-same-user";
        var handler = new UpsertPushSubscriptionHandler(db);

        var firstId = await handler.HandleAsync(new UpsertPushSubscriptionCommand(
            clinicId, user.Id, endpoint, "p256-1", "auth-1", "ua"));
        var secondId = await handler.HandleAsync(new UpsertPushSubscriptionCommand(
            clinicId, user.Id, endpoint, "p256-2", "auth-2", "ua-2"));

        secondId.Should().Be(firstId);
        var row = await db.PushSubscriptions.SingleAsync(s => s.Id == firstId);
        row.P256dh.Should().Be("p256-2");
        row.Auth.Should().Be("auth-2");
        row.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Upsert_rejects_endpoint_owned_by_another_user()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var clinicId = Guid.NewGuid();
        var owner = new User
        {
            EmailOrPhone = $"owner-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        var other = new User
        {
            EmailOrPhone = $"other-{Guid.NewGuid():N}@test.local",
            PasswordHash = "x"
        };
        db.Users.AddRange(owner, other);
        db.Clinics.Add(new Clinic
        {
            Id = clinicId,
            Name = "Push Takeover Clinic",
            Specialty = "OB/GYN",
            SubdomainSlug = $"pt-{Guid.NewGuid():N}"[..20],
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        const string endpoint = "https://push.example/endpoint-taken";
        var handler = new UpsertPushSubscriptionHandler(db);
        await handler.HandleAsync(new UpsertPushSubscriptionCommand(
            clinicId, owner.Id, endpoint, "p256", "auth", null));

        var act = async () => await handler.HandleAsync(new UpsertPushSubscriptionCommand(
            clinicId, other.Id, endpoint, "p256-x", "auth-x", null));

        await act.Should().ThrowAsync<PushSubscriptionTakeoverException>();
    }
}
