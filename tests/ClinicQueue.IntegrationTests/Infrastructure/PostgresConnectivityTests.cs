using FluentAssertions;
using Npgsql;

namespace ClinicQueue.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class PostgresConnectivityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Container_accepts_connections()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync();

        result.Should().Be(1);
    }
}
