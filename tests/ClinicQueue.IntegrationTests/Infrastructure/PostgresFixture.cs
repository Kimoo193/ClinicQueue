using Npgsql;
using Testcontainers.PostgreSql;

namespace ClinicQueue.IntegrationTests.Infrastructure;

/// <summary>
/// Shared PostgreSQL 16 container for integration tests (Testcontainers).
/// The container superuser (<c>clinicqueue</c>) owns tables and runs migrations. On startup we
/// also provision the two non-owner roles the production deployment uses, so RLS-enforcement
/// tests can connect exactly as the app would:
///   <c>clinicqueue_app</c>       — NOSUPERUSER, NOBYPASSRLS (RLS applies) → <see cref="AppConnectionString"/>
///   <c>clinicqueue_bypassrls</c> — NOSUPERUSER, BYPASSRLS   (RLS skipped) → <see cref="BypassConnectionString"/>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AppRolePassword = "app-role-pw";
    private const string BypassRolePassword = "bypass-role-pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("clinicqueue_test")
        .WithUsername("clinicqueue")
        .WithPassword("clinicqueue")
        .Build();

    /// <summary>Owner/superuser connection — bypasses RLS. Used for migrations and seeding.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Non-owner app role connection — RLS is ENFORCED (proves isolation).</summary>
    public string AppConnectionString => WithRole("clinicqueue_app", AppRolePassword);

    /// <summary>BYPASSRLS role connection — cross-tenant reads/writes succeed by design.</summary>
    public string BypassConnectionString => WithRole("clinicqueue_bypassrls", BypassRolePassword);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ProvisionRolesAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string WithRole(string username, string password) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = username,
            Password = password,
            // Non-owner roles must not use the superuser's pooled connections.
            Pooling = false,
        }.ConnectionString;

    private async Task ProvisionRolesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_app') THEN
                    CREATE ROLE clinicqueue_app LOGIN PASSWORD '{AppRolePassword}'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_bypassrls') THEN
                    CREATE ROLE clinicqueue_bypassrls LOGIN PASSWORD '{BypassRolePassword}'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS;
                END IF;
            END
            $$;

            GRANT USAGE ON SCHEMA public TO clinicqueue_app, clinicqueue_bypassrls;

            -- Tables created LATER by the owner (EF migrations) auto-grant DML to both roles,
            -- matching docker/postgres-init/01-roles.sh.
            ALTER DEFAULT PRIVILEGES FOR ROLE clinicqueue IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO clinicqueue_app, clinicqueue_bypassrls;
            ALTER DEFAULT PRIVILEGES FOR ROLE clinicqueue IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO clinicqueue_app, clinicqueue_bypassrls;
            """;
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
