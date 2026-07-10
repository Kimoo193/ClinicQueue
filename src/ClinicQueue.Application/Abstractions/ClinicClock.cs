namespace ClinicQueue.Application.Abstractions;

/// <summary>
/// Clinic-local calendar day from UTC (constitution: UTC in DB, timezone at edges).
/// </summary>
public static class ClinicClock
{
    public static DateOnly Today(string timezoneId, DateTime? utcNow = null)
    {
        var zone = ResolveZone(timezoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow ?? DateTime.UtcNow, zone);
        return DateOnly.FromDateTime(local);
    }

    private static TimeZoneInfo ResolveZone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timezoneId) ? "Africa/Cairo" : timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
    }
}
