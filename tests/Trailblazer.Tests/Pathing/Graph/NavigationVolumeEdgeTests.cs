//=======================================================================
// NavigationVolumeEdgeTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;
using NavigationVolumeEdgeStatus = Trailblazer.Pathing.NavigationTraversalEvaluationStatus;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationVolumeEdgeTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("volume-edge", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void VolumeCost_ShouldRejectEachCheckedAccumulationOverflow(int stage)
    {
        Vector3d source = Vector3d.Zero;
        Vector3d target = Vector3d.Right;
        Fixed64 enterCost = (Fixed64)2;
        Fixed64 additionalEnterCost = (Fixed64)3;
        switch (stage)
        {
            case 0:
                source = new Vector3d(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero);
                target = new Vector3d(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero);
                break;
            case 1:
                enterCost = Fixed64.MaxValue;
                additionalEnterCost = Fixed64.Zero;
                break;
            case 2:
                target = Vector3d.Zero;
                enterCost = Fixed64.MaxValue;
                additionalEnterCost = Fixed64.One;
                break;
        }

        NavigationVolumeEdgeEvaluator.TryGetCost(
                source,
                target,
                enterCost,
                additionalEnterCost,
                out Fixed64 total)
            .Should().Be(stage == 3);

        if (stage == 3)
            total.Should().Be((Fixed64)6);
    }


    [Theory]
    [InlineData(1UL, 2UL, (int)GridNavigationBodyTraceStatus.Complete, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.Stale)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.Complete, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.Success)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.Unavailable)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.Unavailable)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.ArithmeticOverflow, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.CostOverflow)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.GridCandidateLimitExceeded, 0, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.GridCandidateLimitExceeded, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.CapacityExceeded)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.AddressLimitExceeded, 1, 1, 0, 1,
        (int)NavigationVolumeAnchorStatus.BudgetExceeded)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.AddressLimitExceeded, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.CapacityExceeded)]
    [InlineData(1UL, 1UL, (int)GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded, 1, 1, 1, 1,
        (int)NavigationVolumeAnchorStatus.BudgetExceeded)]
    public void VolumeAnchorTraceStatus_ShouldPreserveEpochBudgetAndCapacitySemantics(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        int traceStatusValue,
        int gridLimit,
        int mapCapacity,
        int addressLimit,
        int coveredAddressCapacity,
        int expectedStatusValue)
    {
        NavigationVolumeAnchorEvaluator.ResolveTraceStatus(
                worldSequenceBefore,
                worldSequenceAfter,
                (GridNavigationBodyTraceStatus)traceStatusValue,
                gridLimit,
                mapCapacity,
                addressLimit,
                coveredAddressCapacity)
            .Should().Be((NavigationVolumeAnchorStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData(4UL, 4UL, (int)NavigationVolumeAnchorStatus.Success,
        (int)NavigationVolumeAnchorStatus.Success)]
    [InlineData(4UL, 5UL, (int)NavigationVolumeAnchorStatus.Unavailable,
        (int)NavigationVolumeAnchorStatus.Stale)]
    public void VolumeAnchorFinalStatus_ShouldRejectAnEpochChange(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        int resultValue,
        int expectedStatusValue)
    {
        NavigationVolumeAnchorEvaluator.ResolveFinalStatus(
                worldSequenceBefore,
                worldSequenceAfter,
                (NavigationVolumeAnchorStatus)resultValue)
            .Should().Be((NavigationVolumeAnchorStatus)expectedStatusValue);
    }

    [Theory]
    [InlineData(true, 4UL, 4UL, true)]
    [InlineData(true, 4UL, 5UL, false)]
    [InlineData(false, 4UL, 4UL, false)]
    [InlineData(false, 4UL, 5UL, false)]
    public void VolumeAnchorTraceGeneration_ShouldRequireIdentityAndExactSequence(
        bool identityMatches,
        ulong instanceLastChangeSequence,
        ulong traceLastChangeSequence,
        bool expected)
    {
        NavigationVolumeAnchorEvaluator.IsTraceGenerationCurrent(
                identityMatches,
                instanceLastChangeSequence,
                traceLastChangeSequence)
            .Should().Be(expected);
    }

    [Fact]
    public void Dispatcher_ShouldPreserveSolidNativeEvaluation()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Solid, enterCost: Fixed64.One),
            Cell(TraversalMedia.Solid, enterCost: (Fixed64)7));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
        NavigationGraphEdge surfaceEdge = FindSurfaceEdge(lease.Graph, source, target);
        NavigationAgentProfile profile = Profile(TraversalMedia.Solid);
        var surface = new TraversalEvaluator(
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Solid);
        surface.EvaluateEdge(source, surfaceEdge, out TraversalEdgeEvidence expected)
            .Should().Be(TraversalEvaluationStatus.Passable);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Solid),
            profile,
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = 64;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(
            new NavigationMediumStateRef(target, TraversalMedium.Solid));
        dispatcher.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Surface);
        dispatcher.CurrentOrdinal.Should().Be(0);
        dispatcher.CurrentCost.Should().Be(expected.Cost);
    }

    [Fact]
    public void Dispatcher_ShouldKeepGasOpenWhenSolidIsClosedAndIgnoreStepLimits()
    {
        var targetIndex = new VoxelIndex(0, 1, 0);
        NavigationCell volume = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            default,
            targetIndex,
            volume,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas, enterCost: (Fixed64)7));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        lease.Graph.TryGetSurfaceComponent(
                new NavigationCellAddress("map", default),
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey solid,
                out _)
            .Should().BeTrue();
        NavigationWorldGraph gasOnly = lease.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(solid),
            false,
            lease.Graph.GraphVersion + 1);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            gasOnly,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(
            new NavigationMediumStateRef(target, TraversalMedium.Gas));
        dispatcher.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Volume);
        dispatcher.CurrentCost.Should().Be((Fixed64)9);
    }

    [Fact]
    public void Dispatcher_ShouldKeepSolidOpenWhenGasIsClosed()
    {
        var targetIndex = new VoxelIndex(0, 1, 0);
        NavigationCell media = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            default,
            targetIndex,
            media,
            media);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        lease.Graph.TryGetSurfaceComponent(
                new NavigationCellAddress("map", default),
                TraversalMedium.Gas,
                out NavigationSurfaceComponentKey gas,
                out _)
            .Should().BeTrue();
        NavigationWorldGraph solidOnly = lease.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(gas),
            false,
            lease.Graph.GraphVersion + 1);
        solidOnly.TryGetNodeState(source, TraversalMedium.Solid, out _)
            .Should().BeTrue();
        solidOnly.TryGetNodeState(source, TraversalMedium.Gas, out _)
            .Should().BeFalse();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            solidOnly,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
    }

    [Fact]
    public void Dispatcher_ShouldCertifyRectangularDiagonalThroughCoveredUnion()
    {
        var targetIndex = new VoxelIndex(1, 0, 1);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (default(VoxelIndex), gas),
                (new VoxelIndex(1, 0, 0), gas),
                (new VoxelIndex(0, 0, 1), gas),
                (targetIndex, Cell(TraversalMedia.Gas, enterCost: (Fixed64)7))
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || !dispatcher.CurrentTarget.Node.Equals(target)));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(
            new NavigationMediumStateRef(target, TraversalMedium.Gas));
        NavigationDistanceMath.TryCeiling(
                Vector3d.Zero,
                new Vector3d((Fixed64)2, Fixed64.Zero, (Fixed64)4),
                out Fixed64 distance)
            .Should().BeTrue();
        dispatcher.CurrentCost.Should().Be(distance + (Fixed64)7);
        meter.CoveredVoxelIntervals.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Dispatcher_ShouldCeilRectangularThreeAxisShortcutCost()
    {
        var sourceIndex = new VoxelIndex(2, 2, 2);
        var targetIndex = new VoxelIndex(3, 3, 3);
        var cells = new (VoxelIndex Index, NavigationCell Cell)[8];
        int count = 0;
        for (int x = 2; x <= 3; x++)
        {
            for (int y = 2; y <= 3; y++)
            {
                for (int z = 2; z <= 3; z++)
                {
                    VoxelIndex index = new(x, y, z);
                    cells[count++] = (
                        index,
                        Cell(
                            TraversalMedia.Gas,
                            enterCost: index == targetIndex ? (Fixed64)7 : default));
                }
            }
        }
        using TrailblazerWorldContext context = CreateTopologyContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, sourceIndex);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        var workspace = new NavigationRayWorkspace(1, 16, 16, 64, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4096;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || !dispatcher.CurrentTarget.Node.Equals(target)));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        NavigationDistanceMath.TryCeiling(
                Vector3d.Zero,
                new Vector3d((Fixed64)2, (Fixed64)2, (Fixed64)2),
                out Fixed64 distance)
            .Should().BeTrue();
        dispatcher.CurrentCost.Should().Be(distance + (Fixed64)7);
        dispatcher.CurrentVolumeIsShortcut.Should().BeTrue();
    }

    [Fact]
    public void Dispatcher_ShouldOrderVolumeEdgesByDestinationAddress()
    {
        var sourceIndex = new VoxelIndex(1, 1, 1);
        var belowIndex = new VoxelIndex(1, 0, 1);
        var eastIndex = new VoxelIndex(2, 1, 1);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (sourceIndex, gas),
                (eastIndex, gas),
                (belowIndex, gas)
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, sourceIndex);
        NavigationNodeRef below = Resolve(lease.Graph, belowIndex);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Node.Should().Be(below);
        dispatcher.CurrentOrdinal.Should().Be(0);
    }

    [Fact]
    public void IncomingDispatcher_ShouldRecoverSolidForwardEdgeAndOrdinal()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Solid),
            Cell(TraversalMedia.Solid, enterCost: (Fixed64)7));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(target, TraversalMedium.Solid),
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: false);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = incoming.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        incoming.CurrentPredecessor.Should().Be(
            new NavigationMediumStateRef(source, TraversalMedium.Solid));
        incoming.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Surface);
        incoming.CurrentOrdinal.Should().Be(0);
        incoming.CurrentCost.Should().Be((Fixed64)9);
        incoming.CurrentTransitionSourceAction.Should().Be(Vector3d.Zero,
            "ordinary native edges must not expose a stale transition source action");
        incoming.CurrentTransitionDestinationAction.Should().Be(Vector3d.Zero,
            "ordinary native edges must not expose a stale transition destination action");
    }

    [Fact]
    public void IncomingDispatcher_ShouldRecoverVolumeForwardEdgeAndOrdinal()
    {
        var targetIndex = new VoxelIndex(0, 1, 0);
        using TrailblazerWorldContext context = CreateContext(
            default,
            targetIndex,
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas, enterCost: (Fixed64)7));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(target, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = incoming.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        incoming.CurrentPredecessor.Should().Be(
            new NavigationMediumStateRef(source, TraversalMedium.Gas));
        incoming.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Volume);
        incoming.CurrentOrdinal.Should().Be(0);
        incoming.CurrentCost.Should().Be((Fixed64)9);
    }

    [Fact]
    public void Dispatcher_ShouldUseAutomaticSeamForCrossMapVolumeFace()
    {
        using TrailblazerWorldContext context = CreateCrossMapContext();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var sourceAddress = new NavigationCellAddress("a-source", default);
        var targetAddress = new NavigationCellAddress("b-target", default);
        lease.Graph.TryGetMediumStateRef(
                sourceAddress,
                TraversalMedium.Gas,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                targetAddress,
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(target);
        dispatcher.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Volume);
        dispatcher.CurrentCost.Should().Be((Fixed64)8);

        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            target,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false);
        meter.Reset(Budget());
        workspace.Dependencies.Reset();
        remaining = 64;
        do
        {
            status = incoming.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);
        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        incoming.CurrentPredecessor.Should().Be(source);
        incoming.CurrentOrdinal.Should().Be(dispatcher.CurrentOrdinal);
        incoming.CurrentCost.Should().Be(dispatcher.CurrentCost);
    }

    [Theory]
    [InlineData("a-source", true)]
    [InlineData("z-source", false)]
    public void IncomingDispatcher_ShouldCanonicallyOrderNativeAndAutomaticSeamPredecessors(
        string seamSourceMapId,
        bool seamSortsFirst)
    {
        var nativeIndex = new VoxelIndex(1, 0, 0);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            new Vector3d(-1, 0, 0),
            Vector3d.Zero,
            TraversalMedia.Gas,
            seamSourceMapId,
            new Vector3d(1, 0, 0),
            nativeIndex);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var seamAddress = new NavigationCellAddress(seamSourceMapId, default);
        var nativeAddress = new NavigationCellAddress("b-target", nativeIndex);
        var expected = seamSortsFirst
            ? new[] { seamAddress, nativeAddress }
            : new[] { nativeAddress, seamAddress };
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("b-target", default),
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            target,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        for (int i = 0; i < expected.Length; i++)
        {
            NavigationTraversalEdgeAdvanceStatus status;
            do
            {
                status = incoming.AdvanceOne(
                    meter,
                    workspace.Dependencies,
                    ref remaining,
                    ref connectionRemaining);
            }
            while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

            status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
            incoming.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Volume);
            lease.Graph.TryGetNodeAddress(
                    incoming.CurrentPredecessor.Node,
                    out NavigationCellAddress actual)
                .Should().BeTrue();
            actual.Should().Be(expected[i]);
        }

        NavigationTraversalEdgeAdvanceStatus finalStatus;
        do
        {
            finalStatus = incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (finalStatus == NavigationTraversalEdgeAdvanceStatus.Pending);
        finalStatus.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
    }

    [Fact]
    public void Dispatcher_ShouldFailClosedForCrossMapNonFaceShortcut()
    {
        using TrailblazerWorldContext context = CreateCrossMapContext(
            new Vector3d(-1, 0, -1),
            Vector3d.Zero);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Gas,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
    }

    [Fact]
    public void Dispatcher_ShouldRecordWrongMediumCrossMapFaceDependency()
    {
        using TrailblazerWorldContext context = CreateCrossMapContext(
            new Vector3d(-1, 0, 0),
            Vector3d.Zero,
            TraversalMedia.Solid);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Gas,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        workspace.Dependencies.PageCount.Should().Be(2,
            "a later target-medium publication must stale the failed edge scan");
    }

    [Fact]
    public void Dispatcher_ShouldFallBackToUnionWhenBodySpansFacePrisms()
    {
        var sourceIndex = new VoxelIndex(1, 0, 1);
        var targetIndex = new VoxelIndex(2, 0, 1);
        var cells = new (VoxelIndex Index, NavigationCell Cell)[12];
        int cellCount = 0;
        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 3; z++)
            {
                VoxelIndex index = new(x, 0, z);
                cells[cellCount++] = (
                    index,
                    Cell(
                        TraversalMedia.Gas,
                        enterCost: index == targetIndex ? (Fixed64)7 : default));
            }
        }
        using TrailblazerWorldContext context = CreateTopologyContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, sourceIndex);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        var workspace = new NavigationRayWorkspace(1, 16, 16, 64, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(
                TraversalMedia.Gas,
                radius: Fixed64.FromFraction(3, 2)),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4096;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || !dispatcher.CurrentTarget.Node.Equals(target)));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentCost.Should().Be((Fixed64)9);
        meter.CoveredVoxelIntervals.Should().BeGreaterThan(0,
            "the profile is wider than the one-prism face portal");
    }

    [Fact]
    public void Dispatcher_ShouldRejectBlockedShortcutWitnessAndRecordItsPage()
    {
        var targetIndex = new VoxelIndex(1, 0, 1);
        var blockedWitness = new VoxelIndex(1, 0, 0);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (default(VoxelIndex), gas),
                (blockedWitness, Cell(
                    TraversalMedia.Gas,
                    requiredCapabilities: TraversalCapability.Fly)),
                (new VoxelIndex(0, 0, 1), gas),
                (targetIndex, gas)
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationNodeRef target = Resolve(lease.Graph, targetIndex);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4096;
        int connectionRemaining = int.MaxValue;
        bool emittedTarget = false;

        while (true)
        {
            NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                break;
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Edge);
            emittedTarget |= status == NavigationTraversalEdgeAdvanceStatus.Edge
                && dispatcher.CurrentTarget.Node.Equals(target);
        }

        emittedTarget.Should().BeFalse();
        workspace.BodyTraceCells.Should().Contain(cell =>
            cell.Cell.VoxelIndex == blockedWitness
            && cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage);
        workspace.Dependencies.PageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VolumeUnion_ShouldPinExactBudgetAndCapacityBoundaries()
    {
        var targetIndex = new VoxelIndex(1, 0, 1);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (default(VoxelIndex), gas),
                (new VoxelIndex(1, 0, 0), gas),
                (new VoxelIndex(0, 0, 1), gas),
                (targetIndex, gas)
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationMediumStateRef source = new(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        NavigationMediumStateRef target = new(
            Resolve(lease.Graph, targetIndex),
            TraversalMedium.Gas);
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas);
        var measuringWorkspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var measuringMeter = new NavigationWorkMeter(LargeBudget());
        var measuring = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Gas,
            measuringWorkspace);
        measuring.Evaluate(
                source,
                target,
                isPrimary: false,
                default,
                hasSeam: false,
                measuringMeter,
                measuringWorkspace.Dependencies,
                out _)
            .Should().Be(NavigationVolumeEdgeStatus.Passable);
        int lookupCount = measuringMeter.LookupProbes;
        int coveredCount = measuringMeter.CoveredVoxelIntervals;
        lookupCount.Should().Be(1,
            "one same-grid shortcut uses one swept-union trace");
        coveredCount.Should().BePositive();

        EvaluateUnion(
                context,
                lease.Graph,
                source,
                target,
                profile,
                mapCapacity: 1,
                coveredCapacity: coveredCount,
                lookupBudget: lookupCount,
                coveredBudget: coveredCount)
            .Should().Be(NavigationVolumeEdgeStatus.Passable);
        EvaluateUnion(
                context,
                lease.Graph,
                source,
                target,
                profile,
                mapCapacity: 1,
                coveredCapacity: coveredCount,
                lookupBudget: lookupCount,
                coveredBudget: coveredCount - 1)
            .Should().Be(NavigationVolumeEdgeStatus.BudgetExceeded);
        EvaluateUnion(
                context,
                lease.Graph,
                source,
                target,
                profile,
                mapCapacity: 1,
                coveredCapacity: coveredCount - 1,
                lookupBudget: lookupCount,
                coveredBudget: coveredCount)
            .Should().Be(NavigationVolumeEdgeStatus.CapacityExceeded);
    }

    [Fact]
    public void VolumeUnion_ShouldClassifyCombinedGuideWorkAsBudget()
    {
        var targetIndex = new VoxelIndex(1, 0, 1);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (default(VoxelIndex), gas),
                (new VoxelIndex(1, 0, 0), gas),
                (new VoxelIndex(0, 0, 1), gas),
                (targetIndex, gas)
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var source = new NavigationMediumStateRef(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        var target = new NavigationMediumStateRef(
            Resolve(lease.Graph, targetIndex),
            TraversalMedium.Gas);
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var measuringMeter = new NavigationWorkMeter(LargeBudget());
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Gas,
            workspace);
        evaluator.Evaluate(
                source,
                target,
                isPrimary: false,
                default,
                hasSeam: false,
                measuringMeter,
                workspace.Dependencies,
                out _)
            .Should().Be(NavigationVolumeEdgeStatus.Passable);
        int exactCombined = measuringMeter.LookupProbes
            + measuringMeter.CoveredVoxelIntervals;
        exactCombined.Should().BeGreaterThan(1);

        var exactMeter = new NavigationWorkMeter(LargeBudget());
        exactMeter.ResetForGuideSample(
            exactCombined,
            edgeAndConnectionLimit: 0,
            portalLimit: 0,
            prismLimit: 0,
            traceIntervalLimit: 0);
        evaluator.Evaluate(
                source,
                target,
                isPrimary: false,
                default,
                hasSeam: false,
                exactMeter,
                workspace.Dependencies,
                out _)
            .Should().Be(NavigationVolumeEdgeStatus.Passable);

        var oneBelowMeter = new NavigationWorkMeter(LargeBudget());
        oneBelowMeter.ResetForGuideSample(
            exactCombined - 1,
            edgeAndConnectionLimit: 0,
            portalLimit: 0,
            prismLimit: 0,
            traceIntervalLimit: 0);
        evaluator.Evaluate(
                source,
                target,
                isPrimary: false,
                default,
                hasSeam: false,
                oneBelowMeter,
                workspace.Dependencies,
                out _)
            .Should().Be(NavigationVolumeEdgeStatus.BudgetExceeded);
    }

    [Fact]
    public void VolumeDispatchers_ShouldDistinguishChunkExhaustionFromQueryBudget()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef sourceNode = Resolve(lease.Graph, default);
        NavigationNodeRef targetNode = Resolve(
            lease.Graph,
            new VoxelIndex(1, 0, 0));
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas);

        var outgoing = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas),
            profile,
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var availableMeter = new NavigationWorkMeter(Budget());
        int noEdgeSteps = 0;
        int connectionSteps = int.MaxValue;

        outgoing.AdvanceOne(
                availableMeter,
                workspace.Dependencies,
                ref noEdgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked);
        availableMeter.EvaluatedEdges.Should().Be(0);

        outgoing = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas),
            profile,
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var exhaustedMeter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: 0));
        noEdgeSteps = 0;
        outgoing.AdvanceOne(
                exhaustedMeter,
                workspace.Dependencies,
                ref noEdgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        exhaustedMeter.EvaluatedEdges.Should().Be(0);

        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
            profile,
            Policy,
            workspace,
            allowTransitions: false);
        noEdgeSteps = 0;
        incoming.AdvanceOne(
                availableMeter,
                workspace.Dependencies,
                ref noEdgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked);
        availableMeter.EvaluatedEdges.Should().Be(0);

        incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
            profile,
            Policy,
            workspace,
            allowTransitions: false);
        noEdgeSteps = 0;
        incoming.AdvanceOne(
                exhaustedMeter,
                workspace.Dependencies,
                ref noEdgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        exhaustedMeter.EvaluatedEdges.Should().Be(0);

        incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
            profile,
            Policy,
            workspace,
            allowTransitions: false);
        exhaustedMeter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: 0));
        int availableEdgeSteps = 64;
        incoming.AdvanceOne(
                exhaustedMeter,
                workspace.Dependencies,
                ref availableEdgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
                "query-meter exhaustion is independent of the caller's available chunk");
        availableEdgeSteps.Should().Be(64);
        exhaustedMeter.EvaluatedEdges.Should().Be(0);
    }

    [Fact]
    public void VolumeDispatcher_ShouldMapCertificationCapacityAndBudgetExactly()
    {
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(new[]
        {
            (default(VoxelIndex), gas),
            (new VoxelIndex(1, 0, 0), gas),
            (new VoxelIndex(0, 0, 1), gas),
            (new VoxelIndex(1, 0, 1), gas)
        });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var source = new NavigationMediumStateRef(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas);

        var capacityWorkspace = new NavigationRayWorkspace(1, 0, 0, 16, 0);
        var capacityMeter = new NavigationWorkMeter(Budget());
        NavigationTraversalEdgeAdvanceStatus capacity = AdvanceVolumeDispatcher(
            context,
            lease.Graph,
            source,
            profile,
            capacityWorkspace,
            capacityMeter);

        capacity.Should().Be(NavigationTraversalEdgeAdvanceStatus.CapacityExceeded);
        capacityMeter.EvaluatedEdges.Should().Be(1);
        capacityMeter.VolumeUnionChecks.Should().Be(0,
            "page ownership must be retained before physical certification starts");

        var budgetWorkspace = new NavigationRayWorkspace(1, 8, 0, 16, 0);
        var budgetMeter = new NavigationWorkMeter(
            Budget(maxCoveredVoxelIntervals: 0));
        NavigationTraversalEdgeAdvanceStatus budget = AdvanceVolumeDispatcher(
            context,
            lease.Graph,
            source,
            profile,
            budgetWorkspace,
            budgetMeter);

        budget.Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        budgetMeter.EvaluatedEdges.Should().Be(3,
            "two face neighbors precede the canonical diagonal shortcut");
        budgetMeter.PrimaryVolumeCandidates.Should().Be(2);
        budgetMeter.ShortcutVolumeCandidates.Should().Be(1);
        budgetMeter.VolumeUnionChecks.Should().Be(1);
        budgetMeter.CoveredVoxelIntervals.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VolumeRayCertification_ShouldRejectEitherBlockedEndpointBeforeTracing(
        bool blockSource)
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas));
        VoxelGrid grid = context.World.ActiveGrids[0];
        var targetIndex = new VoxelIndex(1, 0, 0);
        VoxelIndex blockedIndex = blockSource ? default : targetIndex;
        grid.TryGetVoxel(blockedIndex, out Voxel? blockedVoxel).Should().BeTrue();
        grid.TryAddObstacle(blockedVoxel!, context.World.AllocateObstacleToken())
            .Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef sourceNode = Resolve(lease.Graph, default);
        NavigationNodeRef targetNode = Resolve(lease.Graph, targetIndex);
        lease.Graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        lease.Graph.TryGetNodeState(targetNode, out NavigationNodeState targetState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(Fixed64.One, out Vector3d sourceFoot)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(Fixed64.One, out Vector3d targetFoot)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas),
            Policy,
            TraversalMedium.Gas,
            workspace);

        evaluator.CertifyRaySegment(
                new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas),
                new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
                sourceFoot,
                targetFoot,
                meter,
                workspace.Dependencies)
            .Should().Be(NavigationVolumeEdgeStatus.Impassable);
        meter.VolumeUnionChecks.Should().Be(0);
    }

    [Fact]
    public void VolumeEvaluation_ShouldRejectCrossMediumEdgesBeforeRecordingDependencies()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas | TraversalMedia.Liquid),
            Cell(TraversalMedia.Gas | TraversalMedia.Liquid));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef sourceNode = Resolve(lease.Graph, default);
        NavigationNodeRef targetNode = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
        var source = new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas);
        var target = new NavigationMediumStateRef(targetNode, TraversalMedium.Liquid);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas | TraversalMedia.Liquid),
            Policy,
            TraversalMedium.Gas,
            workspace);

        evaluator.Evaluate(
                source,
                target,
                isPrimary: true,
                default,
                hasSeam: false,
                meter,
                workspace.Dependencies,
                out Fixed64 cost)
            .Should().Be(NavigationVolumeEdgeStatus.Stale);
        evaluator.CertifyRaySegment(
                source,
                target,
                Vector3d.Zero,
                Vector3d.Zero,
                meter,
                workspace.Dependencies)
            .Should().Be(NavigationVolumeEdgeStatus.Stale);
        cost.Should().Be(Fixed64.Zero);
        workspace.Dependencies.PageCount.Should().Be(0);
        meter.VolumeUnionChecks.Should().Be(0);
    }

    [Fact]
    public void VolumeAnchor_PageCapacityShouldFailBeforePublishingPartialDependencies()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas | TraversalMedia.Liquid),
            Cell(TraversalMedia.Gas | TraversalMedia.Liquid));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef sourceNode = Resolve(lease.Graph, default);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationVolumeAnchorEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas | TraversalMedia.Liquid),
            Policy,
            workspace);
        var meter = new NavigationWorkMeter(Budget());

        var noDependencies = new NavigationDependencyWorkspace(0, 0);
        evaluator.Evaluate(
                sourceNode,
                TraversalMedia.Gas,
                meter,
                noDependencies,
                out _,
                out _)
            .Should().Be(NavigationVolumeAnchorStatus.CapacityExceeded);
        noDependencies.PageCount.Should().Be(0);
        meter.VolumeUnionChecks.Should().Be(1);
    }

    [Fact]
    public void VolumeAnchor_ShouldRejectBodyCoverageFromAnUnpublishedGrid()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration mappedConfiguration = new(
            new Vector3d(-Fixed64.One, Fixed64.Zero, -Fixed64.One),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        GridConfiguration unpublishedConfiguration = new(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex sourceIndex = new(1, 0, 1);
        VoxelIndex[] mappedIndices =
        {
            new(0, 0, 0), new(0, 0, 1), new(0, 0, 2),
            new(1, 0, 0), sourceIndex, new(1, 0, 2),
            new(2, 0, 0), new(2, 0, 2)
        };
        context.World.TryAddGrid(
                mappedConfiguration,
                mappedIndices,
                out _)
            .Should().BeTrue();
        context.World.TryAddGrid(
                unpublishedConfiguration,
                new[] { default(VoxelIndex) },
                out _)
            .Should().BeTrue();
        mappedConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var mapBuilder = new NavigationMapBuilder("mapped", binding);
        for (int i = 0; i < mappedIndices.Length; i++)
            mapBuilder.AddCell(mappedIndices[i], Cell(TraversalMedia.Gas));
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                mapBuilder.Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        while (operation.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("mapped", sourceIndex),
                out NavigationNodeRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 10, 10, 16, 0);
        var evaluator = new NavigationVolumeAnchorEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas, radius: Fixed64.FromFraction(3, 4)),
            Policy,
            workspace);
        var meter = new NavigationWorkMeter(Budget());
        var dependencies = new NavigationDependencyWorkspace(8, 0);

        evaluator.Evaluate(
                source,
                TraversalMedia.Gas,
                meter,
                dependencies,
                out _,
                out _)
            .Should().Be(NavigationVolumeAnchorStatus.Stale);
        dependencies.PageCount.Should().Be(1,
            "the mapped part of the body union is recorded before the unpublished alternative is rejected");
        meter.VolumeUnionChecks.Should().Be(1);
    }

    [Fact]
    public void VolumeAnchor_ShouldRejectCoverageFromANewerMappedGridSequence()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMap("map", out NavigationMapInstance? retained).Should().BeTrue();
        NavigationNodeRef source = Resolve(lease.Graph, default);
        VoxelGrid grid = context.World.ActiveGrids[retained!.GridIdentity.GridIndex];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationVolumeAnchorEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace);
        var meter = new NavigationWorkMeter(Budget());
        var dependencies = new NavigationDependencyWorkspace(8, 0);

        evaluator.Evaluate(
                source,
                TraversalMedia.Gas,
                meter,
                dependencies,
                out _,
                out _)
            .Should().Be(NavigationVolumeAnchorStatus.Stale,
                "a retained graph cannot accept body coverage from a newer mapped-grid sequence");
        dependencies.PageCount.Should().Be(0,
            "generation validation precedes dependency publication");
        meter.VolumeUnionChecks.Should().Be(1);
    }

    [Fact]
    public void VolumeEvaluation_ShouldRejectBeforeTracingWhenPageCapacityIsZero()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationMediumStateRef source = new(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        NavigationMediumStateRef target = new(
            Resolve(lease.Graph, new VoxelIndex(1, 0, 0)),
            TraversalMedium.Gas);
        var workspace = new NavigationRayWorkspace(1, 0, 0, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas),
            Policy,
            TraversalMedium.Gas,
            workspace);

        evaluator.Evaluate(
                source,
                target,
                isPrimary: true,
                default,
                hasSeam: false,
                meter,
                workspace.Dependencies,
                out Fixed64 cost)
            .Should().Be(NavigationVolumeEdgeStatus.CapacityExceeded);
        evaluator.CertifyRaySegment(
                source,
                target,
                Vector3d.Zero,
                Vector3d.Zero,
                meter,
                workspace.Dependencies)
            .Should().Be(NavigationVolumeEdgeStatus.CapacityExceeded);
        cost.Should().Be(Fixed64.Zero);
        workspace.Dependencies.PageCount.Should().Be(0);
        meter.VolumeUnionChecks.Should().Be(0);
    }

    [Fact]
    public void VolumeEvaluation_ShouldReportCheckedEnterCostOverflow()
    {
        using TrailblazerWorldContext context = CreateContext(
            Cell(TraversalMedia.Gas),
            Cell(TraversalMedia.Gas, enterCost: Fixed64.MaxValue));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationMediumStateRef source = new(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        NavigationMediumStateRef target = new(
            Resolve(lease.Graph, new VoxelIndex(1, 0, 0)),
            TraversalMedium.Gas);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var costEvaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas),
            Policy,
            TraversalMedium.Gas,
            workspace);
        costEvaluator.Evaluate(
                source,
                target,
                isPrimary: true,
                default,
                hasSeam: false,
                meter,
                workspace.Dependencies,
                out Fixed64 traversalCost)
            .Should().Be(NavigationVolumeEdgeStatus.CostOverflow);
        traversalCost.Should().Be(Fixed64.Zero);
        workspace.Dependencies.PageCount.Should().Be(1);

        workspace.Reset();
        meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;
        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.CostOverflow);
        dispatcher.CurrentTarget.IsValid.Should().BeFalse();
        meter.EvaluatedEdges.Should().Be(1);
        workspace.Dependencies.PageCount.Should().Be(1);
    }

    [Fact]
    public void VolumeEvaluation_ShouldRejectAnUnrepresentableCenteredFootAnchor()
    {
        Fixed64 centerY = Fixed64.MinValue + Fixed64.One;
        var minimum = new Vector3d(Fixed64.Zero, centerY, Fixed64.Zero);
        var maximum = new Vector3d(Fixed64.One, centerY, Fixed64.Zero);
        GridConfiguration configuration = new(
            minimum,
            maximum,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(1, 0, 0);
        context.World.TryAddGrid(configuration, new[] { sourceIndex, targetIndex }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationCell gas = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.MaxValue);
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("volume-anchor-overflow", binding)
                    .AddCell(sourceIndex, gas)
                    .AddCell(targetIndex, gas)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        while (operation.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("volume-anchor-overflow", sourceIndex),
                out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("volume-anchor-overflow", targetIndex),
                out NavigationNodeRef targetNode)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Gas,
            workspace);

        evaluator.Evaluate(
                new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas),
                new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
                isPrimary: true,
                default,
                hasSeam: false,
                new NavigationWorkMeter(Budget()),
                workspace.Dependencies,
                out Fixed64 cost)
            .Should().Be(NavigationVolumeEdgeStatus.CostOverflow);
        cost.Should().Be(Fixed64.Zero,
            "an unrepresentable body anchor must fail before any cost is published");
        lease.Graph.TryGetNodeState(
                sourceNode,
                TraversalMedium.Gas,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        lease.Graph.TryGetNodeState(
                targetNode,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        evaluator.CertifyRaySegment(
                new NavigationMediumStateRef(sourceNode, TraversalMedium.Gas),
                new NavigationMediumStateRef(targetNode, TraversalMedium.Gas),
                sourceState.FootAnchor,
                targetState.FootAnchor,
                new NavigationWorkMeter(Budget()),
                workspace.Dependencies)
            .Should().Be(NavigationVolumeEdgeStatus.CostOverflow,
                "the same legal endpoints must preserve checked anchor overflow during ray certification");
    }

    [Fact]
    public void WarmedVolumeUnion_ShouldAllocateZeroBytes()
    {
        var targetIndex = new VoxelIndex(1, 0, 1);
        NavigationCell gas = Cell(TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            new[]
            {
                (default(VoxelIndex), gas),
                (new VoxelIndex(1, 0, 0), gas),
                (new VoxelIndex(0, 0, 1), gas),
                (targetIndex, gas)
            });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationMediumStateRef source = new(
            Resolve(lease.Graph, default),
            TraversalMedium.Gas);
        NavigationMediumStateRef target = new(
            Resolve(lease.Graph, targetIndex),
            TraversalMedium.Gas);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(LargeBudget());
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Gas),
            Policy,
            TraversalMedium.Gas,
            workspace);
        NavigationVolumeEdgeStatus result = default;
        Action evaluate = () =>
        {
            workspace.Reset();
            meter.Reset(LargeBudget());
            result = evaluator.Evaluate(
                source,
                target,
                isPrimary: false,
                default,
                hasSeam: false,
                meter,
                workspace.Dependencies,
                out _);
        };
        evaluate();

        AllocationTestUtility.MeasureAllocatedBytes(evaluate).Should().Be(0);
        result.Should().Be(NavigationVolumeEdgeStatus.Passable);
    }

    [Theory]
    [InlineData((int)GridTopologyKind.RectangularPrism, (int)HexOrientation.PointyTop, 6, 20)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.PointyTop, 8, 12)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.FlatTop, 8, 12)]
    public void Dispatcher_ShouldUseGridForgeCompleteDirectionSets(
        int topologyValue,
        int orientationValue,
        int expectedFaces,
        int expectedShortcuts)
    {
        GridTopologyKind topology = (GridTopologyKind)topologyValue;
        HexOrientation orientation = (HexOrientation)orientationValue;
        VoxelIndex sourceIndex;
        if (topology == GridTopologyKind.HexPrism)
        {
            GridConfiguration probe = new(
                Vector3d.Zero,
                new Vector3d(12, 12, 12),
                topologyKind: topology,
                topologyMetrics: GridTopologyMetrics.Hex(
                    (Fixed64)2,
                    (Fixed64)2,
                    orientation),
                storageKind: GridStorageKind.Sparse);
            probe.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            sourceIndex = FindCompleteHexCenter(binding);
        }
        else
        {
            sourceIndex = new VoxelIndex(2, 2, 2);
        }
        int directionCount = topology == GridTopologyKind.HexPrism
            ? HexDirectionUtility.Offsets.Length
            : RectangularDirectionUtility.Offsets.Length;
        var cells = new (VoxelIndex Index, NavigationCell Cell)[directionCount + 1];
        cells[0] = (sourceIndex, Cell(TraversalMedia.Gas));
        for (int i = 0; i < directionCount; i++)
        {
            VoxelIndex offset;
            if (topology == GridTopologyKind.HexPrism)
            {
                offset = HexDirectionUtility.Offsets[i];
            }
            else
            {
                (int x, int y, int z) value = RectangularDirectionUtility.Offsets[i];
                offset = new VoxelIndex(value.x, value.y, value.z);
            }
            cells[i + 1] = (
                new VoxelIndex(
                    sourceIndex.x + offset.x,
                    sourceIndex.y + offset.y,
                    sourceIndex.z + offset.z),
                Cell(TraversalMedia.Gas));
        }

        using TrailblazerWorldContext context = CreateTopologyContext(
            topology,
            orientation,
            cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, sourceIndex);
        var workspace = new NavigationRayWorkspace(1, 16, 16, 64, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            new NavigationMediumStateRef(source, TraversalMedium.Gas),
            Profile(TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4096;
        int connectionRemaining = int.MaxValue;
        int faces = 0;
        int shortcuts = 0;

        while (true)
        {
            NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                break;
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Edge);
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                continue;
            if (dispatcher.CurrentVolumeIsShortcut)
                shortcuts++;
            else
                faces++;
        }

        faces.Should().Be(expectedFaces);
        shortcuts.Should().Be(expectedShortcuts);
        meter.PrimaryVolumeCandidates.Should().Be(expectedFaces);
        meter.ShortcutVolumeCandidates.Should().Be(expectedShortcuts);
        meter.VolumeUnionChecks.Should().BeGreaterThan(0);
        meter.CoveredVoxelIntervals.Should().BeGreaterThan(0);

        meter.Reset(LargeBudget());
        meter.PrimaryVolumeCandidates.Should().Be(0);
        meter.ShortcutVolumeCandidates.Should().Be(0);
        meter.VolumeUnionChecks.Should().Be(0);
    }

    private static TrailblazerWorldContext CreateContext(
        NavigationCell source,
        NavigationCell target) => CreateContext(
        default,
        new VoxelIndex(1, 0, 0),
        source,
        target);

    private static TrailblazerWorldContext CreateContext(
        VoxelIndex sourceIndex,
        VoxelIndex targetIndex,
        NavigationCell source,
        NavigationCell target) => CreateContext(
        new[] { (sourceIndex, source), (targetIndex, target) });

    private static TrailblazerWorldContext CreateContext(
        (VoxelIndex Index, NavigationCell Cell)[] cells)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(4, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)2,
                    (Fixed64)4),
                storageKind: GridStorageKind.Sparse);
            context.World.TryAddGrid(
                    configuration,
                    Array.ConvertAll(cells, static cell => cell.Index),
                    out _)
                .Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < cells.Length; i++)
                builder.AddCell(cells[i].Index, cells[i].Cell);
            NavigationMap map = builder.Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateCrossMapContext() =>
        CreateCrossMapContext(
            new Vector3d(-1, 0, 0),
            Vector3d.Zero,
            TraversalMedia.Gas);

    private static TrailblazerWorldContext CreateCrossMapContext(
        Vector3d sourcePosition,
        Vector3d targetPosition) => CreateCrossMapContext(
        sourcePosition,
        targetPosition,
        TraversalMedia.Gas);

    private static TrailblazerWorldContext CreateCrossMapContext(
        Vector3d sourcePosition,
        Vector3d targetPosition,
        TraversalMedia targetMedia) => CreateCrossMapContext(
        sourcePosition,
        targetPosition,
        targetMedia,
        "a-source",
        targetPosition,
        additionalTargetIndex: null);

    private static TrailblazerWorldContext CreateCrossMapContext(
        Vector3d sourcePosition,
        Vector3d targetPosition,
        TraversalMedia targetMedia,
        string sourceMapId,
        Vector3d targetMaximum,
        VoxelIndex? additionalTargetIndex)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration sourceConfiguration = new(
                sourcePosition,
                sourcePosition,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            GridConfiguration targetConfiguration = new(
                targetPosition,
                targetMaximum,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
            context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
            sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
                .Should().BeTrue();
            targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
                .Should().BeTrue();
            var targetBuilder = new NavigationMapBuilder("b-target", targetBinding)
                .AddCell(default, Cell(targetMedia, enterCost: (Fixed64)7));
            if (additionalTargetIndex.HasValue)
            {
                targetBuilder.AddCell(
                    additionalTargetIndex.Value,
                    Cell(targetMedia, enterCost: (Fixed64)7));
            }
            NavigationMap[] maps =
            {
                new NavigationMapBuilder(sourceMapId, sourceBinding)
                    .AddCell(default, Cell(TraversalMedia.Gas))
                    .Build(),
                targetBuilder.Build()
            };
            var receipts = new NavigationOperationReceipt[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                var operation = new NavigationMapCommitOperation(
                    new PreparedNavigationMap(maps[i], bakeVersion: 1),
                    OverlayReplacementPolicy.Clear,
                    operationSequence: i + 1,
                    effectiveFrame: context.FrameCount + 1);
                context.Pathing.Admit(operation).Should().BeTrue();
                receipts[i] = operation.Receipt;
            }
            for (int frame = 0;
                frame < 256 && receipts[1].Status == NavigationOperationStatus.Pending;
                frame++)
            {
                context.Simulate();
            }
            receipts.Should().OnlyContain(
                receipt => receipt.Status == NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateTopologyContext(
        GridTopologyKind topology,
        HexOrientation orientation,
        (VoxelIndex Index, NavigationCell Cell)[] cells)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridTopologyMetrics metrics = topology == GridTopologyKind.HexPrism
                ? GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation)
                : GridTopologyMetrics.Rectangular((Fixed64)2);
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(12, 12, 12),
                topologyKind: topology,
                topologyMetrics: metrics,
                storageKind: GridStorageKind.Sparse);
            context.World.TryAddGrid(
                    configuration,
                    Array.ConvertAll(cells, static cell => cell.Index),
                    out _)
                .Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < cells.Length; i++)
                builder.AddCell(cells[i].Index, cells[i].Cell);
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static VoxelIndex FindCompleteHexCenter(
        NormalizedGridConfiguration binding)
    {
        for (int y = 1; y < binding.Height - 1; y++)
        {
            for (int q = 1; q < binding.Width - 1; q++)
            {
                for (int r = 1; r < binding.Length - 1; r++)
                {
                    var candidate = new VoxelIndex(q, y, r);
                    bool complete = binding.IsValidIndex(candidate);
                    for (int i = 0; complete && i < HexDirectionUtility.Offsets.Length; i++)
                    {
                        VoxelIndex offset = HexDirectionUtility.Offsets[i];
                        complete = binding.IsValidIndex(new VoxelIndex(
                            candidate.x + offset.x,
                            candidate.y + offset.y,
                            candidate.z + offset.z));
                    }
                    if (complete)
                        return candidate;
                }
            }
        }
        throw new InvalidOperationException("The test configuration has no complete hex neighborhood.");
    }

    private static NavigationCell Cell(
        TraversalMedia media,
        Fixed64 enterCost = default,
        TraversalCapability requiredCapabilities = TraversalCapability.None) => new(
        media,
        requiredCapabilities,
        default,
        enterCost,
        (Fixed64)4,
        (Fixed64)4);

    private static NavigationAgentProfile Profile(
        TraversalMedia media,
        TraversalCapability capabilities = TraversalCapability.None,
        Fixed64 radius = default,
        Fixed64 height = default) => new(
        new KinematicBodyShape(
            radius == Fixed64.Zero ? Fixed64.Half : radius,
            height == Fixed64.Zero ? Fixed64.One : height,
            Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        media,
        capabilities);

    private static NavigationWorkBudget Budget(
        int maxEvaluatedEdges = 64,
        int maxCoveredVoxelIntervals = 64) => new(
        maxLookupProbes: 64,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges,
        maxConnectionLegs: 64,
        maxTransitionCandidates: 64,
        maxTransitionPairs: 64,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals,
        maxSimplificationRays: 0);

    private static NavigationTraversalEdgeAdvanceStatus AdvanceVolumeDispatcher(
        TrailblazerWorldContext context,
        NavigationWorldGraph graph,
        NavigationMediumStateRef source,
        NavigationAgentProfile profile,
        NavigationRayWorkspace workspace,
        NavigationWorkMeter meter)
    {
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            graph,
            source,
            profile,
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;
        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status is NavigationTraversalEdgeAdvanceStatus.Pending
            or NavigationTraversalEdgeAdvanceStatus.Edge);
        return status;
    }

    private static NavigationWorkBudget LargeBudget() => new(
        maxLookupProbes: 4_096,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 4_096,
        maxConnectionLegs: 4_096,
        maxTransitionCandidates: 4_096,
        maxTransitionPairs: 4_096,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 4_096,
        maxSimplificationRays: 0);

    private static NavigationVolumeEdgeStatus EvaluateUnion(
        TrailblazerWorldContext context,
        NavigationWorldGraph graph,
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        NavigationAgentProfile profile,
        int mapCapacity,
        int coveredCapacity,
        int lookupBudget,
        int coveredBudget)
    {
        var workspace = new NavigationRayWorkspace(
            mapCapacity,
            8,
            8,
            coveredCapacity,
            0);
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: lookupBudget,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: coveredBudget,
            maxSimplificationRays: 0));
        var evaluator = new NavigationVolumeEdgeEvaluator(
            context.World,
            graph,
            profile,
            Policy,
            TraversalMedium.Gas,
            workspace);
        return evaluator.Evaluate(
            source,
            target,
            isPrimary: false,
            default,
            hasSeam: false,
            meter,
            workspace.Dependencies,
            out _);
    }

    private static NavigationNodeRef Resolve(
        NavigationWorldGraph graph,
        VoxelIndex index)
    {
        graph.TryGetNodeRef(0, index, out NavigationNodeRef node).Should().BeTrue();
        return node;
    }

    private static NavigationGraphEdge FindSurfaceEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        NavigationNodeRef target)
    {
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(source);
        while (edges.MoveNext())
        {
            if (edges.Current.Target.Equals(target))
                return edges.Current;
        }
        throw new Xunit.Sdk.XunitException("Expected native surface edge was not enumerated.");
    }
}
