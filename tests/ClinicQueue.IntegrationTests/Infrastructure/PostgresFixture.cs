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

    /// <summary>
    /// Non-owner app role connection — RLS is ENFORCED. POOLED, for the request-path host
    /// (<see cref="AppRoleApiFactory"/>): the RlsConnectionInterceptor re-stamps app.current_clinic
    /// on every open, so pooling is safe and avoids exhausting connections under load.
    /// </summary>
    public string AppConnectionString => WithRole("clinicqueue_app", AppRolePassword);

    /// <summary>BYPASSRLS role connection (POOLED) — cross-tenant reads/writes succeed by design.</summary>
    public string BypassConnectionString => WithRole("clinicqueue_bypassrls", BypassRolePassword);

    /// <summary>
    /// Non-owner app role connection, POOLING DISABLED — for raw-SQL RLS tests that manage the
    /// app.current_clinic session variable by hand and must not inherit it from a recycled
    /// connection. Do NOT use for the API host (see <see cref="AppConnectionString"/>).
    /// </summary>
    public string AppConnectionStringUnpooled => WithRole("clinicqueue_app", AppRolePassword, pooling: false);

    /// <summary>BYPASSRLS role connection, POOLING DISABLED — for raw-SQL RLS tests.</summary>
    public string BypassConnectionStringUnpooled => WithRole("clinicqueue_bypassrls", BypassRolePassword, pooling: false);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ProvisionRolesAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string WithRole(string username, string password, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = username,
            Password = password,
            Pooling = pooling,
            // Postgres max_connections defaults to 100 with 3 reserved for superusers. The
            // non-owner app role only reaches ~97 slots, so the default pool max of 100 would fail
            // under load ("slots reserved for superuser"). Cap well below that; a handful of one-shot
            // reads recycle fast, so a small bounded pool sustains the 500-connection SignalR gate.
            MaxPoolSize = pooling ? 40 : 1,
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
