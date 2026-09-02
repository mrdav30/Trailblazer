//=======================================================================
// NavigationSearchFinalizationRuleTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Search;

public sealed class NavigationSearchFinalizationRuleTests
{
    [Theory]
    [InlineData("candidate", "other", 1, 2, false)]
    [InlineData("candidate", "candidate", 3, 2, false)]
    [InlineData("candidate", "candidate", 2, 2, true)]
    [InlineData("candidate", "candidate", 1, 2, true)]
    public void SimplificationRayAcceptance_ShouldRequireExactEndAndNoCostIncrease(
        string candidateMapId,
        string actualMapId,
        int traversalCost,
        int rawCost,
        bool expected)
    {
        NavigationSearchFinalizationRules.ShouldAcceptSimplificationRay(
                new NavigationCellAddress(actualMapId, default),
                new NavigationCellAddress(candidateMapId, default),
                (Fixed64)traversalCost,
                (Fixed64)rawCost)
            .Should().Be(expected);
    }

    [Fact]
    public void SimplificationAdmission_ShouldRequirePathRayReservationAndMeterCapacity()
    {
        NavigationSearchFinalizationRules.TryGetFinalizationLookupReservation(
                componentCount: 2,
                pageCount: 3,
                out int exactReservation)
            .Should().BeTrue();

        NavigationSearchFinalizationRules.TryAdmitSimplification(
                pathNodeCount: 1,
                remainingSimplificationRays: 1,
                componentCount: 2,
                pageCount: 3,
                Meter(exactReservation),
                out int shortPathReservation)
            .Should().BeFalse();
        shortPathReservation.Should().Be(0);

        NavigationSearchFinalizationRules.TryAdmitSimplification(
                pathNodeCount: 2,
                remainingSimplificationRays: 0,
                componentCount: 2,
                pageCount: 3,
                Meter(exactReservation),
                out int noRayReservation)
            .Should().BeFalse();
        noRayReservation.Should().Be(0);

        NavigationSearchFinalizationRules.TryAdmitSimplification(
                pathNodeCount: 2,
                remainingSimplificationRays: 1,
                componentCount: int.MaxValue,
                pageCount: int.MaxValue,
                Meter(exactReservation),
                out int overflowReservation)
            .Should().BeFalse();
        overflowReservation.Should().Be(0);

        NavigationSearchFinalizationRules.TryAdmitSimplification(
                pathNodeCount: 2,
                remainingSimplificationRays: 1,
                componentCount: 2,
                pageCount: 3,
                Meter(exactReservation - 1),
                out int rejectedReservation)
            .Should().BeFalse();
        rejectedReservation.Should().Be(exactReservation);

        var exactMeter = Meter(exactReservation);
        NavigationSearchFinalizationRules.TryAdmitSimplification(
                pathNodeCount: 2,
                remainingSimplificationRays: 1,
                componentCount: 2,
                pageCount: 3,
                exactMeter,
                out int admittedReservation)
            .Should().BeTrue();
        admittedReservation.Should().Be(exactReservation);
        exactMeter.RemainingLookupProbes.Should().Be(0);
    }

