using System.Data.Common;
using ClinicQueue.Domain.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicQueue.Infrastructure.Persistence;

/// <summary>
/// Sets PostgreSQL session variable app.current_clinic for RLS policies on each connection open.
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantProvider _tenantProvider;

    public RlsConnectionInterceptor(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetClinicSettingAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetClinicSettingAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task SetClinicSettingAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (_tenantProvider.ClinicId is { } clinicId)
        {
            // set_config(name, value, is_local) — is_local=true scopes to the current transaction when present
            command.CommandText = "SELECT set_config('app.current_clinic', @clinicId, false)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "clinicId";
            parameter.Value = clinicId.ToString();
            command.Parameters.Add(parameter);
        }
        else
        {
            command.CommandText = "SELECT set_config('app.current_clinic', '', false)";
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
