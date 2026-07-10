using FluentValidation;

namespace ClinicQueue.Application.Reporting;

public sealed record VolumePointDto(DateOnly Date, int Count);

public sealed record VolumeReportDto(IReadOnlyList<VolumePointDto> Series);

public sealed record AbsenceRateReportDto(double AbsenceRate, int AbsentCount, int CompletedCount);

public sealed record PeakHoursReportDto(
    IReadOnlyList<string> PeakDays,
    IReadOnlyList<int> PeakHours);

public sealed record DepositRevenueReportDto(int DepositRevenue);

public sealed record ReportPeriodQuery(
    Guid ClinicId,
    DateTime? From = null,
    DateTime? To = null,
    string Period = "daily");

public sealed record SubmitRatingCommand(
    Guid ClinicId,
    Guid AppointmentId,
    Guid PatientUserId,
    int Score,
    string? Comment = null);

public sealed record SubmitRatingResponse(Guid RatingId);

public sealed record ClinicRatingDto(double? AggregateRating);

public sealed record SubmitRatingRequest(int Score, string? Comment = null);

public sealed class SubmitRatingRequestValidator : AbstractValidator<SubmitRatingRequest>
{
    public SubmitRatingRequestValidator()
    {
        RuleFor(x => x.Score).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000).When(x => x.Comment is not null);
    }
}
