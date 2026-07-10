using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Queue.Booking;

public sealed record ClinicSearchQuery(
    string? Specialty = null,
    string? Name = null,
    string? Location = null,
    int Take = 20);

public sealed record ClinicSearchResultDto(
    Guid ClinicId,
    string Name,
    string Specialty,
    double? AggregateRating,
    string? Location);

public interface IClinicSearchHandler
{
    Task<IReadOnlyList<ClinicSearchResultDto>> HandleAsync(
        ClinicSearchQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Public clinic search by specialty, name, or location (FR-040). AggregateRating from US6 ratings.</summary>
public sealed class ClinicSearchHandler : IClinicSearchHandler
{
    private readonly IAppDbContext _db;

    public ClinicSearchHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClinicSearchResultDto>> HandleAsync(
        ClinicSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 1, 100);
        var clinics = _db.Clinics.AsNoTracking()
            .Where(c => c.Status == ClinicStatus.Active);

        if (!string.IsNullOrWhiteSpace(query.Specialty))
        {
            var specialty = query.Specialty.Trim().ToLowerInvariant();
            clinics = clinics.Where(c => c.Specialty.ToLower().Contains(specialty));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim().ToLowerInvariant();
            clinics = clinics.Where(c => c.Name.ToLower().Contains(name));
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            // Location is stored in ContactInfo until a dedicated field exists (FR-040).
            var location = query.Location.Trim().ToLowerInvariant();
            clinics = clinics.Where(c =>
                c.ContactInfo != null && c.ContactInfo.ToLower().Contains(location));
        }

        return await clinics
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new ClinicSearchResultDto(
                c.Id,
                c.Name,
                c.Specialty,
                c.AggregateRating,
                c.ContactInfo))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ClinicSearchRequest(string? Specialty, string? Name, string? Location);

public sealed class ClinicSearchRequestValidator : AbstractValidator<ClinicSearchRequest>
{
    public ClinicSearchRequestValidator()
    {
        RuleFor(x => x.Specialty).MaximumLength(200).When(x => x.Specialty is not null);
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Location).MaximumLength(200).When(x => x.Location is not null);
    }
}
