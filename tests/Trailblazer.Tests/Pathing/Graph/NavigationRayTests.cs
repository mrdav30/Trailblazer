using System.Linq;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationRayTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void TraceIntervalCurrentness_ShouldRequireEveryCapturedIdentityPart(
        bool identityMatches,
        bool configurationMatches,
        bool sequenceMatches,
        bool expected)
    {
        NavigationRayWork.IsTraceIntervalCurrent(
                identityMatches,
                configurationMatches,
                sequenceMatches)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationRayChainRecordState.Expanded, 2, 1, true)]
    [InlineData((int)NavigationRayChainRecordState.Expanded, 1, 1, false)]
    [InlineData((int)NavigationRayChainRecordState.Expanded, 0, 1, false)]
    [InlineData((int)NavigationRayChainRecordState.Ready, 2, 1, false)]
    public void FarthestExit_ShouldAdvanceOnlyForFartherExpandedRecords(
        int stateValue,
        int candidateExit,
        int farthestExit,
        bool expected)
    {
        NavigationRayWork.ShouldAdvanceFarthestExit(
                (NavigationRayChainRecordState)stateValue,
                (Fixed64)candidateExit,
                (Fixed64)farthestExit)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(false, 0, (int)NavigationRayChainConstraintKind.SeedAddress,
        "actual", "target", 0, false)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.SeedAddress,
        "actual", "target", 0, true)]
    [InlineData(true, 0, (int)NavigationRayChainConstraintKind.SeedAddress,
        "actual", "target", 0, true)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.FinishAddress,
        "actual", "target", 0, false)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.FinishAddress,
        "target", "target", 0, true)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.SelectedEdge,
        "target", "target", -1, false)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.SelectedEdge,
        "actual", "target", 0, false)]
    [InlineData(false, 1, (int)NavigationRayChainConstraintKind.SelectedEdge,
        "target", "target", 0, true)]
    public void FinalTarget_ShouldHonorSuffixAndExactChainConstraint(
        bool permitsDestinationSuffix,
        int intervalExit,
        int constraintKindValue,
        string actualMapId,
        string targetMapId,
        int predecessorOrdinal,
        bool expected)
    {
        NavigationRayWork.IsPermittedFinalTarget(
                permitsDestinationSuffix,
                Fixed64.Zero,
                (Fixed64)intervalExit,
                (NavigationRayChainConstraintKind)constraintKindValue,
                new NavigationCellAddress(actualMapId, default),
                new NavigationCellAddress(targetMapId, default),
                predecessorOrdinal)
            .Should().Be(expected);
    }

    [Fact]
    public void PageDependencyCurrentness_ShouldRequirePresenceAndExactVersion()
    {
        var expected = new GraphPageDependency(
            "map",
            bakeVersion: 1,
            dynamicSlotGeneration: 2,
            pageIndex: 3,
            semanticVersion: 4,
            physicalVersion: 5,
            transitionVersion: 6);
        var changed = new GraphPageDependency(
            "map",
            bakeVersion: 1,
            dynamicSlotGeneration: 2,
            pageIndex: 3,
            semanticVersion: 7,
            physicalVersion: 5,
            transitionVersion: 6);

        NavigationRayWork.IsPageDependencyCurrent(
                false,
                expected,
                default)
            .Should().BeFalse();
        NavigationRayWork.IsPageDependencyCurrent(
                true,
                expected,
                changed)
            .Should().BeFalse();
        NavigationRayWork.IsPageDependencyCurrent(
                true,
                expected,
                expected)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(0UL, 1UL, (int)GridTraceIntervalStatus.Complete, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.Stale)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.Complete, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.Pending)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.UnrepresentableGeometry, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.CostOverflow)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.GridCandidateLimitExceeded, 0, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.GridCandidateLimitExceeded, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.AddressCandidateLimitExceeded, 1, 1, 0, 1, 1, 1,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.AddressCandidateLimitExceeded, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.CandidateWorkLimitExceeded, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.OutputLimitExceeded, 1, 1, 1, 1, 0, 1,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridTraceIntervalStatus.OutputLimitExceeded, 1, 1, 1, 1, 1, 1,
        (int)NavigationRayStatus.CapacityExceeded)]
    public void TraceOutcome_ShouldPreserveEpochAndCapacityClassification(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        int traceStatusValue,
        int gridLimit,
        int mapCapacity,
        int addressLimit,
        int coveredAddressCapacity,
        int outputLimit,
        int traceIntervalCapacity,
        int expectedRayStatusValue)
    {
        NavigationRayWork.ResolveTraceStatus(
                worldSequenceBefore,
                worldSequenceAfter,
                (GridTraceIntervalStatus)traceStatusValue,
                gridLimit,
                mapCapacity,
                addressLimit,
                coveredAddressCapacity,
                outputLimit,
                traceIntervalCapacity)
            .Should().Be((NavigationRayStatus)expectedRayStatusValue);
    }

    [Theory]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Passable,
        (int)NavigationRayStatus.Pending)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Impassable,
        (int)NavigationRayStatus.Pending)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.BudgetExceeded,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.CostOverflow,
        (int)NavigationRayStatus.CostOverflow)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.CapacityExceeded,
        (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Stale,
        (int)NavigationRayStatus.Stale)]
    public void VolumeTraversalStatus_ShouldPreserveItsTerminalCause(
        int traversalStatusValue,
        int expectedRayStatusValue)
    {
        NavigationRayWork.MapVolumeStatus(
                (NavigationTraversalEvaluationStatus)traversalStatusValue)
            .Should().Be((NavigationRayStatus)expectedRayStatusValue);
    }

    [Theory]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Passable,
        false,
        (int)NavigationRayStatus.Pending,
        3)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Passable,
        true,
        (int)NavigationRayStatus.CostOverflow,
        0)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Impassable,
        false,
        (int)NavigationRayStatus.Pending,
        0)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.BudgetExceeded,
        false,
        (int)NavigationRayStatus.BudgetExceeded,
        0)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.CostOverflow,
        false,
        (int)NavigationRayStatus.CostOverflow,
        0)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.CapacityExceeded,
        false,
        (int)NavigationRayStatus.CapacityExceeded,
        0)]
    [InlineData(
        (int)NavigationTraversalEvaluationStatus.Stale,
        false,
        (int)NavigationRayStatus.Stale,
        0)]
    public void VolumeTraversalCost_ShouldPreserveDispositionAndOverflow(
        int traversalStatusValue,
        bool overflow,
        int expectedStatusValue,
        long expectedCost)
    {
        Fixed64 sourceCost = overflow ? Fixed64.MaxValue : Fixed64.One;

        NavigationRayWork.ResolveVolumeTraversalStatus(
                (NavigationTraversalEvaluationStatus)traversalStatusValue,
                sourceCost,
                (Fixed64)2,
                out Fixed64 cost)
            .Should().Be((NavigationRayStatus)expectedStatusValue);
        cost.Should().Be((Fixed64)expectedCost);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void RayInterval_ShouldContainOnlyItsClosedParameterRange(
        long parameterRaw,
        bool expected)
    {
        NavigationRayWork.ContainsParameter(
                Fixed64.Zero,
                Fixed64.FromRaw(10),
                Fixed64.FromRaw(parameterRaw))
            .Should().Be(expected);
    }

    [Fact]
    public void PortalTarget_ShouldRequireOrderedSourceParameterInsideItsInterval()
    {
        Fixed64 quarter = Fixed64.One / (Fixed64)4;
        Fixed64 half = Fixed64.Half;
        Fixed64 threeQuarters = (Fixed64)3 / (Fixed64)4;

        NavigationRayWork.ResolvePortalTargetOrdinal(
                sourceParameter: quarter,
                arrivalParameter: half,
                intervalEnter: quarter,
                intervalExit: threeQuarters,
                targetOrdinal: 7)
            .Should().Be(-1);
        NavigationRayWork.ResolvePortalTargetOrdinal(
                sourceParameter: quarter,
                arrivalParameter: Fixed64.Zero,
                intervalEnter: half,
                intervalExit: threeQuarters,
                targetOrdinal: 7)
            .Should().Be(-1);
        NavigationRayWork.ResolvePortalTargetOrdinal(
                sourceParameter: Fixed64.One,
                arrivalParameter: Fixed64.Zero,
                intervalEnter: quarter,
                intervalExit: threeQuarters,
                targetOrdinal: 7)
            .Should().Be(-1);
        NavigationRayWork.ResolvePortalTargetOrdinal(
                sourceParameter: half,
                arrivalParameter: quarter,
                intervalEnter: quarter,
                intervalExit: threeQuarters,
                targetOrdinal: 7)
            .Should().Be(7);
        NavigationRayWork.ResolvePortalTargetOrdinal(
                sourceParameter: half,
                arrivalParameter: quarter,
                intervalEnter: quarter,
                intervalExit: threeQuarters,
                targetOrdinal: -1)
            .Should().Be(-1);
    }

    [Fact]
    public void ExplicitPortalProgress_ShouldRequireParametersOrderAndIntervalContainment()
    {
        Fixed64 quarter = Fixed64.One / (Fixed64)4;
        Fixed64 half = Fixed64.Half;
        Fixed64 threeQuarters = (Fixed64)3 / (Fixed64)4;

        NavigationRayWork.IsExplicitPortalProgressValid(
                hasPortalParameters: false,
                sourceParameter: half,
                currentParameter: quarter,
                intervalEnter: quarter,
                intervalExit: threeQuarters)
            .Should().BeFalse();
        NavigationRayWork.IsExplicitPortalProgressValid(
                hasPortalParameters: true,
                sourceParameter: quarter,
                currentParameter: half,
                intervalEnter: quarter,
                intervalExit: threeQuarters)
            .Should().BeFalse();
        NavigationRayWork.IsExplicitPortalProgressValid(
                hasPortalParameters: true,
                sourceParameter: quarter,
                currentParameter: Fixed64.Zero,
                intervalEnter: half,
                intervalExit: threeQuarters)
            .Should().BeFalse();
        NavigationRayWork.IsExplicitPortalProgressValid(
                hasPortalParameters: true,
                sourceParameter: Fixed64.One,
                currentParameter: Fixed64.Zero,
                intervalEnter: quarter,
                intervalExit: threeQuarters)
            .Should().BeFalse();
        NavigationRayWork.IsExplicitPortalProgressValid(
                hasPortalParameters: true,
                sourceParameter: half,
                currentParameter: quarter,
                intervalEnter: quarter,
                intervalExit: threeQuarters)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData((int)NavigationRayChainRecordState.Unreached, false, true)]
    [InlineData((int)NavigationRayChainRecordState.Ready, true, true)]
    [InlineData((int)NavigationRayChainRecordState.Ready, false, false)]
    public void Continuation_ShouldAcceptUnreachedOrStrictlyEarlierCandidates(
        int stateValue,
        bool candidateIsEarlier,
        bool expected)
    {
        NavigationRayWork.ShouldAcceptContinuation(
                (NavigationRayChainRecordState)stateValue,
                candidateIsEarlier)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationRayChainRecordState.Unreached, true, false, true)]
    [InlineData((int)NavigationRayChainRecordState.Ready, true, true, true)]
    [InlineData((int)NavigationRayChainRecordState.Ready, true, false, false)]
    [InlineData((int)NavigationRayChainRecordState.Unreached, false, false, false)]
    public void ContinuationApplication_ShouldMutateOnlyAcceptedPassableCandidates(
        int stateValue,
        bool candidatePassable,
        bool candidateIsEarlier,
        bool expectedMutation)
    {
        var record = new NavigationRayChainRecord
        {
            PredecessorOrdinal = 1,
            RootOrdinal = 2,
            ArrivalParameter = Fixed64.One / (Fixed64)4,
            TraversalCost = (Fixed64)3,
            State = (NavigationRayChainRecordState)stateValue,
            IsSemanticCostNeutral = false
        };

        NavigationRayWork.ApplyContinuation(
            ref record,
            candidatePassable,
            candidateIsEarlier,
            predecessorOrdinal: 4,
            rootOrdinal: 5,
            arrivalParameter: Fixed64.Half,
            traversalCost: (Fixed64)6,
            incomingExplicitConnection: null!,
            isSemanticCostNeutral: true);

        record.PredecessorOrdinal.Should().Be(expectedMutation ? 4 : 1);
        record.RootOrdinal.Should().Be(expectedMutation ? 5 : 2);
        record.ArrivalParameter.Should().Be(expectedMutation
            ? Fixed64.Half
            : Fixed64.One / (Fixed64)4);
        record.TraversalCost.Should().Be(expectedMutation ? (Fixed64)6 : (Fixed64)3);
        record.State.Should().Be(expectedMutation
            ? NavigationRayChainRecordState.Ready
            : (NavigationRayChainRecordState)stateValue);
        record.IsSemanticCostNeutral.Should().Be(expectedMutation);
    }

    [Theory]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.Pending,
        (int)NavigationRayStatus.Pending)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.Edge,
        (int)NavigationRayStatus.Pending)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.Complete,
        (int)NavigationRayStatus.Pending)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.Blocked,
        (int)NavigationRayStatus.Blocked)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
        (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.CostOverflow,
        (int)NavigationRayStatus.CostOverflow)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
        (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(
        (int)NavigationTraversalEdgeAdvanceStatus.Stale,
        (int)NavigationRayStatus.Stale)]
    public void VolumeEdgeAdvanceStatus_ShouldPreserveItsTerminalCause(
        int traversalStatusValue,
        int expectedRayStatusValue)
    {
        NavigationRayWork.MapTraversalAdvanceStatus(
                (NavigationTraversalEdgeAdvanceStatus)traversalStatusValue)
            .Should().Be((NavigationRayStatus)expectedRayStatusValue);
    }

    [Fact]
    public void WorkMeter_ShouldDebitEachRayCategoryExactly()
    {
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: 0,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 2,
            maxCoveredVoxelIntervals: 3,
            maxSimplificationRays: 1));

        meter.TryConsumeTraceIntervals(2).Should().BeTrue();
        meter.TryConsumeTraceIntervals(1).Should().BeFalse();
        meter.TraceIntervals.Should().Be(2);
        meter.TryConsumeCoveredVoxelIntervals(3).Should().BeTrue();
        meter.TryConsumeCoveredVoxelIntervals(1).Should().BeFalse();
        meter.CoveredVoxelIntervals.Should().Be(3);
        meter.TryConsumeSimplificationRays(1).Should().BeTrue();
        meter.TryConsumeSimplificationRays(1).Should().BeFalse();
        meter.SimplificationRays.Should().Be(1);

        meter.Reset(new NavigationWorkBudget(0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1));
        meter.TraceIntervals.Should().Be(0);
        meter.CoveredVoxelIntervals.Should().Be(0);
        meter.SimplificationRays.Should().Be(0);
    }

    [Fact]
    public void Workspace_ShouldDeriveEveryBufferFromExplicitCeilings()
    {
        var workspace = new NavigationRayWorkspace(
            mapCapacity: 2,
            pageCapacity: 3,
            componentCapacity: 5,
            coveredAddressCapacity: 13,
            traceIntervalCapacity: 11);

        workspace.TraceIntervals.Capacity.Should().BeGreaterThanOrEqualTo(11);
        workspace.TraceIntervalCapacity.Should().Be(11);
        workspace.ChainRecords.Should().HaveCount(11);
        workspace.Dependencies.Pages.Should().HaveCount(3);
        workspace.Dependencies.Components.Should().HaveCount(5);
        workspace.CoveredAddressCapacity.Should().Be(13);
        workspace.MapCapacity.Should().Be(2);
    }

    [Fact]
    public void MappedIntervalLookup_ShouldFindARealTargetOrStopBeforeTheFirstInterval()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 16);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));
        NavigationCellAddress endAddress = fixture.Near.Nodes[0].Address;
        fixture.Graph.TryGetNodeRef(endAddress, out NavigationNodeRef endNode)
            .Should().BeTrue();
        work.Begin(CreateLineRequest(fixture));
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        int expectedOrdinal = -1;
        for (int ordinal = 0; ordinal < workspace.TraceIntervals.Count; ordinal++)
        {
            if (workspace.ChainRecords[ordinal].Node.Equals(endNode))
            {
                expectedOrdinal = ordinal;
                break;
            }
        }
        expectedOrdinal.Should().BeGreaterThanOrEqualTo(0);

        NavigationRayWork.FindMapped(
                workspace,
                endNode,
                workspace.TraceIntervals[expectedOrdinal].TEnter)
            .Should().Be(expectedOrdinal);
        NavigationRayWork.FindMapped(workspace, endNode, -Fixed64.One)
            .Should().Be(-1,
                "normalized ray intervals begin at zero and the ordered scan can stop immediately");
    }

    [Fact]
    public void ExplicitFirstLeg_ShouldRequireEntryAndPriorExitInRayOrder()
    {
        var leg = new FixedSegment(Vector3d.Zero, Vector3d.Right * (Fixed64)4);
        Vector3d entry = Vector3d.Right * (Fixed64)2;
        NavigationExplicitConnectionRecord orderedPrior =
            CreateExplicitRecord(Vector3d.Right);
        NavigationExplicitConnectionRecord laterPrior =
            CreateExplicitRecord(Vector3d.Right * (Fixed64)3);
        NavigationExplicitConnectionRecord offLegPrior =
            CreateExplicitRecord(Vector3d.Right * (Fixed64)5);
        Vector3d rayEnd = Vector3d.Right * (Fixed64)6;

        NavigationRayWork.IsExplicitFirstLegValid(
                leg,
                Vector3d.Right * (Fixed64)5,
                null!,
                Vector3d.Zero,
                rayEnd)
            .Should().BeFalse("the current connection entry must lie on its first leg");
        NavigationRayWork.IsExplicitFirstLegValid(
                leg,
                entry,
                null!,
                Vector3d.Zero,
                rayEnd)
            .Should().BeTrue("an initial explicit connection has no prior exit to order");
        NavigationRayWork.IsExplicitFirstLegValid(
                leg,
                entry,
                offLegPrior,
                Vector3d.Zero,
                rayEnd)
            .Should().BeFalse("a prior exit outside the first leg cannot be skipped");
        NavigationRayWork.IsExplicitFirstLegValid(
                leg,
                entry,
                laterPrior,
                Vector3d.Zero,
                rayEnd)
            .Should().BeFalse("a chained connection may not move backward along the ray");
        NavigationRayWork.IsExplicitFirstLegValid(
                leg,
                entry,
                orderedPrior,
                Vector3d.Zero,
                rayEnd)
            .Should().BeTrue();
    }

    [Fact]
    public void OrderedRay_ShouldFollowTheExactGraphChainAndCost()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));

        work.Advance(meter).Should().Be(NavigationRayStatus.Pending);
        meter.LookupProbes.Should().Be(0,
            "an unbound ray must not consume deterministic work");
        work.Begin(request);

        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        work.Result.StartAddress.Should().Be(fixture.FarOrigin);
        work.Result.EndAddress.Should().Be(fixture.Near.Nodes[0].Address);
        work.Result.TraversalCost.Should().Be((Fixed64)3);
        work.Result.IsSemanticCostNeutral.Should().BeTrue();
        meter.LookupProbes.Should().Be(5);
        meter.CoveredVoxelIntervals.Should().Be(10);
        meter.EvaluatedEdges.Should().Be(6);
        meter.TraceIntervals.Should().Be(4);
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        meter.LookupProbes.Should().Be(5);
        meter.CoveredVoxelIntervals.Should().Be(10);
        meter.EvaluatedEdges.Should().Be(6);
        meter.TraceIntervals.Should().Be(4,
            "a terminal ray must not consume deterministic work twice");
    }

    [Fact]
    public void OrderedRay_UnrepresentableCandidatePrism_ShouldReportCostOverflow()
    {
        using var world = new GridForge.Grids.GridWorld();
        Vector3d extremePosition = Vector3d.Zero;
        world.TryAddGrid(
                new GridConfiguration(
                    extremePosition,
                    extremePosition,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.MinIncrement,
                        Fixed64.One,
                        Fixed64.One)),
                out _)
            .Should().BeTrue();
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(1),
                new[] { default(VoxelIndex) },
                "ray-unrepresentable-candidate");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            NavigationAStarExitTestHarness.Profile(),
            extremePosition,
            extremePosition));

        result.Status.Should().Be(NavigationRayStatus.CostOverflow);
    }

    [Theory]
    [InlineData((int)TraversalMedium.Gas)]
    [InlineData((int)TraversalMedium.Liquid)]
    public void OrderedVolumeRay_ShouldCertifyTheUnifiedMediumChainAndDependencies(
        int mediumValue)
    {
        TraversalMedium medium = (TraversalMedium)mediumValue;
        TraversalMedia media = NavigationCell.ToMedia(medium);
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        var volumeCell = new NavigationCell(
            media,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                $"ray-{medium}",
                new[] { volumeCell, volumeCell });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            media,
            TraversalCapability.None);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var destinationAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceNode, medium, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                destinationNode,
                medium,
                out NavigationNodeState destinationState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceAnchor)
            .Should().BeTrue();
        destinationState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d destinationAnchor)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 4, 4, 64, 64);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(64, 64));
        var request = new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            medium,
            sourceAnchor,
            destinationAnchor,
            NavigationRayEndpointAllowance.None);
        work.Begin(request);

        work.Advance(meter).Should().Be(NavigationRayStatus.Success);

        work.Result.StartAddress.Should().Be(sourceAddress);
        work.Result.EndAddress.Should().Be(destinationAddress);
        work.Result.TraversalCost.Should().Be(Fixed64.One);
        work.Result.IsSemanticCostNeutral.Should().BeTrue();
        workspace.Dependencies.PageCount.Should().Be(1);
        workspace.Dependencies.ComponentCount.Should().Be(1);

        var constrained = new NavigationRayWork(
            new NavigationRayWorkspace(1, 0, 4, 64, 64));
        constrained.Begin(request);
        var constrainedMeter = new NavigationWorkMeter(CreateRayBudget(64, 64));
        constrained.Advance(constrainedMeter)
            .Should().Be(NavigationRayStatus.CapacityExceeded);
        constrained.Result.Status.Should().Be(NavigationRayStatus.CapacityExceeded);
    }

    [Fact]
    public void OrderedVolumeRay_ShouldPropagateSweptUnionBudgetAndCapacity()
    {
        using var world = new GridForge.Grids.GridWorld();
        var cells = new VoxelIndex[30];
        int cellCount = 0;
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 5; z++)
                cells[cellCount++] = new VoxelIndex(x, 0, z);
        }
        var volumeCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)6, (Fixed64)2, (Fixed64)5),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.One,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                cells,
                "ray-volume-union-boundary",
                Enumerable.Repeat(volumeCell, cells.Length).ToArray());
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                (Fixed64)3 / (Fixed64)2,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var sourceAddress = new NavigationCellAddress(
            fixture.MapId,
            new VoxelIndex(2, 0, 2));
        var targetAddress = new NavigationCellAddress(
            fixture.MapId,
            new VoxelIndex(3, 0, 2));
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef targetNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                targetNode,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceFoot)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d targetFoot)
            .Should().BeTrue();
        var request = new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            sourceFoot,
            targetFoot,
            NavigationRayEndpointAllowance.None);
        var measuringWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var measuringWork = new NavigationRayWork(measuringWorkspace);
        var measuringMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        measuringWork.Begin(request);

        AdvanceToTerminal(measuringWork, measuringMeter)
            .Should().Be(NavigationRayStatus.Success);
        int exactCoveredAddresses = measuringMeter.CoveredVoxelIntervals;
        int exactUnionCapacity = measuringWorkspace.BodyTraceCells.Count;
        measuringMeter.VolumeUnionChecks.Should().BeGreaterThan(1,
            "both edge admission and the actual ray segment require a swept body-union proof");
        exactUnionCapacity.Should().BeGreaterThan(2,
            "the wide body covers more cells than the centerline ray");

        var seedRequest = new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            sourceFoot,
            sourceFoot,
            NavigationRayEndpointAllowance.None);
        var seedMeasuringWorkspace = new NavigationRayWorkspace(
            8,
            64,
            64,
            128,
            128);
        var seedMeasuringWork = new NavigationRayWork(seedMeasuringWorkspace);
        var seedMeasuringMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        seedMeasuringWork.Begin(seedRequest);
        AdvanceToTerminal(seedMeasuringWork, seedMeasuringMeter)
            .Should().Be(NavigationRayStatus.Success);
        int exactSeedCapacity = seedMeasuringWorkspace.BodyTraceCells.Count;
        exactSeedCapacity.Should().BeGreaterThan(1);
        seedMeasuringMeter.TraceIntervals.Should().BeLessThan(exactSeedCapacity,
            "the one-below body capacity must still fit the ordinary ray trace");
        seedMeasuringMeter.VolumeUnionChecks.Should().Be(2,
            "the same wide-body union certifies both the seed and final placement");
        var seedUnionWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var seedUnionMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        var seedUnionEvaluator = new NavigationVolumeEdgeEvaluator(
            world,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            seedUnionWorkspace);
        var sourceStateRef = new NavigationMediumStateRef(
            sourceNode,
            TraversalMedium.Gas);
        seedUnionEvaluator.CertifyRaySegment(
                sourceStateRef,
                sourceStateRef,
                sourceFoot,
                sourceFoot,
                seedUnionMeter,
                seedUnionWorkspace.Dependencies)
            .Should().Be(NavigationTraversalEvaluationStatus.Passable);
        int oneSeedUnionCovered = seedUnionMeter.CoveredVoxelIntervals;

        var seedBudgetWork = new NavigationRayWork(
            new NavigationRayWorkspace(8, 64, 64, 128, 128));
        var seedBudgetMeter = new NavigationWorkMeter(CreateRayBudget(
            seedMeasuringMeter.TraceIntervals,
            seedMeasuringMeter.CoveredVoxelIntervals - oneSeedUnionCovered - 1,
            seedMeasuringMeter.ConnectionLegs,
            seedMeasuringMeter.LookupProbes,
            seedMeasuringMeter.EvaluatedEdges));
        seedBudgetWork.Begin(seedRequest);
        AdvanceToTerminal(seedBudgetWork, seedBudgetMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "the initial volume-body union is part of the exact ray budget");

        var seedCapacityWork = new NavigationRayWork(
            new NavigationRayWorkspace(
                8,
                64,
                64,
                exactSeedCapacity - 1,
                exactSeedCapacity - 1));
        var seedCapacityMeter = new NavigationWorkMeter(CreateRayBudget(
            seedMeasuringMeter.TraceIntervals,
            seedMeasuringMeter.CoveredVoxelIntervals,
            seedMeasuringMeter.ConnectionLegs,
            seedMeasuringMeter.LookupProbes,
            seedMeasuringMeter.EvaluatedEdges));
        seedCapacityWork.Begin(seedRequest);
        AdvanceToTerminal(seedCapacityWork, seedCapacityMeter)
            .Should().Be(NavigationRayStatus.CapacityExceeded,
                "the initial volume-body union must not truncate its covered cells");

        var budgetWork = new NavigationRayWork(
            new NavigationRayWorkspace(8, 64, 64, 128, 128));
        var budgetMeter = new NavigationWorkMeter(CreateRayBudget(
            measuringMeter.TraceIntervals,
            exactCoveredAddresses - 1,
            measuringMeter.ConnectionLegs,
            measuringMeter.LookupProbes,
            measuringMeter.EvaluatedEdges));
        budgetWork.Begin(request);
        AdvanceToTerminal(budgetWork, budgetMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded);

        var capacityWork = new NavigationRayWork(
            new NavigationRayWorkspace(
                8,
                64,
                64,
                exactUnionCapacity - 1,
                exactUnionCapacity - 1));
        var capacityMeter = new NavigationWorkMeter(CreateRayBudget(
            measuringMeter.TraceIntervals,
            exactCoveredAddresses,
            measuringMeter.ConnectionLegs,
            measuringMeter.LookupProbes,
            measuringMeter.EvaluatedEdges));
        capacityWork.Begin(request);
        AdvanceToTerminal(capacityWork, capacityMeter)
            .Should().Be(NavigationRayStatus.CapacityExceeded);

        var targetStateRef = new NavigationMediumStateRef(
            targetNode,
            TraversalMedium.Gas);
        var segmentProfile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        Vector3d offsetSource = sourceFoot
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)8);
        Vector3d offsetTarget = targetFoot
            - Vector3d.Forward * ((Fixed64)3 / (Fixed64)8);
        var segmentWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var segmentMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        var segmentEvaluator = new NavigationVolumeEdgeEvaluator(
            world,
            fixture.Graph,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            segmentWorkspace);
        segmentEvaluator.CertifyRaySegment(
                sourceStateRef,
                targetStateRef,
                offsetSource,
                offsetTarget,
                segmentMeter,
                segmentWorkspace.Dependencies)
            .Should().Be(NavigationTraversalEvaluationStatus.Passable);
        int exactOffsetCapacity = segmentWorkspace.BodyTraceCells.Count;
        exactOffsetCapacity.Should().BeGreaterThan(2,
            "the opposing legal offsets sweep cells outside the centered edge portals");
        var offsetSeedWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var offsetSeedMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        var offsetSeedEvaluator = new NavigationVolumeEdgeEvaluator(
            world,
            fixture.Graph,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            offsetSeedWorkspace);
        offsetSeedEvaluator.CertifyRaySegment(
                sourceStateRef,
                sourceStateRef,
                offsetSource,
                offsetSource,
                offsetSeedMeter,
                offsetSeedWorkspace.Dependencies)
            .Should().Be(NavigationTraversalEvaluationStatus.Passable);
        offsetSeedWorkspace.BodyTraceCells.Count.Should().BeLessThan(
            exactOffsetCapacity,
            "the cross-cell sweep must be the first capacity boundary after seeding");
        fixture.Graph.TryGetSeamPrism(
                sourceAddress,
                out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(
                targetAddress,
                out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal offsetPortal)
            .Should().BeTrue();
        GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                targetPrism,
                offsetPortal,
                offsetSource,
                offsetTarget,
                segmentProfile.Shape.Radius,
                segmentProfile.Shape.Height,
                out _,
                out Fixed64 offsetTargetParameter)
            .Should().BeTrue();
        Vector3d finalSegmentStart = Vector3d.Lerp(
            offsetSource,
            offsetTarget,
            offsetTargetParameter);
        var finalSegmentWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var finalSegmentMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        var finalSegmentEvaluator = new NavigationVolumeEdgeEvaluator(
            world,
            fixture.Graph,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            finalSegmentWorkspace);
        finalSegmentEvaluator.CertifyRaySegment(
                targetStateRef,
                targetStateRef,
                finalSegmentStart,
                offsetTarget,
                finalSegmentMeter,
                finalSegmentWorkspace.Dependencies)
            .Should().Be(NavigationTraversalEvaluationStatus.Passable);

        var ordinalWorkspace = new NavigationRayWorkspace(8, 64, 64, 128, 128);
        var edgeEnumerator = new NavigationTraversalEdgeEnumerator(
            world,
            fixture.Graph,
            sourceStateRef,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            ordinalWorkspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var ordinalMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        int edgeSteps = int.MaxValue;
        int connectionSteps = int.MaxValue;
        int selectedOrdinal = -1;
        while (selectedOrdinal < 0)
        {
            NavigationTraversalEdgeAdvanceStatus edgeStatus = edgeEnumerator.AdvanceOne(
                ordinalMeter,
                ordinalWorkspace.Dependencies,
                ref edgeSteps,
                ref connectionSteps);
            edgeStatus.Should().NotBe(NavigationTraversalEdgeAdvanceStatus.Complete);
            if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Edge
                && edgeEnumerator.CurrentTarget.Equals(targetStateRef))
            {
                selectedOrdinal = edgeEnumerator.CurrentOrdinal;
            }
        }
        var offsetRequest = new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            offsetSource,
            offsetTarget,
            NavigationRayEndpointAllowance.None,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                selectedOrdinal));
        var offsetMeasuringWorkspace = new NavigationRayWorkspace(
            8,
            64,
            64,
            128,
            128);
        var offsetMeasuringWork = new NavigationRayWork(offsetMeasuringWorkspace);
        var offsetMeasuringMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        offsetMeasuringWork.Begin(offsetRequest);
        AdvanceToTerminal(offsetMeasuringWork, offsetMeasuringMeter)
            .Should().Be(NavigationRayStatus.Success);
        offsetMeasuringWorkspace.TraceIntervals.Count.Should().BeLessThan(
            exactOffsetCapacity,
            "the centerline trace must fit before the segment union reaches capacity");

        var offsetBudgetWork = new NavigationRayWork(
            new NavigationRayWorkspace(8, 64, 64, 128, 128));
        var offsetBudgetMeter = new NavigationWorkMeter(CreateRayBudget(
            offsetMeasuringMeter.TraceIntervals,
            offsetMeasuringMeter.CoveredVoxelIntervals
                - finalSegmentMeter.CoveredVoxelIntervals
                - 1,
            offsetMeasuringMeter.ConnectionLegs,
            offsetMeasuringMeter.LookupProbes,
            offsetMeasuringMeter.EvaluatedEdges));
        offsetBudgetWork.Begin(offsetRequest);
        AdvanceToTerminal(offsetBudgetWork, offsetBudgetMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded);

        var offsetCapacityWork = new NavigationRayWork(
            new NavigationRayWorkspace(
                8,
                64,
                64,
                exactOffsetCapacity - 1,
                exactOffsetCapacity - 1));
        var offsetCapacityMeter = new NavigationWorkMeter(CreateRayBudget(
            offsetMeasuringMeter.TraceIntervals,
            offsetMeasuringMeter.CoveredVoxelIntervals,
            offsetMeasuringMeter.ConnectionLegs,
            offsetMeasuringMeter.LookupProbes,
            offsetMeasuringMeter.EvaluatedEdges));
        offsetCapacityWork.Begin(offsetRequest);
        AdvanceToTerminal(offsetCapacityWork, offsetCapacityMeter)
            .Should().Be(NavigationRayStatus.CapacityExceeded);

        var suffixRequest = new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            segmentProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            offsetSource,
            offsetTarget,
            NavigationRayEndpointAllowance.DestinationSuffix,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                selectedOrdinal));
        var suffixMeasuringWork = new NavigationRayWork(
            new NavigationRayWorkspace(8, 64, 64, 128, 128));
        var suffixMeasuringMeter = new NavigationWorkMeter(CreateRayBudget(
            4_096,
            4_096,
            connectionLegs: 4_096,
            lookupProbes: 4_096,
            evaluatedEdges: 4_096));
        suffixMeasuringWork.Begin(suffixRequest);
        AdvanceToTerminal(suffixMeasuringWork, suffixMeasuringMeter)
            .Should().Be(NavigationRayStatus.Success);

        var suffixBudgetWork = new NavigationRayWork(
            new NavigationRayWorkspace(8, 64, 64, 128, 128));
        var suffixBudgetMeter = new NavigationWorkMeter(CreateRayBudget(
            suffixMeasuringMeter.TraceIntervals,
            suffixMeasuringMeter.CoveredVoxelIntervals - 1,
            suffixMeasuringMeter.ConnectionLegs,
            suffixMeasuringMeter.LookupProbes,
            suffixMeasuringMeter.EvaluatedEdges));
        suffixBudgetWork.Begin(suffixRequest);
        AdvanceToTerminal(suffixBudgetWork, suffixBudgetMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "the destination suffix must propagate its final swept-union budget");

        static NavigationRayStatus AdvanceToTerminal(
            NavigationRayWork work,
            NavigationWorkMeter meter)
        {
            NavigationRayStatus status;
            do
            {
                status = work.Advance(meter);
            }
            while (status == NavigationRayStatus.Pending);
            return status;
        }
    }

    [Fact]
    public void OrderedVolumeRay_ShouldReportCheckedEnterCostOverflow()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        var sourceCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var targetCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.MaxValue,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-volume-overflow",
                new[] { sourceCell, targetCell });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        NavigationCellAddress sourceAddress = new(fixture.MapId, source);
        NavigationCellAddress destinationAddress = new(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                destinationNode,
                TraversalMedium.Gas,
                out NavigationNodeState destinationState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceAnchor)
            .Should().BeTrue();
        destinationState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d destinationAnchor)
            .Should().BeTrue();
        var work = new NavigationRayWork(
            new NavigationRayWorkspace(1, 4, 4, 64, 64));
        var meter = new NavigationWorkMeter(CreateRayBudget(64, 64));
        work.Begin(new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            sourceAnchor,
            destinationAnchor,
            NavigationRayEndpointAllowance.None));

        work.Advance(meter).Should().Be(NavigationRayStatus.CostOverflow);
        work.Result.Status.Should().Be(NavigationRayStatus.CostOverflow);
        meter.EvaluatedEdges.Should().BePositive();
    }

    [Fact]
    public void OrderedVolumeRay_SeedFootprintBeyondWorkspace_ShouldReportCapacity()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex[] cells =
        {
            default,
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
            new(4, 0, 0)
        };
        var volumeCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "ray-volume-seed-capacity",
                new[] { volumeCell, volumeCell, volumeCell, volumeCell, volumeCell });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape((Fixed64)2, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, cells[2]);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceFoot)
            .Should().BeTrue();
        var work = new NavigationRayWork(
            new NavigationRayWorkspace(1, 8, 8, 1, 1));
        var meter = new NavigationWorkMeter(CreateRayBudget(1, 64));
        work.Begin(new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            sourceFoot,
            sourceFoot,
            NavigationRayEndpointAllowance.None));

        work.Advance(meter).Should().Be(NavigationRayStatus.CapacityExceeded);
        work.Result.Status.Should().Be(NavigationRayStatus.CapacityExceeded);
    }

    [Fact]
    public void OrderedRay_ShouldHonorEveryQueryMeterAllowanceBelowTheExactBoundary()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        int[] exact = { 5, 10, 6 };

        for (int category = 0; category < exact.Length; category++)
        {
            for (int allowance = 0; allowance < exact[category]; allowance++)
            {
                int[] limits = (int[])exact.Clone();
                limits[category] = allowance;
                NavigationRayResult blocked = RunRay(
                    CreateLineRequest(fixture),
                    CreateRayBudget(
                        traceCapacity: 4,
                        coveredCapacity: limits[1],
                        lookupProbes: limits[0],
                        evaluatedEdges: limits[2]));

                blocked.Status.Should().Be(
                    NavigationRayStatus.BudgetExceeded,
                    "query meter category {0} with allowance {1} is below exact",
                    category,
                    allowance);
            }
        }

        RunRay(
                CreateLineRequest(fixture),
                CreateRayBudget(
                    traceCapacity: 4,
                    coveredCapacity: exact[1],
                    lookupProbes: exact[0],
                    evaluatedEdges: exact[2]))
            .Status.Should().Be(NavigationRayStatus.Success);
    }

    [Theory]
    [InlineData(0, (int)NavigationRayStatus.Success)]
    [InlineData(1, (int)NavigationRayStatus.Success)]
    [InlineData(2, (int)NavigationRayStatus.CostOverflow)]
    public void OrderedRay_ShouldReportCellAndAreaSurchargesWithoutWrapping(
        int surchargeKind,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationAreaId targetArea = surchargeKind == 1
            ? new NavigationAreaId(1)
            : default;
        Fixed64 enterCost = surchargeKind == 0
            ? Fixed64.Half
            : surchargeKind == 2 ? Fixed64.MaxValue : Fixed64.Zero;
        var targetCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            targetArea,
            enterCost,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAreaRule[] rules = surchargeKind == 1
            ? new[]
            {
                new NavigationAreaRule(true, Fixed64.Zero),
                new NavigationAreaRule(true, Fixed64.Half)
            }
            : new[] { new NavigationAreaRule(true, Fixed64.Zero) };
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("ray-cost", 1),
            rules);
        var destination = new VoxelIndex(1, 0, 0);
        var prepared = new PreparedNavigationMap(
            new NavigationMapBuilder("ray-cost", binding)
                .AddCell(default, NavigationAStarExitTestHarness.Cell)
                .AddCell(destination, targetCell)
                .Build(),
            1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationAreaCatalog.Empty.TryPublish(
                policy,
                maxPolicies: 1,
                requiredRuleCount: rules.Length,
                maxRulesPerPolicy: rules.Length,
                maxRules: rules.Length,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph = new NavigationWorldGraph(
            1,
            new[] { instance },
            areaCatalog: catalog);
        graph = graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var request = new NavigationRayRequest(
            world,
            store,
            graph,
            NavigationAStarExitTestHarness.Profile(),
            policy,
            TraversalMedium.Solid,
            NavigationAStarExitTestHarness.GetFoot(binding, default),
            NavigationAStarExitTestHarness.GetFoot(binding, destination),
            NavigationRayEndpointAllowance.None);

        NavigationRayResult result = RunRay(request);

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if (result.Status == NavigationRayStatus.Success)
        {
            result.TraversalCost.Should().Be(Fixed64.One + Fixed64.Half);
            result.IsSemanticCostNeutral.Should().BeFalse();
        }
    }

    [Fact]
    public void OrderedRay_CumulativeSurfaceCostOverflow_ShouldFailClosed()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex[] cells =
        {
            default,
            new(1, 0, 0),
            new(2, 0, 0)
        };
        Fixed64 halfMaximum = Fixed64.MaxValue / (Fixed64)2;
        NavigationCell[] authored =
        {
            NavigationAStarExitTestHarness.Cell,
            new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                halfMaximum,
                (Fixed64)4,
                (Fixed64)4),
            new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                halfMaximum,
                (Fixed64)4,
                (Fixed64)4)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "ray-cumulative-overflow",
                authored);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[0]),
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[^1])));

        result.Status.Should().Be(NavigationRayStatus.CostOverflow,
            "individually representable edge costs must not wrap when accumulated");
    }

    [Fact]
    public void OrderedRay_OrdinarySurfaceEdgeMissingCapability_ShouldBeBlocked()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        var climbCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.Climb,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-ordinary-capability",
                new[] { NavigationAStarExitTestHarness.Cell, climbCell });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, destination)));

        result.Status.Should().Be(NavigationRayStatus.Blocked,
            "ordinary surface edges must enforce the target cell's required capabilities");
    }

    [Fact]
    public void OrderedVolumeRay_CumulativeCostOverflow_ShouldFailClosed()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex[] cells =
        {
            default,
            new(1, 0, 0),
            new(2, 0, 0)
        };
        Fixed64 halfMaximum = Fixed64.MaxValue / (Fixed64)2;
        NavigationCell[] authored =
        {
            new(
                TraversalMedia.Gas,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                (Fixed64)4,
                (Fixed64)4),
            new(
                TraversalMedia.Gas,
                TraversalCapability.None,
                default,
                halfMaximum,
                (Fixed64)4,
                (Fixed64)4),
            new(
                TraversalMedia.Gas,
                TraversalCapability.None,
                default,
                halfMaximum,
                (Fixed64)4,
                (Fixed64)4)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "ray-volume-cumulative-overflow",
                authored);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        fixture.Graph.TryGetNodeRef(
                new NavigationCellAddress(fixture.MapId, cells[0]),
                out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                new NavigationCellAddress(fixture.MapId, cells[^1]),
                out NavigationNodeRef targetNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(
                targetNode,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d sourceFoot)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(
                profile.Shape.Height,
                out Vector3d targetFoot)
            .Should().BeTrue();

        NavigationRayResult result = RunRay(new NavigationRayRequest(
            world,
            store,
            fixture.Graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Gas,
            sourceFoot,
            targetFoot,
            NavigationRayEndpointAllowance.None));

        result.Status.Should().Be(NavigationRayStatus.CostOverflow,
            "individually representable volume edge costs must not wrap when accumulated");
    }

    [Theory]
    [InlineData(2, 16, (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(16, 2, (int)NavigationRayStatus.BudgetExceeded)]
    public void OrderedRay_ShouldDistinguishWorkspaceAndQueryTraceCeilings(
        int workspaceTraceCapacity,
        int budgetTraceCapacity,
        int expectedStatus)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var work = new NavigationRayWork(new NavigationRayWorkspace(
            1,
            8,
            8,
            16,
            workspaceTraceCapacity));
        var meter = new NavigationWorkMeter(CreateRayBudget(
            budgetTraceCapacity,
            coveredCapacity: 16));

        work.Begin(CreateLineRequest(fixture));

        NavigationRayStatus expected = (NavigationRayStatus)expectedStatus;
        work.Advance(meter).Should().Be(expected);
        work.Result.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 16, (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(4, 1, (int)NavigationRayStatus.BudgetExceeded)]
    public void OrderedRay_ShouldDistinguishWorkspaceAndQueryGridCandidateCeilings(
        int workspaceMapCapacity,
        int lookupBudget,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        GridConfiguration configuration = NavigationAStarExitTestHarness.RectangularLine(2);
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { source, destination },
                "ray-grid-ceiling");
        GridConfiguration overlapping = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)2, (Fixed64)2, (Fixed64)2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(overlapping, out _).Should().BeTrue(
            "an overlapping host grid is a legitimate broad-phase candidate even when unmapped");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var work = new NavigationRayWork(new NavigationRayWorkspace(
            workspaceMapCapacity,
            8,
            8,
            16,
            16));
        var meter = new NavigationWorkMeter(CreateRayBudget(
            traceCapacity: 16,
            coveredCapacity: 16,
            lookupProbes: lookupBudget));
        work.Begin(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, destination)));

        work.Advance(meter).Should().Be((NavigationRayStatus)expectedStatus);
        work.Result.Status.Should().Be((NavigationRayStatus)expectedStatus);
    }

    [Fact]
    public void OrderedRay_ShouldHonorSourceOnlyAndExactSelectedEdgeConstraints()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationWorldGraph graph = fixture.Graph;
        graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        var selectedAddress = new NavigationCellAddress(
            fixture.FarOrigin.MapId,
            new VoxelIndex(2, 0, 0));
        graph.TryGetNodeRef(selectedAddress, out NavigationNodeRef selectedNode)
            .Should().BeTrue();
        graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        graph.TryGetNodeState(selectedNode, out NavigationNodeState selectedState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        edges.MoveNext().Should().BeTrue();
        graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress firstTarget)
            .Should().BeTrue();
        firstTarget.Should().Be(selectedAddress);
        int selectedOrdinal = edges.CurrentOrdinal;

        RunRay(CreateRequest(
                fixture.World,
                fixture.Store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceState.FootAnchor,
                sourceState.FootAnchor,
                NavigationRayChainConstraint.SelectedEdge(
                    fixture.FarOrigin,
                    selectedAddress,
                    selectedOrdinal)))
            .Status.Should().Be(NavigationRayStatus.Blocked,
                "a selected-edge constraint must actually traverse its exact edge");

        RunRay(CreateRequest(
                fixture.World,
                fixture.Store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceState.FootAnchor,
                selectedState.FootAnchor,
                NavigationRayChainConstraint.SourceOnly(fixture.FarOrigin)))
            .Status.Should().Be(NavigationRayStatus.Blocked);

        NavigationRayResult selected = RunRay(CreateRequest(
            fixture.World,
            fixture.Store,
            graph,
            NavigationAStarExitTestHarness.Profile(),
            sourceState.FootAnchor,
            selectedState.FootAnchor,
            NavigationRayChainConstraint.SelectedEdge(
                fixture.FarOrigin,
                selectedAddress,
                selectedOrdinal)));
        selected.Status.Should().Be(NavigationRayStatus.Success);
        selected.EndAddress.Should().Be(selectedAddress);

        RunRay(CreateRequest(
                fixture.World,
                fixture.Store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceState.FootAnchor,
                selectedState.FootAnchor,
                NavigationRayChainConstraint.SelectedEdge(
                    fixture.FarOrigin,
                    selectedAddress,
                    selectedOrdinal + 1)))
            .Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OrderedRay_StartPrefixShouldRejectEarlierMappedCellOutsideRequiredChain(
        bool selectedEdge,
        bool weightedPrefix)
    {
        using var world = new GridForge.Grids.GridWorld();
        var prefix = new VoxelIndex(0, 0, 0);
        var source = new VoxelIndex(1, 0, 0);
        var target = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { prefix, source, target },
                "ray-start-prefix-chain",
                new[]
                {
                    weightedPrefix
                        ? NavigationAStarExitTestHarness.ExpensiveCell
                        : NavigationAStarExitTestHarness.Cell,
                    NavigationAStarExitTestHarness.Cell,
                    NavigationAStarExitTestHarness.Cell
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var targetAddress = new NavigationCellAddress(fixture.MapId, target);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges =
            fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        int selectedOrdinal = -1;
        while (edges.MoveNext())
        {
            if (fixture.Graph.TryGetNodeAddress(
                    edges.Current.Target,
                    out NavigationCellAddress address)
                && address == targetAddress)
            {
                selectedOrdinal = edges.CurrentOrdinal;
                break;
            }
        }
        selectedOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationRayChainConstraint constraint = selectedEdge
            ? NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                selectedOrdinal)
            : NavigationRayChainConstraint.SourceOnly(sourceAddress);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, prefix),
            NavigationAStarExitTestHarness.GetFoot(
                fixture.Binding,
                selectedEdge ? target : source),
            constraint,
            NavigationRayEndpointAllowance.StartPrefix));

        result.Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedRay_ShouldTraverseAutomaticSeamsInBothDirections(bool stacked)
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        NavigationAgentProfile profile = stacked
            ? new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                (Fixed64)2,
                (Fixed64)2,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None)
            : fixture.DefaultProfile;

        NavigationRayResult forward = RunRay(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            profile,
            fixture.Start,
            fixture.End));
        NavigationRayResult reverse = RunRay(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            profile,
            fixture.End,
            fixture.Start));

        forward.Status.Should().Be(NavigationRayStatus.Success);
        reverse.Status.Should().Be(NavigationRayStatus.Success);
        forward.StartAddress.Should().Be(reverse.EndAddress);
        forward.EndAddress.Should().Be(reverse.StartAddress);
    }

    [Fact]
    public void OrderedRay_ShouldRevisitAnEarlierCanonicalRecordUnlockedByALaterSeamSource()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        edges.MoveNext().Should().BeTrue();
        edges.Current.Kind.Should().Be(NavigationGraphEdgeKind.Seam);
        int selectedOrdinal = edges.CurrentOrdinal;
        Vector3d seamPoint = Vector3d.Lerp(fixture.Start, fixture.End, Fixed64.Half);
        var workspace = new NavigationRayWorkspace(4, 16, 16, 32, 32);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(32, 32));

        work.Begin(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            seamPoint,
            fixture.End,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                selectedOrdinal)));
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        NavigationRayResult result = work.Result;

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(sourceAddress);
        result.EndAddress.Should().Be(targetAddress);
    }

    [Fact]
    public void OrderedRay_ShouldPreserveTheTransitiveTieGroupAcrossAnUnmappedBridge()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        fixture.Context.World.TryAddGrid(
                new GridConfiguration(
                    Vector3d.Zero,
                    Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(16))),
                out _)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(4, 16, 16, 32, 32);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(32, 32));

        work.Begin(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            fixture.Start,
            fixture.End));
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);

        workspace.TraceIntervals
            .GroupBy(interval => interval.TieGroupId)
            .Should().Contain(group => group.Count() >= 3);
    }

    [Theory]
    [InlineData(false, false, false, false, false, (int)NavigationRayStatus.Success)]
    [InlineData(true, false, false, false, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(false, true, false, false, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(false, false, true, false, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(false, false, false, true, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(false, false, false, false, true, (int)NavigationRayStatus.CostOverflow)]
    public void OrderedRay_ShouldRequireTheExactExplicitCorridor(
        bool offLineEntry,
        bool offLineExit,
        bool tooWideForConnection,
        bool exitAtTargetWall,
        bool costOverflow,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-explicit",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "corridor",
                        source,
                        destination,
                        corridorCost: Fixed64.One,
                        radiusClearance: tooWideForConnection
                            ? Fixed64.One / (Fixed64)8
                            : Fixed64.One,
                        entryOffset: offLineEntry
                            ? new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)4)
                            : default,
                        exitOffset: offLineExit
                            ? new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)4)
                            : exitAtTargetWall
                                ? new Vector3d(
                                    (Fixed64)3 / (Fixed64)8,
                                    Fixed64.Zero,
                                    Fixed64.Zero)
                                : default)
                },
                cell: costOverflow
                    ? new NavigationCell(
                        TraversalMedia.Solid,
                        TraversalCapability.None,
                        default,
                        Fixed64.MaxValue,
                        Fixed64.One,
                        Fixed64.One)
                    : null);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var targetAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef targetNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetNode, out NavigationNodeState targetState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        int explicitOrdinal = -1;
        NavigationGraphEdge explicitEdge = default;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
            {
                explicitOrdinal = edges.CurrentOrdinal;
                explicitEdge = edges.Current;
            }
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);

        NavigationAgentProfile profile = !tooWideForConnection && !exitAtTargetWall
            ? fixture.DefaultProfile
            : new NavigationAgentProfile(
                new KinematicBodyShape(
                    Fixed64.One / (Fixed64)4,
                    Fixed64.One,
                    Fixed64.Zero),
                maxStepUp: Fixed64.Zero,
                maxDropDown: Fixed64.Zero,
                arrivalRadius: Fixed64.Zero,
                allowedMedia: TraversalMedia.Solid,
                capabilities: TraversalCapability.None);
        if (exitAtTargetWall)
        {
            var evaluator = new TraversalEvaluator(
                fixture.Graph,
                profile,
                NavigationAStarExitTestHarness.Policy,
                TraversalMedium.Solid);
            var route = new NavigationSurfaceEdgeRouteWork();
            route.Begin(evaluator, sourceNode, explicitEdge, emitPoints: false)
                .Should().Be(NavigationSurfaceEdgeRouteStatus.Pending);
            int connectionSteps = 1;
            route.Advance(
                    new NavigationWorkMeter(CreateRayBudget(64, 64)),
                    ref connectionSteps)
                .Should().Be(NavigationSurfaceEdgeRouteStatus.Impassable,
                    "the portal-to-exit body segment clips the target's outer wall");
        }
        NavigationRayRequest request = CreateRequest(
            world,
            store,
            fixture.Graph,
            profile,
            sourceState.FootAnchor,
            targetState.FootAnchor,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal));
        var workspace = new NavigationRayWorkspace(4, 32, 32, 64, 64);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(64, 64));
        work.Begin(request);
        work.Advance(meter);
        NavigationRayResult result = work.Result;

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if ((NavigationRayStatus)expectedStatus == NavigationRayStatus.Success)
            result.TraversalCost.Should().Be(Fixed64.One);
        workspace.ChainRecords.Should().OnlyContain(record =>
            record.IncomingExplicitConnection == null);
    }

    [Fact]
    public void OrderedRay_StartPrefixShouldRejectAnAlignedExplicitCorridorThatClipsTheSourceWall()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        var wallOffset = new Vector3d(
            -((Fixed64)3 / (Fixed64)8),
            Fixed64.Zero,
            Fixed64.Zero);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-explicit-start-prefix-wall",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "corridor",
                        source,
                        destination,
                        corridorCost: Fixed64.One,
                        radiusClearance: Fixed64.One,
                        entryOffset: wallOffset,
                        exitOffset: wallOffset)
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var destinationAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationNode, out NavigationNodeState destinationState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        NavigationGraphEdge explicitEdge = default;
        int explicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
            {
                explicitEdge = edges.Current;
                explicitOrdinal = edges.CurrentOrdinal;
            }
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        Vector3d start = sourceState.FootAnchor + wallOffset;
        Vector3d end = destinationState.FootAnchor + wallOffset;
        NavigationExplicitConnectionRecord connection = explicitEdge.ExplicitConnection;
        connection.Definition.EntryAnchor.Should().Be(start);
        connection.Definition.ExitAnchor.Should().Be(end);
        fixture.Graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(destinationAddress, out GridCellPrism destinationPrism)
            .Should().BeTrue();
        sourcePrism.Contains(start).Should().BeTrue();
        var portals = connection.NavigationPortals.GetEnumerator();
        portals.MoveNext().Should().BeTrue();
        GridNavigationPortal portal = portals.Current;
        GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                destinationPrism,
                portal,
                start,
                end,
                profile.Shape.Radius,
                profile.Shape.Height,
                out Fixed64 sourceParameter,
                out _)
            .Should().BeTrue("the aligned centerline crosses the profile-resolved portal");
        Vector3d outgoingPoint = Vector3d.Lerp(start, end, sourceParameter);
        GridCellGeometry.IsNavigationBodySegmentValid(
                sourcePrism,
                start,
                outgoingPoint,
                profile.Shape.Radius,
                profile.Shape.Height,
                default,
                portal,
                GridNavigationBodySegmentEndpointAllowance.None)
            .Should().BeFalse("the source is inside the prism but its body clips the outer wall");
        NavigationRayRequest request = CreateRequest(
            world,
            store,
            fixture.Graph,
            profile,
            start,
            end,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                destinationAddress,
                explicitOrdinal),
            NavigationRayEndpointAllowance.StartPrefix);

        NavigationRayResult result = RunRay(request);

        result.Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Fact]
    public void OrderedRay_StartPrefixShouldRejectAnOrdinaryLegThatClipsTheSourceWall()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-ordinary-wall");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var destinationAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(destinationAddress, out GridCellPrism destinationPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                destinationPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        var wallOffset = new Vector3d(
            -((Fixed64)3 / (Fixed64)8),
            Fixed64.Zero,
            Fixed64.Zero);
        Vector3d start = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source)
            + wallOffset;
        Vector3d end = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, destination)
            + wallOffset;
        GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                destinationPrism,
                portal,
                start,
                end,
                profile.Shape.Radius,
                profile.Shape.Height,
                out Fixed64 sourceParameter,
                out _)
            .Should().BeTrue("the aligned centerline crosses the eroded portal");
        Vector3d outgoingPoint = Vector3d.Lerp(start, end, sourceParameter);
        GridCellGeometry.IsNavigationBodySegmentValid(
                sourcePrism,
                start,
                outgoingPoint,
                profile.Shape.Radius,
                profile.Shape.Height,
                default,
                portal,
                GridNavigationBodySegmentEndpointAllowance.None)
            .Should().BeFalse("the source foot is inside the prism but its body clips the outer wall");

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            profile,
            start,
            end,
            endpointAllowance: NavigationRayEndpointAllowance.StartPrefix));

        result.Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Fact]
    public void OrderedRay_ShouldRejectTheDirectShortcutAcrossAnLShapedExplicitCorridor()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var witness = new VoxelIndex(1, 0, 0);
        var destination = new VoxelIndex(1, 0, 1);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 1, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                configuration,
                new[] { source, witness, destination },
                "ray-l-corridor",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "turn",
                        source,
                        destination,
                        corridorCost: Fixed64.One,
                        radiusClearance: Fixed64.One,
                        witnesses: new[] { witness })
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var destinationAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        int explicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                explicitOrdinal = edges.CurrentOrdinal;
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        Vector3d start = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            source);
        Vector3d end = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            destination);
        NavigationRayRequest request = CreateRequest(
            world,
            store,
            fixture.Graph,
            profile,
            start,
            end,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                destinationAddress,
                explicitOrdinal));

        NavigationRayResult result = RunRay(request);

        result.Status.Should().Be(NavigationRayStatus.Blocked,
            "a direct diagonal may not skip the certified right-angle witness corridor");
    }

    [Theory]
    [InlineData((int)GridTopologyKind.RectangularPrism, (int)HexOrientation.PointyTop)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.PointyTop)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.FlatTop)]
    public void OrderedRay_ShouldFollowNativeChainsAcrossEveryTopology(
        int topologyValue,
        int orientationValue)
    {
        GridTopologyKind topology = (GridTopologyKind)topologyValue;
        HexOrientation orientation = (HexOrientation)orientationValue;
        using var world = new GridForge.Grids.GridWorld();
        GridTopologyMetrics metrics = topology == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(Fixed64.One)
            : GridTopologyMetrics.Hex((Fixed64)2, Fixed64.One, orientation);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(12, 2, 12),
            topologyKind: topology,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex start;
        VoxelIndex middle;
        VoxelIndex end;
        if (topology == GridTopologyKind.RectangularPrism)
        {
            start = default;
            middle = new VoxelIndex(1, 0, 0);
            end = new VoxelIndex(2, 0, 0);
        }
        else
        {
            FindHexLine(binding, out start, out middle, out end);
        }
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { start, middle, end },
                topology == GridTopologyKind.RectangularPrism
                    ? "ray-rect"
                    : orientation == HexOrientation.PointyTop ? "ray-pointy" : "ray-flat");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(binding, start),
            NavigationAStarExitTestHarness.GetFoot(binding, end)));

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(new NavigationCellAddress(fixture.MapId, start));
        result.EndAddress.Should().Be(new NavigationCellAddress(fixture.MapId, end));
    }

    [Fact]
    public void OrderedRay_ShouldRejectAnInteriorSparseHole()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { start, end },
                "ray-hole");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                fixture.DefaultProfile,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, start),
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, end)))
            .Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(false, (int)NavigationRayStatus.Success)]
    [InlineData(true, (int)NavigationRayStatus.Blocked)]
    public void OrderedRay_ShouldRejectPositiveRadiusWallClipping(
        bool nearWall,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { start, new VoxelIndex(1, 0, 0), end },
                "ray-radius");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d offset = nearWall
            ? new Vector3d(Fixed64.Zero, Fixed64.Zero, (Fixed64)2 / (Fixed64)5)
            : default;
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, start) + offset,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, end) + offset))
            .Status.Should().Be((NavigationRayStatus)expectedStatus);
    }

    [Theory]
    [InlineData((int)NavigationRayEndpointAllowance.StartPrefix)]
    [InlineData((int)NavigationRayEndpointAllowance.DestinationSuffix)]
    public void OrderedRay_ShouldPermitOnlyTheRequestedEndpointBoundary(
        int allowanceValue)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex cell = default;
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(1),
                new[] { cell },
                "ray-endpoint");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d foot = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cell);
        Vector3d outside = foot - new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        var address = new NavigationCellAddress(fixture.MapId, cell);
        NavigationRayEndpointAllowance allowance =
            (NavigationRayEndpointAllowance)allowanceValue;
        Vector3d start = allowance == NavigationRayEndpointAllowance.StartPrefix
            ? outside
            : foot;
        Vector3d end = allowance == NavigationRayEndpointAllowance.StartPrefix
            ? foot
            : outside;

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                start,
                end,
                NavigationRayChainConstraint.SourceOnly(address),
                allowance))
            .Status.Should().Be(NavigationRayStatus.Success);
        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                start,
                end,
                NavigationRayChainConstraint.SourceOnly(address)))
            .Status.Should().Be(NavigationRayStatus.Blocked);

        if (allowance == NavigationRayEndpointAllowance.StartPrefix)
        {
            NavigationRayResult unrestricted = RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                start,
                end,
                endpointAllowance: allowance));

            unrestricted.Status.Should().Be(NavigationRayStatus.Success);
            unrestricted.StartAddress.Should().Be(address,
                "an unrestricted prefix ray must seed the first valid covered cell");
        }
    }

    [Fact]
    public void OrderedRay_ShouldReachTheFarthestValidDestinationSuffixCell()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-destination-suffix");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d destinationFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            destination);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
            destinationFoot + new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            endpointAllowance: NavigationRayEndpointAllowance.DestinationSuffix));

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.EndAddress.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));

    }

    [Fact]
    public void DestinationSuffix_WithOffRayRequiredFinish_ShouldBeBlocked()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var crossed = new VoxelIndex(1, 0, 0);
        var requiredFinish = new VoxelIndex(1, 0, 1);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 1, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[]
                {
                    source,
                    crossed,
                    new VoxelIndex(0, 0, 1),
                    requiredFinish
                },
                "ray-destination-suffix-finish");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d start = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            source);
        Vector3d end = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            crossed) + Vector3d.Right;

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            start,
            end,
            NavigationRayChainConstraint.FinishAt(
                new NavigationCellAddress(fixture.MapId, requiredFinish)),
            NavigationRayEndpointAllowance.DestinationSuffix));

        result.Status.Should().Be(NavigationRayStatus.Blocked,
            "a suffix may not finish in a different cell from the required address");
    }

    [Fact]
    public void DestinationSuffix_ShouldRequireTheFinalPrismCertificationBudget()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-suffix-budget");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        NavigationRayRequest request = CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, destination)
                + Vector3d.Right,
            endpointAllowance: NavigationRayEndpointAllowance.DestinationSuffix);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        const int exactPrismChecks = 3;

        for (int allowance = 0; allowance < exactPrismChecks; allowance++)
        {
            var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
                maxCurrentNodeLookupProbes: 64,
                maxCursorLegScans: 64,
                maxCursorRebases: 0,
                maxPortalChecks: 64,
                maxPrismChecks: allowance,
                maxTraceIntervals: 64,
                maxLocalRecoveryAttempts: 0));
            work.Begin(request);

            work.Advance(ref meter).Should().Be(
                NavigationRayStatus.BudgetExceeded,
                "prism allowance {0} is below the exact suffix proof boundary",
                allowance);
        }

        var exactMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 64,
            maxCursorLegScans: 64,
            maxCursorRebases: 0,
            maxPortalChecks: 64,
            maxPrismChecks: exactPrismChecks,
            maxTraceIntervals: 64,
            maxLocalRecoveryAttempts: 0));
        work.Begin(request);

        work.Advance(ref exactMeter).Should().Be(NavigationRayStatus.Success);
        work.Result.EndAddress.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));
    }

    [Fact]
    public void OrderedRay_ShouldMeterAndValidateEveryExplicitWitnessLeg()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex source = default;
        var firstWitness = new VoxelIndex(1, 0, 0);
        var secondWitness = new VoxelIndex(2, 0, 0);
        var destination = new VoxelIndex(3, 0, 0);
        Vector3d sourceFoot = NavigationAStarExitTestHarness.GetFoot(binding, source);
        Vector3d destinationFoot = NavigationAStarExitTestHarness.GetFoot(binding, destination);
        var connection = new NavigationConnection(
            "ray-multi",
            source,
            new NavigationCellAddress("ray-multi", destination),
            sourceFoot,
            destinationFoot,
            Fixed64.Half,
            Fixed64.One,
            new[]
            {
                new NavigationCellAddress("ray-multi", firstWitness),
                new NavigationCellAddress("ray-multi", secondWitness)
            },
            additionalCost: Fixed64.Half);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        SimulateUntilTerminal(context, policyOperation.Receipt);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("ray-multi", binding)
                    .AddCell(source, NavigationAStarExitTestHarness.Cell)
                    .AddCell(
                        firstWitness,
                        new NavigationCell(
                            TraversalMedia.Solid,
                            TraversalCapability.Climb,
                            default,
                            Fixed64.Zero,
                            (Fixed64)4,
                            (Fixed64)4))
                    .AddCell(secondWitness, NavigationAStarExitTestHarness.Cell)
                    .AddCell(destination, NavigationAStarExitTestHarness.Cell)
                    .AddConnection(connection)
                    .Build(),
                1),
            OverlayReplacementPolicy.Clear,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        SimulateUntilTerminal(context, mapOperation.Receipt);
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current;
        var sourceAddress = new NavigationCellAddress("ray-multi", source);
        var targetAddress = new NavigationCellAddress("ray-multi", destination);
        graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        int explicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                explicitOrdinal = edges.CurrentOrdinal;
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationAgentProfile baselineProfile = NavigationAStarExitTestHarness.Profile();
        var capableProfile = new NavigationAgentProfile(
            baselineProfile.Shape,
            baselineProfile.MaxStepUp,
            baselineProfile.MaxDropDown,
            baselineProfile.ArrivalRadius,
            baselineProfile.AllowedMedia,
            TraversalCapability.Climb);
        NavigationRayRequest request = CreateRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            capableProfile,
            sourceFoot,
            destinationFoot,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal));

        var guideWork = new NavigationRayWork(
            new NavigationRayWorkspace(4, 32, 32, 64, 64));
        var noPortalMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 0,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref noPortalMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "an explicit portal proof cannot be reclassified as geometric blockage");
        var noPrismMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 0,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref noPrismMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "an explicit body-segment proof cannot be reclassified as geometric blockage");
        var finalPortalBlockedMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 3,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref finalPortalBlockedMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "the final explicit segment must debit its retained incoming portal");
        var exactPortalMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 4,
            maxPrismChecks: 128,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref exactPortalMeter).Should().Be(NavigationRayStatus.Success);
        var firstLegPrismBlockedMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 1,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref firstLegPrismBlockedMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "the first explicit segment must debit its source-prism proof");
        var finalPrismBlockedMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 4,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref finalPrismBlockedMeter)
            .Should().Be(NavigationRayStatus.BudgetExceeded,
                "the final explicit segment must debit its target-prism proof");
        var exactPrismMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 128,
            maxCursorLegScans: 128,
            maxCursorRebases: 0,
            maxPortalChecks: 128,
            maxPrismChecks: 5,
            maxTraceIntervals: 128,
            maxLocalRecoveryAttempts: 0));
        guideWork.Begin(request);
        guideWork.Advance(ref exactPrismMeter).Should().Be(NavigationRayStatus.Success);

        RunRay(request, CreateRayBudget(64, 64, connectionLegs: 2))
            .Status.Should().Be(NavigationRayStatus.BudgetExceeded);
        NavigationRayResult success = RunRay(
            request,
            CreateRayBudget(64, 64, connectionLegs: 3));
        success.Status.Should().Be(NavigationRayStatus.Success);
        success.StartAddress.Should().Be(sourceAddress);
        success.EndAddress.Should().Be(targetAddress);
        success.IsSemanticCostNeutral.Should().BeFalse();
        NavigationRayResult missingCapability = RunRay(CreateRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            baselineProfile,
            sourceFoot,
            destinationFoot,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal)));
        missingCapability.Status.Should().Be(NavigationRayStatus.Blocked,
            "the published corridor must re-evaluate every witness for this profile");
        var oversizedProfileShape = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One,
                baselineProfile.Shape.Height,
                baselineProfile.Shape.RootToFootOffsetY),
            baselineProfile.MaxStepUp,
            baselineProfile.MaxDropDown,
            baselineProfile.ArrivalRadius,
            baselineProfile.AllowedMedia,
            TraversalCapability.Climb);
        NavigationRayResult oversizedProfile = RunRay(CreateRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            oversizedProfileShape,
            sourceFoot,
            destinationFoot,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal)));
        oversizedProfile.Status.Should().Be(NavigationRayStatus.Blocked,
            "a valid profile wider than the explicit corridor portals cannot traverse it");
        var positiveRadiusProfile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baselineProfile.Shape.Height,
                baselineProfile.Shape.RootToFootOffsetY),
            baselineProfile.MaxStepUp,
            baselineProfile.MaxDropDown,
            baselineProfile.ArrivalRadius,
            baselineProfile.AllowedMedia,
            TraversalCapability.Climb);
        Vector3d wallOffset = new(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)3 / (Fixed64)8);
        NavigationRayResult wallClipping = RunRay(CreateRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            positiveRadiusProfile,
            sourceFoot + wallOffset,
            destinationFoot + wallOffset,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal)));
        wallClipping.Status.Should().Be(NavigationRayStatus.Blocked,
            "every witnessed leg must certify the positive-radius body segment");

        RunRay(CreateRequest(
                context.World,
                context.Pathing.NavigationGraphStore,
                graph,
                capableProfile,
                sourceFoot - new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
                destinationFoot,
                NavigationRayChainConstraint.SelectedEdge(
                    sourceAddress,
                    targetAddress,
                    explicitOrdinal),
                NavigationRayEndpointAllowance.StartPrefix))
            .Status.Should().Be(NavigationRayStatus.Success);
    }

    [Theory]
    [InlineData(0, 1, false, false, (int)NavigationRayStatus.Success)]
    [InlineData(2, 1, false, false, (int)NavigationRayStatus.Success)]
    [InlineData(0, -1, false, false, (int)NavigationRayStatus.Success)]
    [InlineData(2, -1, false, false, (int)NavigationRayStatus.Success)]
    [InlineData(0, 1, true, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(0, 1, true, true, (int)NavigationRayStatus.Success)]
    public void OrderedRay_ShouldOrderConsecutiveExplicitEdgesAndKeepThemDirected(
        int axis,
        int direction,
        bool reverseSharedAnchors,
        bool includeEarlierAlternative,
        int expectedStatus)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridConfiguration startConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        Vector3d middleCenter = AxisVector(axis, (Fixed64)(2 * direction));
        GridConfiguration middleConfiguration = new(
            middleCenter,
            middleCenter,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        Vector3d endCenter = AxisVector(axis, (Fixed64)(4 * direction));
        GridConfiguration endConfiguration = new(
            endCenter,
            endCenter,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(startConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(middleConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(endConfiguration, out _).Should().BeTrue();
        startConfiguration.TryNormalize(out NormalizedGridConfiguration startBinding)
            .Should().BeTrue();
        middleConfiguration.TryNormalize(out NormalizedGridConfiguration middleBinding)
            .Should().BeTrue();
        endConfiguration.TryNormalize(out NormalizedGridConfiguration endBinding)
            .Should().BeTrue();
        Vector3d startFoot = NavigationAStarExitTestHarness.GetFoot(
            startBinding,
            default);
        Vector3d middleFoot = NavigationAStarExitTestHarness.GetFoot(
            middleBinding,
            default);
        Vector3d endFoot = NavigationAStarExitTestHarness.GetFoot(endBinding, default);
        Vector3d quarter = AxisVector(
            axis,
            (Fixed64)direction / (Fixed64)4);
        NavigationCell cell = NavigationAStarExitTestHarness.Cell;
        NavigationAgentProfile profile = NavigationAStarExitTestHarness.Profile();
        var first = new NavigationConnection(
            includeEarlierAlternative ? "a-late" : "first",
            default,
            new NavigationCellAddress("m-middle", default),
            startFoot,
            middleFoot + (reverseSharedAnchors ? quarter : -quarter),
            Fixed64.Zero,
            Fixed64.One);
        NavigationConnection? earlierAlternative = includeEarlierAlternative
            ? new NavigationConnection(
                "z-early",
                default,
                new NavigationCellAddress("m-middle", default),
                startFoot,
                middleFoot - quarter,
                Fixed64.Zero,
                Fixed64.One)
            : null;
        var next = new NavigationConnection(
            "next",
            default,
            new NavigationCellAddress("a-end", default),
            middleFoot + (reverseSharedAnchors ? Vector3d.Zero : quarter),
            endFoot,
            Fixed64.Zero,
            Fixed64.One);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        SimulateUntilTerminal(context, policyOperation.Receipt);
        var startBuilder = new NavigationMapBuilder("z-start", startBinding)
            .AddCell(default, cell)
            .AddConnection(first);
        if (earlierAlternative != null)
            startBuilder.AddConnection(earlierAlternative);
        var middleBuilder = new NavigationMapBuilder("m-middle", middleBinding)
            .AddCell(default, cell)
            .AddConnection(next);
        NavigationMapCommitOperation[] maps =
        {
            new(
                new PreparedNavigationMap(
                    startBuilder.Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                1,
                context.FrameCount + 1),
            new(
                new PreparedNavigationMap(
                    middleBuilder.Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                2,
                context.FrameCount + 1),
            new(
                new PreparedNavigationMap(
                    new NavigationMapBuilder("a-end", endBinding)
                        .AddCell(default, cell)
                        .Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                3,
                context.FrameCount + 1)
        };
        for (int i = 0; i < maps.Length; i++)
            context.Pathing.Admit(maps[i]).Should().BeTrue();
        SimulateUntilTerminal(context, maps[maps.Length - 1].Receipt);
        for (int i = 0; i < maps.Length; i++)
        {
            maps[i].Receipt.Status.Should().Be(
                NavigationOperationStatus.Applied,
                $"map {i} must publish before ray evaluation; rejection was {maps[i].Receipt.Rejection}");
        }
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current
            .WithAutomaticSeams(NavigationAutomaticSeamIndex.Empty);
        graph = graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
                context.World,
                store,
                graph,
                profile,
                startFoot,
                endFoot));

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if (!reverseSharedAnchors && !includeEarlierAlternative)
        {
            RunRay(CreateRequest(
                    context.World,
                    store,
                    graph,
                    profile,
                    endFoot,
                    startFoot))
                .Status.Should().Be(NavigationRayStatus.Blocked);
        }
    }

    [Fact]
    public void OrderedRay_ShouldAcceptOnlyDependencyCompatiblePublications()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture compatible =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest compatibleRequest = CreateLineRequest(compatible);
        compatible.Store.TryPublish(
                compatible.Graph.WithGraphVersion(compatible.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);

        RunRay(compatibleRequest).Status.Should().Be(NavigationRayStatus.Success);

        using NavigationFlowFieldCacheTestHarness.LineFixture stale =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest staleRequest = CreateLineRequest(stale);
        var revisedPolicy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey(
                NavigationAStarExitTestHarness.Policy.Key.PolicyId,
                NavigationAStarExitTestHarness.Policy.Key.Revision + 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        NavigationAreaCatalog.Empty.TryPublish(
                revisedPolicy,
                1,
                1,
                1,
                1,
                out NavigationAreaCatalog revisedCatalog)
            .Should().Be(NavigationOperationRejection.None);
        stale.Store.TryPublish(stale.Graph.WithAreaCatalog(
                revisedCatalog,
                stale.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);

        RunRay(staleRequest).Status.Should().Be(NavigationRayStatus.Stale);

        using NavigationFlowFieldCacheTestHarness.LineFixture componentStale =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest componentRequest = CreateLineRequest(componentStale);
        componentStale.Graph.TryGetSurfaceComponent(
                componentStale.FarOrigin,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey component,
                out _)
            .Should().BeTrue();
        NavigationWorldGraph closed = componentStale.Graph
            .WithClosedStructuralComponents(
                NavigationSurfaceComponentKeySet.Empty.Add(component),
                closeAllStructuralComponents: false,
                componentStale.Graph.GraphVersion + 1);
        componentStale.Store.TryPublish(closed).Should().Be(
            NavigationCandidatePublication.Published);

        RunRay(componentRequest).Status.Should().Be(NavigationRayStatus.Stale,
            "closing a consumed surface component invalidates the exact ray proof");
    }

    [Fact]
    public void OrderedRay_ShouldRejectPreTraceRawWorldMutationAsStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        fixture.Graph.TryGetMap(
                fixture.FarOrigin.MapId,
                out NavigationMapInstance? instance)
            .Should().BeTrue();
        instance.Should().NotBeNull();
        fixture.World.ActiveGrids[instance!.GridIdentity.GridIndex]
            .TryRemoveVoxel(new VoxelIndex(2, 0, 0))
            .Should().BeTrue();

        RunRay(request).Status.Should().Be(NavigationRayStatus.Stale);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    public void OrderedRay_ShouldFailClosedWhenDependencyScratchIsTooSmall(
        int pageCapacity,
        int componentCapacity)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var work = new NavigationRayWork(new NavigationRayWorkspace(
            1,
            pageCapacity,
            componentCapacity,
            16,
            16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));

        work.Begin(CreateLineRequest(fixture));

        work.Advance(meter).Should().Be(NavigationRayStatus.CapacityExceeded);
    }

    [Fact]
    public void OrderedRay_ShouldAllocateZeroBytesAfterWarmup()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));
        GuideSampleWorkBudget guideBudget = new(
            maxCurrentNodeLookupProbes: 64,
            maxCursorLegScans: 64,
            maxCursorRebases: 0,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 64,
            maxLocalRecoveryAttempts: 0);
        var guideMeter = new GuideSampleWorkMeter(guideBudget);
        for (int i = 0; i < 16; i++)
        {
            meter.Reset(CreateRayBudget(16, 16));
            work.Begin(request);
            work.Advance(meter).Should().Be(NavigationRayStatus.Success);
            guideMeter = new GuideSampleWorkMeter(guideBudget);
            work.Begin(request);
            work.Advance(ref guideMeter).Should().Be(NavigationRayStatus.Success);
        }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        NavigationRayStatus status = default;
        NavigationRayStatus guideStatus = default;
        for (int i = 0; i < 256; i++)
        {
            meter.Reset(CreateRayBudget(16, 16));
            work.Begin(request);
            status = work.Advance(meter);
            guideMeter = new GuideSampleWorkMeter(guideBudget);
            work.Begin(request);
            guideStatus = work.Advance(ref guideMeter);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationRayStatus.Success);
        guideStatus.Should().Be(NavigationRayStatus.Success);
        allocated.Should().Be(0);
    }

    [Fact]
    public void OrderedRay_ShouldHonorEveryGuideMeterAllowanceBelowTheExactBoundary()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        int[] exact = { 15, 6, 9, 5, 4 };

        for (int category = 0; category < exact.Length; category++)
        {
            for (int allowance = 0; allowance < exact[category]; allowance++)
            {
                int[] limits = (int[])exact.Clone();
                limits[category] = allowance;
                var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
                    maxCurrentNodeLookupProbes: limits[0],
                    maxCursorLegScans: limits[1],
                    maxCursorRebases: 0,
                    maxPortalChecks: limits[2],
                    maxPrismChecks: limits[3],
                    maxTraceIntervals: limits[4],
                    maxLocalRecoveryAttempts: 0));

                work.Begin(request);
                work.Advance(ref meter).Should().Be(
                    NavigationRayStatus.BudgetExceeded,
                    "guide meter category {0} with allowance {1} is below exact",
                    category,
                    allowance);
            }
        }

        var exactMeter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            exact[0],
            exact[1],
            maxCursorRebases: 0,
            exact[2],
            exact[3],
            exact[4],
            maxLocalRecoveryAttempts: 0));

        work.Begin(request);
        work.Advance(ref exactMeter).Should().Be(NavigationRayStatus.Success);
    }

    private static NavigationRayRequest CreateLineRequest(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef startNode)
            .Should().BeTrue();
        NavigationCellAddress endAddress = fixture.Near.Nodes[0].Address;
        fixture.Graph.TryGetNodeRef(endAddress, out NavigationNodeRef endNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(startNode, out NavigationNodeState startState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(endNode, out NavigationNodeState endState)
            .Should().BeTrue();
        return CreateRequest(
            fixture.World,
            fixture.Store,
            fixture.Graph,
            NavigationAStarExitTestHarness.Profile(),
            startState.FootAnchor,
            endState.FootAnchor);
    }

    private static NavigationExplicitConnectionRecord CreateExplicitRecord(
        Vector3d exitAnchor)
    {
        var definition = new NavigationConnection(
            "ray-policy",
            default,
            new NavigationCellAddress("ray-policy", default),
            Vector3d.Zero,
            exitAnchor,
            Fixed64.Zero,
            Fixed64.One);
        return new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("ray-policy", definition.Id),
            definition,
            isActive: true,
            Fixed64.Zero,
            NavigationPagedSequence<GridNavigationPortal>.Empty);
    }

    private static NavigationRayRequest CreateRequest(
        GridForge.Grids.GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        Vector3d start,
        Vector3d end,
        NavigationRayChainConstraint constraint = default,
        NavigationRayEndpointAllowance endpointAllowance =
            NavigationRayEndpointAllowance.None) => new(
        world,
        store,
        graph,
        profile,
        NavigationAStarExitTestHarness.Policy,
        TraversalMedium.Solid,
        start,
        end,
        endpointAllowance,
        constraint);

    private static NavigationRayResult RunRay(NavigationRayRequest request)
        => RunRay(request, CreateRayBudget(64, 64));

    private static NavigationRayResult RunRay(
        NavigationRayRequest request,
        NavigationWorkBudget budget)
    {
        var work = new NavigationRayWork(new NavigationRayWorkspace(4, 32, 32, 64, 64));
        var meter = new NavigationWorkMeter(budget);
        work.Begin(request);
        work.Advance(meter);
        return work.Result;
    }

    private static NavigationWorkBudget CreateRayBudget(
        int traceCapacity,
        int coveredCapacity,
        int connectionLegs = 1_024,
        int lookupProbes = 1_024,
        int evaluatedEdges = 1_024) => new(
        maxLookupProbes: lookupProbes,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: evaluatedEdges,
        maxConnectionLegs: connectionLegs,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: traceCapacity,
        maxCoveredVoxelIntervals: coveredCapacity,
        maxSimplificationRays: 0);

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
            frame < 1_024 && receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
    }

    private static Vector3d AxisVector(int axis, Fixed64 value) => axis switch
    {
        0 => new Vector3d(value, Fixed64.Zero, Fixed64.Zero),
        1 => new Vector3d(Fixed64.Zero, value, Fixed64.Zero),
        _ => new Vector3d(Fixed64.Zero, Fixed64.Zero, value)
    };

    private static void FindHexLine(
        NormalizedGridConfiguration binding,
        out VoxelIndex start,
        out VoxelIndex middle,
        out VoxelIndex end)
    {
        HexDirection[] directions =
        {
            HexDirection.QNegative,
            HexDirection.QNegativeRPositive,
            HexDirection.RNegative,
            HexDirection.RPositive,
            HexDirection.QPositiveRNegative,
            HexDirection.QPositive
        };
        for (int q = 0; q < binding.Width; q++)
        {
            for (int r = 0; r < binding.Length; r++)
            {
                var candidate = new VoxelIndex(q, 0, r);
                for (int direction = 0; direction < directions.Length; direction++)
                {
                    VoxelIndex offset = HexDirectionUtility.GetOffset(directions[direction]);
                    var next = new VoxelIndex(
                        candidate.x + offset.x,
                        candidate.y + offset.y,
                        candidate.z + offset.z);
                    var last = new VoxelIndex(
                        next.x + offset.x,
                        next.y + offset.y,
                        next.z + offset.z);
                    if (!binding.IsValidIndex(candidate)
                        || !binding.IsValidIndex(next)
                        || !binding.IsValidIndex(last))
                    {
                        continue;
                    }
                    start = candidate;
                    middle = next;
                    end = last;
                    return;
                }
            }
        }
        start = default;
        middle = default;
        end = default;
        throw new System.InvalidOperationException("The test grid has no three-cell hex ray.");
    }
}
