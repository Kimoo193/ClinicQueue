namespace ClinicQueue.Application.Abstractions;

/// <summary>
/// A persistence port that legitimately crosses tenant boundaries (SuperAdmin/platform
/// handlers and background jobs). Backed by a dedicated <c>clinicqueue_bypassrls</c>
/// database role that carries the PostgreSQL <c>BYPASSRLS</c> attribute, so Row-Level
/// Security is intentionally skipped. Correctness of every read/write MUST therefore be
/// guaranteed by explicit <c>ClinicId</c> predicates and by authorization (SuperAdmin-only
/// or trusted background jobs) — never rely on RLS through this port.
/// </summary>
public interface IPrivilegedDbContext : IAppDbContext;
