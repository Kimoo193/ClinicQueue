using System.Linq.Expressions;
using ClinicQueue.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Infrastructure.Persistence;

/// <summary>
/// Applies the tenant filter at query execution time via expression trees.
/// Does NOT use a cached EF Core global query filter (constitution Principle I).
/// </summary>
public class BaseRepository<T> where T : class, IClinicScoped
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenantProvider;

    public BaseRepository(AppDbContext db, ITenantProvider tenantProvider)
    {
        _db = db;
        _tenantProvider = tenantProvider;
    }

    protected AppDbContext Db => _db;

    protected IQueryable<T> Query()
    {
        var query = _db.Set<T>().AsQueryable();
        return ApplyTenantFilter(query);
    }

    /// <summary>Tenant-filtered query surface for callers and isolation tests.</summary>
    public IQueryable<T> QueryTenantScoped() => Query();

    protected IQueryable<T> QueryIgnoreFilters() => _db.Set<T>().AsQueryable();

    private IQueryable<T> ApplyTenantFilter(IQueryable<T> query)
    {
        if (!_tenantProvider.HasTenant || _tenantProvider.ClinicId is not { } clinicId)
        {
            // No tenant context: return empty for clinic-scoped reads (SuperAdmin uses dedicated paths).
            return query.Where(_ => false);
        }

        // Capture clinicId in a local so the expression tree closes over a value,
        // evaluated at execution time — not baked into the EF model cache.
        Expression<Func<T, bool>> predicate = e => e.ClinicId == clinicId;
        return query.Where(predicate);
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Query().FirstOrDefaultAsync(e => EF.Property<Guid>(e, nameof(BaseEntity.Id)) == id, cancellationToken);
    }

    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        if (_tenantProvider.ClinicId is { } clinicId)
        {
            entity.ClinicId = clinicId;
        }

        await _db.Set<T>().AddAsync(entity, cancellationToken);
    }

    public virtual void Update(T entity) => _db.Set<T>().Update(entity);

    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _db.SaveChangesAsync(cancellationToken);
}
