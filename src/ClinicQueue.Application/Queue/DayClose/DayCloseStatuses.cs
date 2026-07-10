using ClinicQueue.Domain.Queue;

namespace ClinicQueue.Application.Queue.DayClose;

/// <summary>Which appointment statuses count as "unserved" at day close (FR-061).</summary>
internal static class DayCloseStatuses
{
    public static bool IsUnserved(AppointmentStatus status) => status is
        AppointmentStatus.Booked or
        AppointmentStatus.Present or
        AppointmentStatus.Inside or
        AppointmentStatus.Absent;
}
