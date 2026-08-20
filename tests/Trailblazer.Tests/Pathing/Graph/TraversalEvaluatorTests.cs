//=======================================================================
// TraversalEvaluatorTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
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
public sealed class TraversalEvaluatorTests
{
    private static readonly NavigationAreaPolicy DefaultPolicy = CreatePolicy(
        default(NavigationAreaRule));

    [Fact]
    public void NavigationDistanceMath_ShouldBoundFractionalDistanceExactly()
    {
        Vector3d start = Vector3d.Zero;
        var end = new Vector3d(Fixed64.One, Fixed64.One, Fixed64.Zero);

        NavigationDistanceMath.TryFloor(start, end, out Fixed64 floor).Should().BeTrue();
        NavigationDistanceMath.TryCeiling(start, end, out Fixed64 ceiling).Should().BeTrue();

        Vector3d floorScalar = new(floor, Fixed64.Zero, Fixed64.Zero);
        Vector3d ceilingScalar = new(ceiling, Fixed64.Zero, Fixed64.Zero);
        Vector3d.CompareDistanceSquared(start, end, Vector3d.Zero, floorScalar)
            .Should().BeGreaterThanOrEqualTo(0);
        Vector3d.CompareDistanceSquared(start, end, Vector3d.Zero, ceilingScalar)
            .Should().BeLessThanOrEqualTo(0);
        Fixed64.TrySubtract(ceiling, floor, out Fixed64 width).Should().BeTrue();
        width.Should().Be(Fixed64.MinIncrement);
    }

