using ClinicQueue.Application.Abstractions;
using ClinicQueue.Domain.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application.Reporting;

public interface IClinicReportsHandler
{
    Task<VolumeReportDto> GetVolumeAsync(ReportPeriodQuery query, CancellationToken cancellationToken = default);

    Task<AbsenceRateReportDto> GetAbsenceRateAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    Task<PeakHoursReportDto> GetPeakHoursAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    Task<DepositRevenueReportDto> GetDepositRevenueAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
}

/// <summary>Clinic-scoped volume, absence, peak-hour, and deposit-revenue reports (FR-090–FR-093).</summary>
public sealed class ClinicReportsHandler : IClinicReportsHandler
{
    private readonly IAppDbContext _db;

    public ClinicReportsHandler(IAppDbContext db) => _db = db;

    public async Task<VolumeReportDto> GetVolumeAsync(
        ReportPeriodQuery query,
        CancellationToken cancellationToken = default)
    {
        var from = NormalizeUtc(query.From);
        var to = NormalizeUtc(query.To);
        var period = (query.Period ?? "daily").Trim().ToLowerInvariant();

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == query.ClinicId);

        if (from.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt <= to.Value);
        }

        var rows = await appointments
            .Select(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        IEnumerable<IGrouping<DateOnly, DateTime>> groups = period switch
        {
            "weekly" => rows.GroupBy(d => StartOfWeek(DateOnly.FromDateTime(d))),
            "monthly" => rows.GroupBy(d => new DateOnly(d.Year, d.Month, 1)),
            _ => rows.GroupBy(d => DateOnly.FromDateTime(d))
        };

        var series = groups
            .OrderBy(g => g.Key)
            .Select(g => new VolumePointDto(g.Key, g.Count()))
            .ToList();

        return new VolumeReportDto(series);
    }

    public async Task<AbsenceRateReportDto> GetAbsenceRateAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = NormalizeUtc(from);
        var toUtc = NormalizeUtc(to);

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId);

        if (fromUtc.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt <= toUtc.Value);
        }

        var absentCount = await appointments.CountAsync(
            a => a.Status == AppointmentStatus.Absent
                 || (a.Status == AppointmentStatus.CarriedOver && a.CalledAt != null),
            cancellationToken);

        var completedCount = await appointments.CountAsync(
            a => a.Status == AppointmentStatus.Completed,
            cancellationToken);

        var denominator = absentCount + completedCount;
        var rate = denominator == 0 ? 0d : (double)absentCount / denominator;

        return new AbsenceRateReportDto(rate, absentCount, completedCount);
    }

    public async Task<PeakHoursReportDto> GetPeakHoursAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = NormalizeUtc(from);
        var toUtc = NormalizeUtc(to);

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId);

        if (fromUtc.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            appointments = appointments.Where(a => a.CreatedAt <= toUtc.Value);
        }

        var stamps = await appointments.Select(a => a.CreatedAt).ToListAsync(cancellationToken);
        if (stamps.Count == 0)
        {
            return new PeakHoursReportDto([], []);
        }

        var peakDays = stamps
            .GroupBy(d => d.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(3)
            .Select(g => g.Key.ToString())
            .ToList();

        var peakHours = stamps
            .GroupBy(d => d.Hour)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        return new PeakHoursReportDto(peakDays, peakHours);
    }

    public async Task<DepositRevenueReportDto> GetDepositRevenueAsync(
        Guid clinicId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = NormalizeUtc(from);
        var toUtc = NormalizeUtc(to);

        // MVP stores no deposit amount — revenue is confirmed-deposit volume (same as platform analytics).
        var deposits = _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.Method == AppointmentMethod.PhoneDeposit
                        && a.DepositConfirmedAt != null);

        if (fromUtc.HasValue)
        {
            deposits = deposits.Where(a => a.DepositConfirmedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            deposits = deposits.Where(a => a.DepositConfirmedAt <= toUtc.Value);
        }

        var count = await deposits.CountAsync(cancellationToken);
        return new DepositRevenueReportDto(count);
    }

    private static DateOnly StartOfWeek(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday-start
        return day.AddDays(-offset);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
