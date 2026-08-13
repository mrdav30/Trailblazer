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
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 2, 3, 4, 5, 6, 7, 8, 9));

        meter.TryConsumeEnvelopes(1).Should().BeTrue();
        meter.TryConsumeBaselineAddresses(2).Should().BeTrue();
        meter.TryConsumeOverlaySlots(3).Should().BeTrue();
        meter.TryConsumeSeamCandidates(4).Should().BeTrue();
        meter.TryConsumeComponentNodes(5).Should().BeTrue();
        meter.TryConsumeImplicitEdges(6).Should().BeTrue();
        meter.TryConsumeExplicitEdges(7).Should().BeTrue();
        meter.TryConsumeDependencyEntries(8).Should().BeTrue();
        meter.TryConsumeCacheInvalidations(9).Should().BeTrue();

        meter.TryConsumeEnvelopes(1).Should().BeFalse();
        meter.TryConsumeBaselineAddresses(1).Should().BeFalse();
        meter.TryConsumeOverlaySlots(1).Should().BeFalse();
        meter.TryConsumeSeamCandidates(1).Should().BeFalse();
        meter.TryConsumeComponentNodes(1).Should().BeFalse();
        meter.TryConsumeImplicitEdges(1).Should().BeFalse();
        meter.TryConsumeExplicitEdges(1).Should().BeFalse();
        meter.TryConsumeDependencyEntries(1).Should().BeFalse();
        meter.TryConsumeCacheInvalidations(1).Should().BeFalse();
        meter.RemainingEnvelopes.Should().Be(0);
        meter.RemainingBaselineAddresses.Should().Be(0);
        meter.RemainingOverlaySlots.Should().Be(0);
        meter.RemainingSeamCandidates.Should().Be(0);
        meter.RemainingComponentNodes.Should().Be(0);
        meter.RemainingImplicitEdges.Should().Be(0);
        meter.RemainingExplicitEdges.Should().Be(0);
        meter.RemainingDependencyEntries.Should().Be(0);
        meter.RemainingCacheInvalidations.Should().Be(0);
    }

    [Fact]
    public void NegativeConsumption_ShouldFailFast()
    {
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1, 1, 1));

        Action consume = () => meter.TryConsumeComponentNodes(-1);

        consume.Should().Throw<ArgumentOutOfRangeException>();
        meter.ComponentNodes.Should().Be(0);
    }
}
