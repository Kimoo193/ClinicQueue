namespace ClinicQueue.Infrastructure.Persistence;

/// <summary>
/// Scoped switch that selects which PostgreSQL role <see cref="AppDbContext"/> connects as.
/// Default (false) → RLS-enforced app role. Set to true only for trusted cross-tenant scopes
/// (background jobs, via <c>PrivilegedJobActivator</c>) so the context and every collaborator
/// resolved in that scope share the <c>clinicqueue_bypassrls</c> connection.
/// Per-request scopes MUST leave this false.
/// </summary>
public sealed class DbConnectionRole
{
    public bool Privileged { get; set; }
}
