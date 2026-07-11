using ClinicQueue.Infrastructure.Persistence;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Hangfire job activator that runs every job in a DI scope marked
/// <see cref="DbConnectionRole.Privileged"/> = true, so background jobs (which legitimately
/// process data across all clinics) and their collaborators use the <c>clinicqueue_bypassrls</c>
/// connection instead of the RLS-restricted app role. Correctness of cross-tenant reads/writes
/// relies on the explicit <c>ClinicId</c> predicates each job already carries.
/// </summary>
public sealed class PrivilegedJobActivator : JobActivator
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PrivilegedJobActivator(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public override JobActivatorScope BeginScope(JobActivatorContext context) =>
        new PrivilegedScope(_scopeFactory.CreateScope());

    private sealed class PrivilegedScope : JobActivatorScope
    {
        private readonly IServiceScope _scope;

        public PrivilegedScope(IServiceScope scope)
        {
            _scope = scope;
            // Must run before any AppDbContext is resolved from this scope.
            _scope.ServiceProvider.GetRequiredService<DbConnectionRole>().Privileged = true;
        }

        public override object Resolve(Type type) =>
            ActivatorUtilities.GetServiceOrCreateInstance(_scope.ServiceProvider, type);

        public override void DisposeScope() => _scope.Dispose();
    }
}
