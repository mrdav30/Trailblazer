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
                || dispatcher.CurrentTarget.Node != target));

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
                || dispatcher.CurrentTarget.Node != target));

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
                || dispatcher.CurrentTarget.Node != target));

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
                && dispatcher.CurrentTarget.Node == target;
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
        meter.CoveredVoxelIntervals.Should().BeGreaterThan(0);
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
        TraversalMedia targetMedia)
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
                targetPosition,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
            context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
            sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
                .Should().BeTrue();
            targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
                .Should().BeTrue();
            NavigationMap[] maps =
            {
                new NavigationMapBuilder("a-source", sourceBinding)
                    .AddCell(default, Cell(TraversalMedia.Gas))
                    .Build(),
                new NavigationMapBuilder("b-target", targetBinding)
                    .AddCell(default, Cell(targetMedia, enterCost: (Fixed64)7))
                    .Build()
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

    private static NavigationWorkBudget Budget() => new(
        maxLookupProbes: 64,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 64,
        maxConnectionLegs: 64,
        maxTransitionCandidates: 64,
        maxTransitionPairs: 64,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 64,
        maxSimplificationRays: 0);

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
            if (edges.Current.Target == target)
                return edges.Current;
        }
        throw new Xunit.Sdk.XunitException("Expected native surface edge was not enumerated.");
    }
}
