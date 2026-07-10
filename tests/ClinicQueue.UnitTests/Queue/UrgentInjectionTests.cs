using ClinicQueue.Application.Queue.Numbering;
using ClinicQueue.Domain.Queue;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Queue;

/// <summary>
/// Deterministic 2:1 urgent-injection algorithm + ratio-neutral Instant Entry
/// (FR-024, FR-031; docs/plan.md §6).
/// </summary>
public sealed class UrgentInjectionTests
{
    private readonly UrgentInjectionService _sut = new();

    [Fact]
    public void Normal_assigns_next_sequential_and_increments_counter()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Normal,
            IsInstantEntry: false,
            NormalSinceLastUrgent: 0,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 0));

        result.AssignRealNumber.Should().BeTrue();
        result.RealNumber.Should().Be(1);
        result.NewNormalSinceLastUrgent.Should().Be(1);
    }

    [Fact]
    public void Second_normal_reaches_default_ratio_of_two()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Normal,
            IsInstantEntry: false,
            NormalSinceLastUrgent: 1,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 1));

        result.RealNumber.Should().Be(2);
        result.NewNormalSinceLastUrgent.Should().Be(2);
        _sut.ShouldInjectWaitingUrgent(result.NewNormalSinceLastUrgent, 2).Should().BeTrue();
    }

    [Fact]
    public void Urgent_when_ratio_met_inserts_after_last_confirmed_and_resets_counter()
    {
        // After 2 Normals (real 1,2), confirming Urgent gets real 3 — not front, not back-of-all.
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            NormalSinceLastUrgent: 2,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 2));

        result.AssignRealNumber.Should().BeTrue();
        result.RealNumber.Should().Be(3);
        result.NewNormalSinceLastUrgent.Should().Be(0);
    }

    [Fact]
    public void Urgent_when_ratio_not_met_waits_without_real_number()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            NormalSinceLastUrgent: 1,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 1));

        result.AssignRealNumber.Should().BeFalse();
        result.RealNumber.Should().BeNull();
        result.NewNormalSinceLastUrgent.Should().Be(1);
    }

    [Fact]
    public void Urgent_with_zero_normals_confirmed_must_wait()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            NormalSinceLastUrgent: 0,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 0));

        result.AssignRealNumber.Should().BeFalse();
        result.RealNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public void Urgent_respects_configurable_clinic_ratio(int ratio, int normalsNeeded)
    {
        var waiting = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            NormalSinceLastUrgent: normalsNeeded - 1,
            UrgentRatio: ratio,
            HighestAssignedRealNumber: normalsNeeded - 1));

        waiting.AssignRealNumber.Should().BeFalse();

        var ready = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: false,
            NormalSinceLastUrgent: normalsNeeded,
            UrgentRatio: ratio,
            HighestAssignedRealNumber: normalsNeeded));

        ready.AssignRealNumber.Should().BeTrue();
        ready.RealNumber.Should().Be(normalsNeeded + 1);
        ready.NewNormalSinceLastUrgent.Should().Be(0);
    }

    [Fact]
    public void Instant_entry_assigns_immediately_and_is_ratio_neutral()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Normal,
            IsInstantEntry: true,
            NormalSinceLastUrgent: 1,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 4));

        result.AssignRealNumber.Should().BeTrue();
        result.RealNumber.Should().Be(5);
        // Counter unchanged — continues as if Instant Entry never happened (FR-031).
        result.NewNormalSinceLastUrgent.Should().Be(1);
    }

    [Fact]
    public void Instant_entry_does_not_reset_counter_even_when_typed_urgent()
    {
        var result = _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Urgent,
            IsInstantEntry: true,
            NormalSinceLastUrgent: 2,
            UrgentRatio: 2,
            HighestAssignedRealNumber: 2));

        result.RealNumber.Should().Be(3);
        result.NewNormalSinceLastUrgent.Should().Be(2);
    }

    [Fact]
    public void Full_interleave_sequence_matches_plan_section_6()
    {
        var counter = 0;
        var highest = 0;

        // N1
        var n1 = _sut.Decide(Req(AppointmentType.Normal, false, counter, highest));
        n1.RealNumber.Should().Be(1);
        counter = n1.NewNormalSinceLastUrgent;
        highest = n1.RealNumber!.Value;

        // N2
        var n2 = _sut.Decide(Req(AppointmentType.Normal, false, counter, highest));
        n2.RealNumber.Should().Be(2);
        counter = n2.NewNormalSinceLastUrgent;
        highest = n2.RealNumber!.Value;
        counter.Should().Be(2);

        // U — ratio met → insert after last confirmed (real 3), reset
        var u = _sut.Decide(Req(AppointmentType.Urgent, false, counter, highest));
        u.RealNumber.Should().Be(3);
        counter = u.NewNormalSinceLastUrgent;
        highest = u.RealNumber!.Value;
        counter.Should().Be(0);

        // N3
        var n3 = _sut.Decide(Req(AppointmentType.Normal, false, counter, highest));
        n3.RealNumber.Should().Be(4);
        counter = n3.NewNormalSinceLastUrgent;

        // Instant Entry mid-cycle — does not change counter
        var instant = _sut.Decide(Req(AppointmentType.Normal, true, counter, highest = n3.RealNumber!.Value));
        instant.RealNumber.Should().Be(5);
        instant.NewNormalSinceLastUrgent.Should().Be(counter);
        highest = instant.RealNumber!.Value;

        // N4 completes ratio again
        var n4 = _sut.Decide(Req(AppointmentType.Normal, false, counter, highest));
        n4.RealNumber.Should().Be(6);
        n4.NewNormalSinceLastUrgent.Should().Be(2);
    }

    [Fact]
    public void Rejects_invalid_ratio()
    {
        var act = () => _sut.Decide(new UrgentInjectionRequest(
            AppointmentType.Normal, false, 0, UrgentRatio: 0, HighestAssignedRealNumber: 0));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static UrgentInjectionRequest Req(
        AppointmentType type,
        bool instant,
        int counter,
        int highest) =>
        new(type, instant, counter, UrgentRatio: 2, highest);
}
