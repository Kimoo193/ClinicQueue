namespace ClinicQueue.Domain.Identity;

public enum ClinicRole
{
    Patient = 0,
    Receptionist = 1,
    Doctor = 2,
    ClinicOwner = 3
}

public enum MembershipStatus
{
    Active = 0,
    Restricted = 1
}

public enum ClinicStatus
{
    Active = 0,
    Suspended = 1
}

public enum SubscriptionStatus
{
    Active = 0,
    Expired = 1,
    Suspended = 2
}