    [Fact]
    public void EvaluateEdge_ShouldUseExactTargetEnterCostsAndRemainDirectionAsymmetric()
    {
        NavigationCell west = Cell(enterCost: Fixed64.One);
        NavigationCell east = Cell(area: new NavigationAreaId(1), enterCost: (Fixed64)7);
        using TrailblazerWorldContext context = CreateContext(west, east);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef westNode = Resolve(lease.Graph, default);
        NavigationNodeRef eastNode = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
        NavigationGraphEdge westToEast = FindEdge(lease.Graph, westNode, eastNode);
        NavigationGraphEdge eastToWest = FindEdge(lease.Graph, eastNode, westNode);
        var policy = CreatePolicy(
            default,
            new NavigationAreaRule(isAllowed: true, additionalEnterCost: (Fixed64)3));
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            Profile(maxStepUp: Fixed64.Zero, maxDropDown: Fixed64.Zero),
            policy,
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(westNode, westToEast, out TraversalEdgeEvidence forward)
            .Should().Be(TraversalEvaluationStatus.Passable);
        evaluator.EvaluateEdge(eastNode, eastToWest, out TraversalEdgeEvidence reverse)
            .Should().Be(TraversalEvaluationStatus.Passable);

        // The anisotropic cells are two world units apart; native portal legs sum to two.
        forward.Cost.Should().Be((Fixed64)12);
        reverse.Cost.Should().Be((Fixed64)3);
        westToEast.NativePortal.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EvaluateEdge_ShouldReportStaleForAnUncertifiedNativePortal()
    {
        using TrailblazerWorldContext context = CreateContext(Cell(), Cell());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationGraphEdge current = FindEdge(
            lease.Graph,
            source,
            Resolve(lease.Graph, new VoxelIndex(1, 0, 0)));
        var uncertified = new NavigationGraphEdge(
            current.Target,
            NavigationGraphEdgeKind.Native,
            default,
            current.NativeDirectionOrdinal);
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            Profile(),
            DefaultPolicy,
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(source, uncertified, out _)
            .Should().Be(TraversalEvaluationStatus.Stale);
    }

    [Fact]
    public void EvaluateEdge_ShouldReportStaleForAnUncertifiedAutomaticSeam()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef source)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef target)
            .Should().BeTrue();
        var pair = new NavigationAutomaticSeamPair(
            sourceAddress,
            targetAddress,
            default);
        var edge = new NavigationGraphEdge(
            target,
            new NavigationAutomaticSeamRef(pair, reverse: false));
        var evaluator = new TraversalEvaluator(
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.Policy,
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(source, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Stale);
    }

    [Fact]
    public void NodePassability_ShouldRequireExactMediumAllCapabilitiesAndDirectAreaRule()
    {
        NavigationCell source = Cell(media: TraversalMedia.Solid | TraversalMedia.Liquid);
        NavigationCell target = Cell(
            media: TraversalMedia.Solid | TraversalMedia.Liquid,
            capabilities: TraversalCapability.Jump | TraversalCapability.Climb,
            area: new NavigationAreaId(1));
        using TrailblazerWorldContext context = CreateContext(source, target);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef targetNode = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
        var allowed = CreatePolicy(default, new NavigationAreaRule(true, Fixed64.Zero));
        var denied = CreatePolicy(default, new NavigationAreaRule(false, Fixed64.Zero));

        new TraversalEvaluator(
                lease.Graph,
                Profile(
                    allowedMedia: TraversalMedia.Solid,
                    capabilities: TraversalCapability.Jump | TraversalCapability.Climb),
                allowed,
                TraversalMedium.Liquid)
            .TryGetPassableNodeState(targetNode, out _).Should().BeFalse();
        new TraversalEvaluator(
                lease.Graph,
                Profile(
                    allowedMedia: TraversalMedia.Liquid,
                    capabilities: TraversalCapability.Jump),
                allowed,
                TraversalMedium.Liquid)
            .TryGetPassableNodeState(targetNode, out _).Should().BeFalse();
        new TraversalEvaluator(
                lease.Graph,
                Profile(
                    allowedMedia: TraversalMedia.Liquid,
                    capabilities: TraversalCapability.Jump | TraversalCapability.Climb),
                allowed,
                TraversalMedium.Liquid)
            .TryGetPassableNodeState(targetNode, out _).Should().BeTrue();
        new TraversalEvaluator(
                lease.Graph,
                Profile(
                    allowedMedia: TraversalMedia.Liquid,
                    capabilities: TraversalCapability.Jump | TraversalCapability.Climb),
                denied,
                TraversalMedium.Liquid)
            .TryGetPassableNodeState(targetNode, out _).Should().BeFalse();
        new TraversalEvaluator(
                lease.Graph,
                Profile(
                    allowedMedia: TraversalMedia.Liquid,
                    capabilities: TraversalCapability.Jump | TraversalCapability.Climb),
                DefaultPolicy,
                TraversalMedium.Liquid)
            .TryGetPassableNodeState(targetNode, out _).Should().BeFalse();

        NavigationNodeRef sourceNode = Resolve(lease.Graph, default);
        new TraversalEvaluator(
                lease.Graph,
                Profile(allowedMedia: TraversalMedia.Solid),
                DefaultPolicy,
                TraversalMedium.Solid)
            .TryGetPassableNodeState(sourceNode, out _).Should().BeTrue();
    }

    [Fact]
    public void NodePassability_ShouldRejectStructuralClosureDormancyAbsenceSuppressionAndBlockage()
    {
        NavigationCell cell = Cell();
        using (TrailblazerWorldContext context = CreateContext(cell, cell))
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationNodeRef target = Resolve(lease.Graph, new VoxelIndex(1, 0, 0));
            lease.Graph.TryGetSurfaceComponent(
                    new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                    out NavigationSurfaceComponentKey componentKey,
                    out _)
                .Should().BeTrue();
            NavigationWorldGraph closed = lease.Graph.WithClosedStructuralComponents(
                NavigationSurfaceComponentKeySet.Empty.Add(componentKey),
                false,
                lease.Graph.GraphVersion + 1);
            new TraversalEvaluator(closed, Profile(), DefaultPolicy, TraversalMedium.Solid)
                .TryGetPassableNodeState(target, out _).Should().BeFalse();

            VoxelGrid grid = context.World.ActiveGrids[0];
            grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
            context.Simulate();
            using NavigationWorldGraphLease blockedLease = context.Pathing.TryAcquireNavigationGraph()!;
            new TraversalEvaluator(blockedLease.Graph, Profile(), DefaultPolicy, TraversalMedium.Solid)
                .TryGetPassableNodeState(
                    Resolve(blockedLease.Graph, new VoxelIndex(1, 0, 0)),
                    out _).Should().BeFalse();
        }

        using (TrailblazerWorldContext context = CreateContext(cell, cell, targetPhysicallyPresent: false))
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            new TraversalEvaluator(lease.Graph, Profile(), DefaultPolicy, TraversalMedium.Solid)
                .TryGetPassableNodeState(
                    Resolve(lease.Graph, new VoxelIndex(1, 0, 0)),
                    out _).Should().BeFalse();
        }

        using (TrailblazerWorldContext context = CreateContext(cell, cell))
        {
            CommitCellOverlay(context, NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0)));
            using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
            new TraversalEvaluator(lease.Graph, Profile(), DefaultPolicy, TraversalMedium.Solid)
                .TryGetPassableNodeState(
                    Resolve(lease.Graph, new VoxelIndex(1, 0, 0)),
                    out _).Should().BeFalse();
        }

        using (TrailblazerWorldContext context = CreateContext(cell, cell))
        {
            ushort gridIndex = context.World.ActiveGrids[0].GridIndex;
            context.World.TryRemoveGrid(gridIndex).Should().BeTrue();
            context.Simulate();
            using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
            new TraversalEvaluator(lease.Graph, Profile(), DefaultPolicy, TraversalMedium.Solid)
                .TryGetPassableNodeState(
                    Resolve(lease.Graph, new VoxelIndex(1, 0, 0)),
                    out _).Should().BeFalse();
        }
    }

    [Fact]
    public void NativeClearance_ShouldUseInclusiveCellPortalAndPlanarStepDropLimits()
    {
        GridConfiguration configuration = CreateConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap geometryMap = new NavigationMapBuilder("geometry", binding).Build();
        GridNavigationPortal portal = geometryMap.GetNativePortalTemplate(3);

        NavigationCell cellLimited = Cell(radius: Fixed64.One, height: Fixed64.One);
        using (TrailblazerWorldContext context = CreateContext(cellLimited, cellLimited))
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationNodeRef source = Resolve(lease.Graph, default);
            NavigationGraphEdge edge = FindEdge(
                lease.Graph,
                source,
                Resolve(lease.Graph, new VoxelIndex(1, 0, 0)));
            var inclusive = new TraversalEvaluator(
                lease.Graph,
                Profile(radius: Fixed64.One, height: Fixed64.One),
                DefaultPolicy,
                TraversalMedium.Solid);
            inclusive.EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Passable);

            Fixed64.TryAdd(Fixed64.One, Fixed64.MinIncrement, out Fixed64 justOver).Should().BeTrue();
            new TraversalEvaluator(
                    lease.Graph,
                    Profile(radius: justOver, height: Fixed64.One),
                    DefaultPolicy,
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
            new TraversalEvaluator(
                    lease.Graph,
                    Profile(radius: Fixed64.One, height: justOver),
                    DefaultPolicy,
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
        }

        NavigationCell portalLimited = Cell(radius: Fixed64.MaxValue, height: Fixed64.MaxValue);
        using (TrailblazerWorldContext context = CreateContext(portalLimited, portalLimited))
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationNodeRef source = Resolve(lease.Graph, default);
            NavigationGraphEdge edge = FindEdge(
                lease.Graph,
                source,
                Resolve(lease.Graph, new VoxelIndex(1, 0, 0)));
            new TraversalEvaluator(
                    lease.Graph,
                    Profile(
                        radius: portal.MaximumHorizontalRadius,
                        height: portal.MaximumBodyHeight,
                        maxStepUp: Fixed64.Zero,
                        maxDropDown: Fixed64.Zero),
                    DefaultPolicy,
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Passable);

            Fixed64.TryAdd(
                portal.MaximumHorizontalRadius,
                Fixed64.MinIncrement,
                out Fixed64 radiusOver).Should().BeTrue();
            Fixed64.TryAdd(
                portal.MaximumBodyHeight,
                Fixed64.MinIncrement,
                out Fixed64 heightOver).Should().BeTrue();
            new TraversalEvaluator(
                    lease.Graph,
                    Profile(radius: radiusOver, height: portal.MaximumBodyHeight),
                    DefaultPolicy,
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
            new TraversalEvaluator(
                    lease.Graph,
                    Profile(radius: portal.MaximumHorizontalRadius, height: heightOver),
                    DefaultPolicy,
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
        }
    }

    [Fact]
    public void FractionalHexEndpointPortal_ShouldAdmitOnlyZeroRadiusProfiles()
    {
        GridConfiguration configuration = new(
            new Vector3d(-8, 3, -20),
            new Vector3d(8, 5, -4),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(
                Fixed64.FromRaw(4_294_967_302L),
                (Fixed64)2,
                HexOrientation.PointyTop),
            storageKind: GridStorageKind.Sparse);
        var sourceIndex = new VoxelIndex(1, 0, 1);
        var targetIndex = new VoxelIndex(0, 0, 2);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        binding.TryGetCellPrism(sourceIndex, out GridCellPrism sourcePrism).Should().BeTrue();
        binding.TryGetCellPrism(targetIndex, out GridCellPrism targetPrism).Should().BeTrue();
        using TrailblazerWorldContext context = CreateContext(
            configuration,
            sourceIndex,
            targetIndex,
            Cell(),
            Cell(),
            targetPhysicallyPresent: true);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, sourceIndex);
        NavigationGraphEdge edge = FindEdge(lease.Graph, source, Resolve(lease.Graph, targetIndex));
        var zeroRadiusProfile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);

        new TraversalEvaluator(lease.Graph, zeroRadiusProfile, DefaultPolicy, TraversalMedium.Solid)
            .EvaluateEdge(source, edge, out TraversalEdgeEvidence traversal)
            .Should().Be(TraversalEvaluationStatus.Passable);
        new TraversalEvaluator(
                lease.Graph,
                Profile(radius: Fixed64.MinIncrement),
                DefaultPolicy,
                TraversalMedium.Solid)
            .EvaluateEdge(source, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);
        edge.NativePortal.TryTranslate(sourcePrism.Center, out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                Fixed64.Zero,
                Fixed64.One,
                out Vector3d sourceAnchor,
                out Vector3d targetAnchor)
            .Should().BeTrue();
        sourcePrism.Contains(sourceAnchor).Should().BeTrue();
        targetPrism.Contains(targetAnchor).Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                new Vector3d(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z),
                sourceAnchor,
                out Fixed64 sourceDistance)
            .Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                targetAnchor,
                new Vector3d(targetPrism.Center.X, targetPrism.VerticalMin, targetPrism.Center.Z),
                out Fixed64 targetDistance)
            .Should().BeTrue();
        Fixed64.TryAdd(sourceDistance, targetDistance, out Fixed64 expectedCost)
            .Should().BeTrue();
        traversal.Cost.Should().Be(expectedCost);
    }

    [Fact]
    public void EvaluateEdge_ShouldReportCheckedOverflowAfterPassabilityOnly()
    {
        NavigationCell sourceCell = Cell();
        NavigationCell targetCell = Cell(enterCost: Fixed64.MaxValue);
        using TrailblazerWorldContext context = CreateContext(sourceCell, targetCell);
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationNodeRef source = Resolve(lease.Graph, default);
            NavigationGraphEdge edge = FindEdge(
                lease.Graph,
                source,
                Resolve(lease.Graph, new VoxelIndex(1, 0, 0)));
            var evaluator = new TraversalEvaluator(
                lease.Graph,
                Profile(),
                DefaultPolicy,
                TraversalMedium.Solid);

            evaluator.EvaluateEdge(source, edge, out TraversalEdgeEvidence evidence)
                .Should().Be(TraversalEvaluationStatus.CostOverflow);
            evidence.Cost.Should().Be(Fixed64.Zero);
        }

        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? target).Should().BeTrue();
        grid.TryAddObstacle(target!, context.World.AllocateObstacleToken()).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease blockedLease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef blockedSource = Resolve(blockedLease.Graph, default);
        NavigationNodeRef blockedTarget = Resolve(blockedLease.Graph, new VoxelIndex(1, 0, 0));
        var blockedEdge = new NavigationGraphEdge(
            blockedTarget,
            NavigationGraphEdgeKind.Native,
            blockedLease.Graph.GetInstance(0).Map.GetNativePortalTemplate(3));
        new TraversalEvaluator(
                blockedLease.Graph,
                Profile(),
                DefaultPolicy,
                TraversalMedium.Solid)
            .EvaluateEdge(blockedSource, blockedEdge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);
    }

    [Fact]
    public void WarmedNativeEvaluation_ShouldAllocateZeroBytes()
    {
        using TrailblazerWorldContext context = CreateContext(Cell(), Cell());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, default);
        NavigationGraphEdge edge = FindEdge(
            lease.Graph,
            source,
            Resolve(lease.Graph, new VoxelIndex(1, 0, 0)));
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            Profile(),
            DefaultPolicy,
            TraversalMedium.Solid);
        int checksum = 0;
        Action evaluate = () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                checksum += (int)evaluator.EvaluateEdge(source, edge, out TraversalEdgeEvidence evidence);
                checksum += evidence.Cost.GetHashCode();
            }
        };
        evaluate();

        AllocationTestUtility.MeasureAllocatedBytes(evaluate).Should().Be(0);
        checksum.Should().NotBe(0);
    }

    private static TrailblazerWorldContext CreateContext(
        NavigationCell source,
        NavigationCell target,
        bool targetPhysicallyPresent = true)
    {
        return CreateContext(
            CreateConfiguration(),
            default,
            new VoxelIndex(1, 0, 0),
            source,
            target,
            targetPhysicallyPresent);
    }

    private static TrailblazerWorldContext CreateContext(
        GridConfiguration configuration,
        VoxelIndex sourceIndex,
        VoxelIndex targetIndex,
        NavigationCell source,
        NavigationCell target,
        bool targetPhysicallyPresent)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings());
        try
        {
            VoxelIndex[] physical = targetPhysicallyPresent
                ? new[] { sourceIndex, targetIndex }
                : new[] { sourceIndex };
            context.World.TryAddGrid(configuration, physical, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            NavigationMap map = new NavigationMapBuilder("map", binding)
                .AddCell(sourceIndex, source)
                .AddCell(targetIndex, target)
                .Build();
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

    private static GridConfiguration CreateConfiguration() => new(
        Vector3d.Zero,
        new Vector3d(4, 2, 4),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)4),
        storageKind: GridStorageKind.Sparse);

    private static TrailblazerWorldContextSettings CreateSettings()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            navigationAreaCount: 2,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }

    private static NavigationCell Cell(
        TraversalMedia media = TraversalMedia.Solid,
        TraversalCapability capabilities = TraversalCapability.None,
        NavigationAreaId area = default,
        Fixed64 enterCost = default,
        Fixed64 radius = default,
        Fixed64 height = default) => new(
            media,
            capabilities,
            area,
            enterCost,
            radius == Fixed64.Zero ? (Fixed64)4 : radius,
            height == Fixed64.Zero ? (Fixed64)4 : height);

    private static NavigationAgentProfile Profile(
        Fixed64 radius = default,
        Fixed64 height = default,
        Fixed64 maxStepUp = default,
        Fixed64 maxDropDown = default,
        TraversalMedia allowedMedia = TraversalMedia.Solid,
        TraversalCapability capabilities = TraversalCapability.None) => new(
            new KinematicBodyShape(
                radius == Fixed64.Zero ? Fixed64.Half : radius,
                height == Fixed64.Zero ? Fixed64.One : height,
                Fixed64.Zero),
            maxStepUp,
            maxDropDown,
            Fixed64.Zero,
            allowedMedia,
            capabilities);

    private static NavigationAreaPolicy CreatePolicy(params NavigationAreaRule[] rules) => new(
        new NavigationAreaPolicyKey("test", 1),
        rules);

    private static NavigationNodeRef Resolve(NavigationWorldGraph graph, VoxelIndex index)
    {
        graph.TryGetNodeRef(0, index, out NavigationNodeRef node).Should().BeTrue();
        return node;
    }

    private static NavigationGraphEdge FindEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        NavigationNodeRef target)
    {
        NavigationNativeSurfaceEdgeEnumerator edges = graph.EnumerateNativeSurfaceEdges(source);
        while (edges.MoveNext())
        {
            if (edges.Current.Target == target)
                return edges.Current;
        }
        throw new InvalidOperationException("Expected native edge was not enumerated.");
    }

    private static void CommitCellOverlay(
        TrailblazerWorldContext context,
        NavigationCellOverlayOperation cell)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta("map", new[] { cell }) })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        while (operation.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }
}
