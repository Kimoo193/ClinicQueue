namespace ClinicQueue.Seeder;

/// <summary>Ids captured while seeding identity, needed to wire tenant data together.</summary>
internal sealed class SeededIds
{
    public Guid Admin { get; set; }
    public Guid GhadaOwner { get; set; }
    public Guid Doctor { get; set; }
    public Guid Reception { get; set; }
    public Guid Patient1 { get; set; }
    public Guid Patient2 { get; set; }
    public Guid Patient3 { get; set; }
    public Guid Patient4 { get; set; }
    public Guid Urgent { get; set; }
    public Guid Multi { get; set; }
    public Guid Newbie { get; set; }

    public Guid GhadaOwnerMembership { get; set; }
    public Guid DoctorMembership { get; set; }
    public Guid ReceptionMembership { get; set; }
    public Guid Patient1Membership { get; set; }
    public Guid Patient2Membership { get; set; }
    public Guid Patient3Membership { get; set; }
    public Guid Patient4Membership { get; set; }
    public Guid UrgentMembership { get; set; }
    public Guid MultiSmileMembership { get; set; }

    public List<Guid> GhadaPool { get; } = [];
    public List<Guid> GhadaPoolMemberships { get; } = [];
    public List<Guid> SmilePool { get; } = [];
    public List<Guid> SmilePoolMemberships { get; } = [];
}

public sealed record AccountRow(string Email, string Role, string Clinic, string Scenario);

public sealed record ClinicSummary(string Name, string Slug, string QrUrl);

public sealed record SeedResult(
    string Password,
    ClinicSummary Ghada,
    ClinicSummary Smile,
    IReadOnlyList<AccountRow> Accounts);
