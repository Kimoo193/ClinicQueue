using ClinicQueue.Application.Abstractions;
using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using ClinicQueue.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Reporting;

public interface ISubmitRatingHandler
{
    Task<SubmitRatingResponse> HandleAsync(
        SubmitRatingCommand command,
        CancellationToken cancellationToken = default);
}

public interface IGetClinicRatingHandler
{
    Task<ClinicRatingDto> HandleAsync(Guid clinicId, CancellationToken cancellationToken = default);
}

/// <summary>Patient post-visit rating submission and clinic aggregate (FR-094, FR-095).</summary>
public sealed class SubmitRatingHandler : ISubmitRatingHandler
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public SubmitRatingHandler(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<SubmitRatingResponse> HandleAsync(
        SubmitRatingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Score is < 1 or > 5)
        {
            throw new InvalidOperationException("Score must be between 1 and 5.");
        }

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == command.AppointmentId && a.ClinicId == command.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("Appointment not found.");

        if (appointment.BookedByUserId != command.PatientUserId)
        {
            throw new UnauthorizedAccessException("Only the booking patient may rate this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            throw new InvalidOperationException("Only completed appointments can be rated.");
        }

        var existing = await _db.Ratings.AsNoTracking()
            .AnyAsync(r => r.AppointmentId == appointment.Id, cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException("This appointment already has a rating.");
        }

        var rating = new Rating
        {
            ClinicId = command.ClinicId,
            AppointmentId = appointment.Id,
            Score = command.Score,
            Comment = string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment.Trim()
        };
        _db.Ratings.Add(rating);

        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == command.ClinicId, cancellationToken)
            ?? throw new InvalidOperationException("Clinic not found.");

        var priorScores = await _db.Ratings
            .Where(r => r.ClinicId == command.ClinicId)
            .Select(r => r.Score)
            .ToListAsync(cancellationToken);
        priorScores.Add(command.Score);
        clinic.AggregateRating = priorScores.Average();

        _audit.Append(
            command.ClinicId,
            command.PatientUserId,
            "rating.submitted",
            nameof(Rating),
            rating.Id);

        await _db.SaveChangesAsync(cancellationToken);

        return new SubmitRatingResponse(rating.Id);
    }
}

public sealed class GetClinicRatingHandler : IGetClinicRatingHandler
{
    private readonly IAppDbContext _db;

    public GetClinicRatingHandler(IAppDbContext db) => _db = db;

    public async Task<ClinicRatingDto> HandleAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var rating = await _db.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => c.AggregateRating)
            .FirstOrDefaultAsync(cancellationToken);

        return new ClinicRatingDto(rating);
    }
}
