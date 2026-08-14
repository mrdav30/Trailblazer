using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class MaintenanceWorkMeterTests
{
    [Fact]
    public void Counters_ShouldDebitIndependentlyAndRejectWithoutPartialConsumption()
    {
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 2, 3, 4, 5, 6));

        meter.TryConsumeEnvelopes(1).Should().BeTrue();
        meter.TryConsumeBaselineAddresses(2).Should().BeTrue();
        meter.TryConsumeOverlaySlots(3).Should().BeTrue();
        meter.TryConsumeComponentNodes(4).Should().BeTrue();
        meter.TryConsumeExplicitEdges(5).Should().BeTrue();
        meter.TryConsumeDependencyEntries(6).Should().BeTrue();

        meter.TryConsumeEnvelopes(1).Should().BeFalse();
        meter.TryConsumeBaselineAddresses(1).Should().BeFalse();
        meter.TryConsumeOverlaySlots(1).Should().BeFalse();
        meter.TryConsumeComponentNodes(1).Should().BeFalse();
        meter.TryConsumeExplicitEdges(1).Should().BeFalse();
        meter.TryConsumeDependencyEntries(1).Should().BeFalse();
        meter.RemainingEnvelopes.Should().Be(0);
        meter.RemainingBaselineAddresses.Should().Be(0);
        meter.RemainingOverlaySlots.Should().Be(0);
        meter.RemainingComponentNodes.Should().Be(0);
        meter.RemainingExplicitEdges.Should().Be(0);
        meter.RemainingDependencyEntries.Should().Be(0);
    }

    [Fact]
    public void NegativeConsumption_ShouldFailFast()
    {
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1));

        Action consume = () => meter.TryConsumeComponentNodes(-1);

        consume.Should().Throw<ArgumentOutOfRangeException>();
        meter.ComponentNodes.Should().Be(0);
    }
}
