using Testcontainers.PostgreSql;

namespace ClinicQueue.IntegrationTests.Infrastructure;

/// <summary>
/// Shared PostgreSQL 16 container for integration tests (Testcontainers).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("clinicqueue_test")
        .WithUsername("clinicqueue")
        .WithPassword("clinicqueue")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
