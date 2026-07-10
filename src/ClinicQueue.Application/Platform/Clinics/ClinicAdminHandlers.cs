using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Platform.Clinics;

public sealed record ListAdminClinicsQuery(
    ClinicStatus? Status = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AdminClinicListItemDto(
    Guid ClinicId,
    string Name,
    string Status,
    string? SubscriptionStatus,
    string? PlanName,
    DateTime? RenewalDate);

public sealed record AdminClinicListResult(
    IReadOnlyList<AdminClinicListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ClinicLifecycleResult(Guid ClinicId, string Status);

public interface IListAdminClinicsHandler
{
    Task<AdminClinicListResult> HandleAsync(
        ListAdminClinicsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IActivateClinicHandler
{
    Task<ClinicLifecycleResult> HandleAsync(Guid clinicId, CancellationToken cancellationToken = default);
}

public interface ISuspendClinicHandler
{
    Task<ClinicLifecycleResult> HandleAsync(Guid clinicId, CancellationToken cancellationToken = default);
}

public sealed class ListAdminClinicsHandler : IListAdminClinicsHandler
{
    private readonly IAppDbContext _db;

    public ListAdminClinicsHandler(IAppDbContext db) => _db = db;

    public async Task<AdminClinicListResult> HandleAsync(
        ListAdminClinicsQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

        var clinics = _db.Clinics.AsNoTracking().AsQueryable();
        if (query.Status.HasValue)
        {
            clinics = clinics.Where(c => c.Status == query.Status.Value);
        }

        var total = await clinics.CountAsync(cancellationToken);

        var rows = await clinics
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Status,
                Subscription = _db.Subscriptions
                    .Where(s => s.ClinicId == c.Id)
                    .OrderByDescending(s => s.RenewalDate)
                    .Select(s => new { s.Status, s.PlanName, s.RenewalDate })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new AdminClinicListItemDto(
            r.Id,
            r.Name,
            r.Status.ToString(),
            r.Subscription?.Status.ToString(),
            r.Subscription?.PlanName,
            r.Subscription?.RenewalDate)).ToList();

        return new AdminClinicListResult(items, page, pageSize, total);
    }
}

public sealed class ActivateClinicHandler : IActivateClinicHandler
{
    private readonly IAppDbContext _db;

    public ActivateClinicHandler(IAppDbContext db) => _db = db;

    public async Task<ClinicLifecycleResult> HandleAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        clinic.Status = ClinicStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);
        return new ClinicLifecycleResult(clinic.Id, clinic.Status.ToString());
    }
}

public sealed class SuspendClinicHandler : ISuspendClinicHandler
{
    private readonly IAppDbContext _db;

    public SuspendClinicHandler(IAppDbContext db) => _db = db;

    public async Task<ClinicLifecycleResult> HandleAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        clinic.Status = ClinicStatus.Suspended;
        await _db.SaveChangesAsync(cancellationToken);
        return new ClinicLifecycleResult(clinic.Id, clinic.Status.ToString());
    }
}

public sealed record UpdateSubscriptionRequest(string PlanName, DateTime RenewalDate);

public sealed class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(x => x.PlanName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RenewalDate).Must(d => d.Kind == DateTimeKind.Utc || d.Kind == DateTimeKind.Unspecified)
            .WithMessage("RenewalDate must be UTC.");
    }
}
