using ClinicQueue.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Infrastructure.Persistence;

/// <summary>
/// Cross-tenant persistence for SuperAdmin/platform handlers and background jobs.
/// Connects with the <c>clinicqueue_bypassrls</c> role (PostgreSQL BYPASSRLS) and is
/// registered WITHOUT the <see cref="RlsConnectionInterceptor"/>: RLS is intentionally
/// skipped here. Every query MUST scope by <c>ClinicId</c> explicitly. Do not resolve this
/// context on per-tenant request paths — those use <see cref="AppDbContext"/> (RLS-enforced).
/// </summary>
public sealed class PrivilegedDbContext : AppDbContext, IPrivilegedDbContext
{
    public PrivilegedDbContext(DbContextOptions<PrivilegedDbContext> options) : base(options)
    {
    }
}
