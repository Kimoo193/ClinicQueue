namespace ClinicQueue.Domain.Queue;

public enum QueueSessionStatus
{
    Open = 0,
    Paused = 1,
    Closed = 2
}

public enum AppointmentType
{
    Normal = 0,
    Urgent = 1
}

public enum AppointmentMethod
{
    WalkIn = 0,
    PhoneDeposit = 1
}

public enum AppointmentStatus
{
    Booked = 0,
    Present = 1,
    Inside = 2,
    Completed = 3,
    Absent = 4,
    Cancelled = 5,
    CarriedOver = 6
}
