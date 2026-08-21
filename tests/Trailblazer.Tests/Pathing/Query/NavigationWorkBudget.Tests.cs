using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Query;

public sealed class NavigationWorkBudgetTests
{
    [Fact]
    public void NavigationWorkBudget_ShouldPreserveEveryCounterInExactIdentity()
    {
        NavigationWorkBudget first = CreateNavigationBudget(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        NavigationWorkBudget same = CreateNavigationBudget(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        NavigationWorkBudget different = CreateNavigationBudget(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12);

        first.MaxLookupProbes.Should().Be(1);
        first.MaxEndpointCandidates.Should().Be(2);
        first.MaxExpandedNodes.Should().Be(3);
        first.MaxEvaluatedEdges.Should().Be(4);
        first.MaxConnectionLegs.Should().Be(5);
        first.MaxTransitionCandidates.Should().Be(6);
        first.MaxTransitionPairs.Should().Be(7);
        first.MaxStagedLegAttempts.Should().Be(8);
        first.MaxTraceIntervals.Should().Be(9);
        first.MaxCoveredVoxelIntervals.Should().Be(10);
        first.MaxSimplificationRays.Should().Be(11);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different);
    }

    [Fact]
    public void NavigationWorkBudget_ShouldAllowZeroAndRejectNegativeCounters()
    {
        CreateNavigationBudget(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            .Should().Be(default(NavigationWorkBudget));

        for (int invalidIndex = 0; invalidIndex < 11; invalidIndex++)
        {
            int[] values = new int[11];
            values[invalidIndex] = -1;
            Action construct = () => _ = CreateNavigationBudget(values);

            construct.Should().Throw<ArgumentOutOfRangeException>(
                $"counter {invalidIndex} must be finite and non-negative");
        }
    }

    [Fact]
    public void NavigationWorkMeter_ShouldPreserveTheFullGridCandidateDomain()
    {
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: int.MaxValue,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: int.MaxValue,
            maxSimplificationRays: 0));

        meter.RemainingGridCandidateWork.Should().Be(2L * int.MaxValue);
    }

    [Fact]
    public void GuideSampleWorkBudget_ShouldPreserveEveryCounterInExactIdentity()
    {
        GuideSampleWorkBudget first = CreateGuideBudget(1, 2, 3, 4, 5, 6, 7);
        GuideSampleWorkBudget same = CreateGuideBudget(1, 2, 3, 4, 5, 6, 7);
        GuideSampleWorkBudget different = CreateGuideBudget(1, 2, 3, 4, 5, 6, 8);

        first.MaxCurrentNodeLookupProbes.Should().Be(1);
        first.MaxCursorLegScans.Should().Be(2);
        first.MaxCursorRebases.Should().Be(3);
        first.MaxPortalChecks.Should().Be(4);
        first.MaxPrismChecks.Should().Be(5);
        first.MaxTraceIntervals.Should().Be(6);
        first.MaxLocalRecoveryAttempts.Should().Be(7);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different);
    }

    [Fact]
    public void GuideSampleWorkBudget_ShouldAllowZeroAndRejectNegativeCounters()
    {
        CreateGuideBudget(0, 0, 0, 0, 0, 0, 0).Should().Be(default(GuideSampleWorkBudget));

        for (int invalidIndex = 0; invalidIndex < 7; invalidIndex++)
        {
            int[] values = new int[7];
            values[invalidIndex] = -1;
            Action construct = () => _ = CreateGuideBudget(values);

            construct.Should().Throw<ArgumentOutOfRangeException>(
                $"counter {invalidIndex} must be finite and non-negative");
        }
    }

    private static NavigationWorkBudget CreateNavigationBudget(params int[] values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10]);

    private static GuideSampleWorkBudget CreateGuideBudget(params int[] values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
}