    [Fact]
    public void DependencyMergePreparation_ShouldBeAtomicAcrossEveryBoundary()
    {
        var source = new NavigationDependencyWorkspace(
            pageCapacity: 1,
            componentCapacity: 1);
        source.TryRecordComponent(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("map", default),
                TraversalMedium.Solid))
            .Should().BeTrue();
        source.TryRecordPage("map", pageIndex: 0).Should().BeTrue();
        var target = new NavigationDependencyWorkspace(
            pageCapacity: 1,
            componentCapacity: 1);
        NavigationSearchFinalizationRules.TryGetFinalizationLookupReservation(
                componentCount: 1,
                pageCount: 1,
                out int exactReservation)
            .Should().BeTrue();
        const int dependencyProbeCount = 2;

        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: false,
                target,
                source,
                Meter(0),
                priorLookupReservation: 0,
                out int staleReservation)
            .Should().BeFalse();
        staleReservation.Should().Be(0);

        var countShortMeter = Meter(dependencyProbeCount - 1);
        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                target,
                source,
                countShortMeter,
                priorLookupReservation: 0,
                out int countShortReservation)
            .Should().BeFalse();
        countShortReservation.Should().Be(0);
        countShortMeter.LookupProbes.Should().Be(0,
            "the missing-dependency pass debits atomically");

        var insufficientTarget = new NavigationDependencyWorkspace(
            pageCapacity: 0,
            componentCapacity: 0);
        var capacityMeter = Meter(dependencyProbeCount + exactReservation);
        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                insufficientTarget,
                source,
                capacityMeter,
                priorLookupReservation: 0,
                out int capacityReservation)
            .Should().BeFalse();
        capacityReservation.Should().Be(0);
        capacityMeter.LookupProbes.Should().Be(dependencyProbeCount);

        var floorShortMeter = Meter(
            dependencyProbeCount + exactReservation - 1);
        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                target,
                source,
                floorShortMeter,
                priorLookupReservation: 0,
                out int floorShortReservation)
            .Should().BeFalse();
        floorShortReservation.Should().Be(exactReservation);
        floorShortMeter.LookupProbes.Should().Be(dependencyProbeCount);

        var appendShortMeter = Meter(dependencyProbeCount + exactReservation);
        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                target,
                source,
                appendShortMeter,
                priorLookupReservation: 0,
                out int appendShortReservation)
            .Should().BeFalse();
        appendShortReservation.Should().Be(exactReservation);
        appendShortMeter.RemainingLookupProbes.Should().Be(exactReservation,
            "a failed append restores the prior lookup reservation floor");

        var exactMeter = Meter(
            dependencyProbeCount + exactReservation + dependencyProbeCount);
        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                target,
                source,
                exactMeter,
                priorLookupReservation: 0,
                out int preparedReservation)
            .Should().BeTrue();
        preparedReservation.Should().Be(exactReservation);
        exactMeter.LookupProbes.Should().Be(dependencyProbeCount * 2);
        target.ComponentCount.Should().Be(0,
            "preparation proves capacity and budget before the caller commits");
        target.PageCount.Should().Be(0);
    }

    [Fact]
    public void EuclideanHeuristic_ShouldRequireAnAnchorAndRepresentableDistance()
    {
        NavigationSearchFinalizationRules.TryGetEuclideanHeuristic(
                hasFootAnchor: false,
                Vector3d.Zero,
                Vector3d.One,
                out Fixed64 missingAnchor)
            .Should().BeFalse();
        missingAnchor.Should().Be(Fixed64.Zero);

        NavigationSearchFinalizationRules.TryGetEuclideanHeuristic(
                hasFootAnchor: true,
                Vector3d.Zero,
                new Vector3d((Fixed64)3, (Fixed64)4, Fixed64.Zero),
                out Fixed64 representable)
            .Should().BeTrue();
        representable.Should().Be((Fixed64)5);

        Fixed64 distantCoordinate = Fixed64.FromRaw((long.MaxValue / 2L) + 1L);
        NavigationSearchFinalizationRules.TryGetEuclideanHeuristic(
                hasFootAnchor: true,
                new Vector3d(distantCoordinate, Fixed64.Zero, Fixed64.Zero),
                new Vector3d(-distantCoordinate, Fixed64.Zero, Fixed64.Zero),
                out Fixed64 unrepresentable)
            .Should().BeFalse();
        unrepresentable.Should().Be(Fixed64.Zero);
    }

    [Theory]
    [InlineData(false, 7UL, 8UL, true)]
    [InlineData(true, 7UL, 7UL, true)]
    [InlineData(true, 7UL, 8UL, false)]
    public void EpochCurrentness_ShouldEnforceOnlyRequiredWorldProofs(
        bool epochRequired,
        ulong expectedEpoch,
        ulong currentEpoch,
        bool expectedCurrent)
    {
        NavigationSearchFinalizationRules.IsEpochCurrent(
                epochRequired,
                expectedEpoch,
                currentEpoch)
            .Should().Be(expectedCurrent);
    }

    [Theory]
    [InlineData(
        (int)NavigationEndpointResolutionStatus.Success,
        true,
        true,
        true,
        (int)NavigationEndpointResolutionStatus.Success)]
    [InlineData(
        (int)NavigationEndpointResolutionStatus.Success,
        true,
        false,
        true,
        (int)NavigationEndpointResolutionStatus.Stale)]
    [InlineData(
        (int)NavigationEndpointResolutionStatus.InvalidEndpoint,
        false,
        false,
        true,
        (int)NavigationEndpointResolutionStatus.InvalidEndpoint)]
    [InlineData(
        (int)NavigationEndpointResolutionStatus.InvalidEndpoint,
        true,
        false,
        true,
        (int)NavigationEndpointResolutionStatus.Stale)]
    [InlineData(
        (int)NavigationEndpointResolutionStatus.BudgetExceeded,
        false,
        true,
        false,
        (int)NavigationEndpointResolutionStatus.Stale)]
    public void EndpointFinalization_ShouldApplyDependencyAndEpochProofs(
        int statusValue,
        bool dependenciesWereRead,
        bool dependenciesAreCurrent,
        bool epochIsCurrent,
        int expectedStatusValue)
    {
        NavigationSearchFinalizationRules.ResolveEndpointStatus(
                (NavigationEndpointResolutionStatus)statusValue,
                dependenciesWereRead,
                dependenciesAreCurrent,
                epochIsCurrent)
            .Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData(false, (int)NavigationEndpointResolutionStatus.InvalidEndpoint)]
    [InlineData(true, (int)NavigationEndpointResolutionStatus.Success)]
    public void EndpointCursorCompletion_ShouldRequireAnAcceptedResult(
        bool hasResult,
        int expectedStatusValue)
    {
        NavigationSearchFinalizationRules.ResolveEndpointCursorStatus(hasResult)
            .Should().Be((NavigationEndpointResolutionStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Pending, false, (int)NavigationSurfaceAStarStatus.Pending)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Edge, false, (int)NavigationSurfaceAStarStatus.Pending)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Complete, false, (int)NavigationSurfaceAStarStatus.Pending)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Blocked, false, (int)NavigationSurfaceAStarStatus.Pending)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded, true, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.CapacityExceeded, true, (int)NavigationSurfaceAStarStatus.CapacityExceeded)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.CostOverflow, true, (int)NavigationSurfaceAStarStatus.CostOverflow)]
    [InlineData((int)NavigationTraversalEdgeAdvanceStatus.Stale, true, (int)NavigationSurfaceAStarStatus.Stale)]
    public void TraversalTerminalStatus_ShouldPreserveExactSearchFailure(
        int traversalStatusValue,
        bool expectedTerminal,
        int expectedStatusValue)
    {
        bool terminal = NavigationSearchFinalizationRules.TryResolveTraversalTerminalStatus(
            (NavigationTraversalEdgeAdvanceStatus)traversalStatusValue,
            out NavigationSurfaceAStarStatus status);

        terminal.Should().Be(expectedTerminal);
        status.Should().Be((NavigationSurfaceAStarStatus)expectedStatusValue);
    }

    [Fact]
    public void TraversalEpochStatus_ShouldPreserveEveryCurrentStatusAndOverrideEveryStaleOne()
    {
        NavigationTraversalEdgeAdvanceStatus[] statuses =
        {
            NavigationTraversalEdgeAdvanceStatus.Pending,
            NavigationTraversalEdgeAdvanceStatus.Edge,
            NavigationTraversalEdgeAdvanceStatus.Complete,
            NavigationTraversalEdgeAdvanceStatus.Blocked,
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
            NavigationTraversalEdgeAdvanceStatus.CostOverflow,
            NavigationTraversalEdgeAdvanceStatus.Stale
        };

        foreach (NavigationTraversalEdgeAdvanceStatus status in statuses)
        {
            NavigationSearchFinalizationRules.ResolveTraversalEpochStatus(
                    status,
                    epochIsCurrent: true)
                .Should().Be(status);
            NavigationSearchFinalizationRules.ResolveTraversalEpochStatus(
                    status,
                    epochIsCurrent: false)
                .Should().Be(NavigationTraversalEdgeAdvanceStatus.Stale);
        }
    }

    [Fact]
    public void AStarEpochStatus_ShouldPreserveEveryCurrentStatusAndOverrideEveryStaleOne()
    {
        NavigationSurfaceAStarStatus[] statuses =
        {
            NavigationSurfaceAStarStatus.Pending,
            NavigationSurfaceAStarStatus.Success,
            NavigationSurfaceAStarStatus.NoPath,
            NavigationSurfaceAStarStatus.BudgetExceeded,
            NavigationSurfaceAStarStatus.CostOverflow,
            NavigationSurfaceAStarStatus.CapacityExceeded,
            NavigationSurfaceAStarStatus.Stale
        };

        foreach (NavigationSurfaceAStarStatus status in statuses)
        {
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                    status,
                    epochIsCurrent: true)
                .Should().Be(status);
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                    status,
                    epochIsCurrent: false)
                .Should().Be(NavigationSurfaceAStarStatus.Stale);
        }
    }

    [Theory]
    [InlineData(false, 0, 0, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    [InlineData(false, 0, 1, (int)NavigationSurfaceAStarStatus.Pending)]
    [InlineData(true, 0, 1, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    [InlineData(true, 1, 0, (int)NavigationSurfaceAStarStatus.Pending)]
    public void BlockedTraversalStatus_ShouldUseTheRequiredResourceOnly(
        bool requiresConnectionProgress,
        int remainingConnectionLegs,
        int remainingEvaluatedEdges,
        int expectedStatusValue)
    {
        NavigationSearchFinalizationRules.ResolveBlockedTraversalStatus(
                requiresConnectionProgress,
                remainingConnectionLegs,
                remainingEvaluatedEdges)
            .Should().Be((NavigationSurfaceAStarStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData(0, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    [InlineData(1, (int)NavigationSurfaceAStarStatus.Pending)]
    public void IncompleteLookupStatus_ShouldDistinguishGlobalExhaustionFromLocalYield(
        int remainingLookupProbes,
        int expectedStatusValue)
    {
        NavigationSearchFinalizationRules.ResolveIncompleteLookupStatus(
                remainingLookupProbes)
            .Should().Be((NavigationSurfaceAStarStatus)expectedStatusValue);
    }

    [Fact]
    public void TraversalSurfacePoint_ShouldRemainConsumableUnlessTheEpochIsStale()
    {
        NavigationTraversalEdgeAdvanceStatus[] statuses =
        {
            NavigationTraversalEdgeAdvanceStatus.Pending,
            NavigationTraversalEdgeAdvanceStatus.Edge,
            NavigationTraversalEdgeAdvanceStatus.Complete,
            NavigationTraversalEdgeAdvanceStatus.Blocked,
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
            NavigationTraversalEdgeAdvanceStatus.CostOverflow,
            NavigationTraversalEdgeAdvanceStatus.Stale
        };

        foreach (NavigationTraversalEdgeAdvanceStatus status in statuses)
        {
            NavigationSearchFinalizationRules.ShouldConsumeTraversalSurfacePoint(
                    hasSurfacePoint: false,
                    status)
                .Should().BeFalse();
            NavigationSearchFinalizationRules.ShouldConsumeTraversalSurfacePoint(
                    hasSurfacePoint: true,
                    status)
                .Should().Be(status != NavigationTraversalEdgeAdvanceStatus.Stale);
        }
    }

    [Theory]
    [InlineData(0, 0, 0, true, 0)]
    [InlineData(11, 2, 3, true, 16)]
    [InlineData(int.MaxValue, 0, 0, true, int.MaxValue)]
    [InlineData(int.MaxValue, 1, 0, false, 0)]
    [InlineData(int.MaxValue - 1, 1, 1, false, 0)]
    public void LookupReservation_ShouldReturnExactCheckedAccounting(
        int comparisonCount,
        int componentCount,
        int pageCount,
        bool expectedSuccess,
        int expectedReservation)
    {
        NavigationSearchFinalizationRules.TryCombineLookupReservation(
                comparisonCount,
                componentCount,
                pageCount,
                out int reservation)
            .Should().Be(expectedSuccess);
        reservation.Should().Be(expectedReservation);
    }

    [Theory]
    [InlineData(0, 0, true, 0)]
    [InlineData(2, 3, true, 9)]
    [InlineData(int.MaxValue, 0, false, 0)]
    [InlineData(0, int.MaxValue, false, 0)]
    public void FinalizationLookupReservation_ShouldIncludeSortingAndRejectOverflow(
        int componentCount,
        int pageCount,
        bool expectedSuccess,
        int expectedReservation)
    {
        NavigationSearchFinalizationRules.TryGetFinalizationLookupReservation(
                componentCount,
                pageCount,
                out int reservation)
            .Should().Be(expectedSuccess);
        reservation.Should().Be(expectedReservation);
    }

    private static NavigationWorkMeter Meter(int lookupProbes) =>
        new(new NavigationWorkBudget(
            lookupProbes,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0));
}
