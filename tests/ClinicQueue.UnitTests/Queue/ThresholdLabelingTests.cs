using ClinicQueue.Application.Queue.Positioning;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Queue;

/// <summary>FR-025 / FR-026 — wait-home when count-ahead &gt; threshold, head-to-clinic when ≤.</summary>
public sealed class ThresholdLabelingTests
{
    [Theory]
    [InlineData(5, 4, QueueInstruction.WaitHome)]
    [InlineData(10, 4, QueueInstruction.WaitHome)]
    [InlineData(1, 0, QueueInstruction.WaitHome)]
    public void Labels_wait_home_when_count_ahead_exceeds_threshold(
        int countAhead,
        int threshold,
        QueueInstruction expected)
    {
        ThresholdLabeling.Label(countAhead, threshold).Should().Be(expected);
    }

    [Theory]
    [InlineData(4, 4, QueueInstruction.HeadToClinic)]
    [InlineData(3, 4, QueueInstruction.HeadToClinic)]
    [InlineData(0, 4, QueueInstruction.HeadToClinic)]
    [InlineData(0, 0, QueueInstruction.HeadToClinic)]
    public void Labels_head_to_clinic_when_count_ahead_at_or_below_threshold(
        int countAhead,
        int threshold,
        QueueInstruction expected)
    {
        ThresholdLabeling.Label(countAhead, threshold).Should().Be(expected);
    }

    [Fact]
    public void Rejects_negative_count_ahead()
    {
        var act = () => ThresholdLabeling.Label(-1, 4);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rejects_negative_threshold()
    {
        var act = () => ThresholdLabeling.Label(0, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
