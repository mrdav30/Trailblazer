//=======================================================================
// NavigationVolumeRayTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Reflection;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationVolumeRayTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("volume-ray", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Theory]
    [InlineData((int)GridTopologyKind.RectangularPrism, (int)HexOrientation.PointyTop, (int)TraversalMedium.Gas)]
    [InlineData((int)GridTopologyKind.RectangularPrism, (int)HexOrientation.PointyTop, (int)TraversalMedium.Liquid)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.PointyTop, (int)TraversalMedium.Gas)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.FlatTop, (int)TraversalMedium.Gas)]
    public void Ray_ShouldTraverseVerticalPrimaryFaceChain(
        int topologyValue,
        int orientationValue,
        int mediumValue)
    {
        GridTopologyKind topology = (GridTopologyKind)topologyValue;
        HexOrientation orientation = (HexOrientation)orientationValue;
        TraversalMedium medium = (TraversalMedium)mediumValue;
        TraversalMedia media = NavigationCell.ToMedia(medium);
        using TrailblazerWorldContext context = CreateVerticalContext(
            topology,
            orientation,
            media);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationCellAddress sourceAddress = new("map", default);
        NavigationCellAddress targetAddress = new("map", new VoxelIndex(0, 1, 0));
        Assert.True(lease.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef source));
        Assert.True(lease.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef target));
        Assert.True(lease.Graph.TryGetRawNodeState(source, out NavigationNodeState sourceState));
        Assert.True(lease.Graph.TryGetRawNodeState(target, out NavigationNodeState targetState));
        NavigationAgentProfile profile = Profile(media);
        Assert.True(sourceState.TryGetCenteredVolumeFootAnchor(
            profile.Shape.Height,
            out Vector3d start));
        Assert.True(targetState.TryGetCenteredVolumeFootAnchor(
            profile.Shape.Height,
            out Vector3d end));
        var request = new NavigationRayRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            lease.Graph,
            profile,
            Policy,
            medium,
            start,
            end,
            NavigationRayEndpointAllowance.None);

        NavigationRayResult result = RunRay(request);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.Equal(sourceAddress, result.StartAddress);
        Assert.Equal(targetAddress, result.EndAddress);
    }

    [Fact]
    public void Ray_ShouldTraverseRectangularVolumeShortcutWithoutSurfacePortalState()
    {
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(1, 0, 1);
        VoxelIndex[] indices =
        {
            sourceIndex,
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(0, 0, 1),
            targetIndex
        };
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            indices);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(sourceIndex),
            MapAddress(targetIndex),
            TraversalMedium.Gas);

        NavigationRayResult result = RunRay(request);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
    }

    [Theory]
    [InlineData((int)HexOrientation.PointyTop)]
    [InlineData((int)HexOrientation.FlatTop)]
    public void Ray_ShouldTraverseHexVolumeShortcutThroughDispatcher(int orientationValue)
    {
        HexOrientation orientation = (HexOrientation)orientationValue;
        VoxelIndex source = FindCompleteHexCenter(orientation);
        VoxelIndex[] indices = new VoxelIndex[HexDirectionUtility.Offsets.Length + 1];
        indices[0] = source;
        VoxelIndex target = default;
        bool foundShortcut = false;
        for (int i = 0; i < HexDirectionUtility.Offsets.Length; i++)
        {
            VoxelIndex offset = HexDirectionUtility.Offsets[i];
            indices[i + 1] = new VoxelIndex(
                source.x + offset.x,
                source.y + offset.y,
                source.z + offset.z);
            HexDirection direction = (HexDirection)i;
            if (!foundShortcut
                && !HexDirectionUtility.IsPlanar(direction)
                && !HexDirectionUtility.IsVertical(direction))
            {
                target = indices[i + 1];
                foundShortcut = true;
            }
        }
        Assert.True(foundShortcut);
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.HexPrism,
            orientation,
            TraversalMedia.Gas,
            indices);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(source),
            MapAddress(target),
            TraversalMedium.Gas);

        NavigationRayResult result = RunRay(request, out NavigationWorkMeter meter);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.True(meter.CoveredVoxelIntervals > 0);
    }

    [Fact]
    public void Request_ShouldRejectUnknownMedium()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new NavigationRayRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Unknown,
            Vector3d.Zero,
            Vector3d.Zero,
            NavigationRayEndpointAllowance.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ray_ShouldTraverseCrossGridAutomaticFaceInBothDirections(bool reverse)
    {
        using TrailblazerWorldContext context = CreateCrossGridContext();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var first = new NavigationCellAddress("a-source", default);
        var second = new NavigationCellAddress("b-target", default);
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            reverse ? second : first,
            reverse ? first : second,
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas));

        NavigationRayResult result = RunRay(request);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ray_ShouldUseProfileResolvedHandoffAcrossHeterogeneousVerticalSeam(
        bool reverse)
    {
        using TrailblazerWorldContext context = CreateCrossGridContext(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)3,
                    (Fixed64)2)),
            new GridConfiguration(
                new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero),
                new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    Fixed64.One,
                    (Fixed64)2)));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var lower = new NavigationCellAddress("a-source", default);
        var upper = new NavigationCellAddress("b-target", default);
        Assert.Equal(1, lease.Graph.AutomaticSeams.PairCount);
        Assert.True(lease.Graph.AutomaticSeams.TryGetPair(lower, upper, out _));
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            reverse ? upper : lower,
            reverse ? lower : upper,
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas, radius: Fixed64.One / (Fixed64)4));
        Fixed64 canonicalCost = GetCanonicalVolumeCost(
            context,
            lease.Graph,
            reverse ? upper : lower,
            reverse ? lower : upper,
            TraversalMedium.Gas,
            request.Profile);

        NavigationRayResult result = RunRay(request);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.Equal(canonicalCost, result.TraversalCost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ray_ShouldFinishAcrossUnlikeSizedVerticalFace(bool reverse)
    {
        using TrailblazerWorldContext context = CreateCrossGridContext(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)3,
                    (Fixed64)2,
                    (Fixed64)2)),
            new GridConfiguration(
                new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero),
                new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    Fixed64.One,
                    (Fixed64)2,
                    (Fixed64)2)));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var left = new NavigationCellAddress("a-source", default);
        var right = new NavigationCellAddress("b-target", default);
        Assert.Equal(1, lease.Graph.AutomaticSeams.PairCount);
        Assert.True(lease.Graph.AutomaticSeams.TryGetPair(left, right, out _));
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            reverse ? right : left,
            reverse ? left : right,
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas, radius: Fixed64.One / (Fixed64)4));

        NavigationRayResult result = RunRay(request);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
    }

    [Fact]
    public void Ray_ShouldRejectMisalignedCrossGridNonFace()
    {
        using TrailblazerWorldContext context = CreateCrossGridContext(
            new Vector3d(0, 0, 1));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            new NavigationCellAddress("a-source", default),
            new NavigationCellAddress("b-target", default),
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas));

        Assert.Equal(NavigationRayStatus.Blocked, RunRay(request).Status);
    }

    [Fact]
    public void Ray_ShouldUseCoveredUnionAcrossGridBoundary()
    {
        using TrailblazerWorldContext context = CreateCrossGridClosureContext();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            new NavigationCellAddress("a-source", new VoxelIndex(1, 1, 1)),
            new NavigationCellAddress("b-target", new VoxelIndex(0, 1, 1)),
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas, radius: (Fixed64)3 / (Fixed64)4));

        NavigationRayResult result = RunRay(request, out NavigationWorkMeter meter);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.True(meter.CoveredVoxelIntervals > 0);
    }

    [Fact]
    public void Ray_ShouldRefuseVolumeGuideMeterUntilFlowIntegration()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);
        var work = new NavigationRayWork(new NavigationRayWorkspace(4, 16, 16, 256, 128));
        var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 64,
            maxCursorLegScans: 64,
            maxCursorRebases: 64,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 64,
            maxLocalRecoveryAttempts: 64));
        work.Begin(request);

        Assert.Equal(NavigationRayStatus.Blocked, work.Advance(ref meter));
    }

    [Fact]
    public void Ray_ShouldUseCoveredUnionForOversizedFaceAndEndpointLegs()
    {
        var indices = new VoxelIndex[125];
        int count = 0;
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++)
                    indices[count++] = new VoxelIndex(x, y, z);
            }
        }
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            indices);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationAgentProfile profile = Profile(
            TraversalMedia.Gas,
            radius: (Fixed64)5 / (Fixed64)4);
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            new NavigationCellAddress("map", new VoxelIndex(2, 2, 2)),
            new NavigationCellAddress("map", new VoxelIndex(2, 3, 2)),
            TraversalMedium.Gas,
            profile);
        var sourceAddress = new NavigationCellAddress(
            "map",
            new VoxelIndex(2, 2, 2));
        var targetAddress = new NavigationCellAddress(
            "map",
            new VoxelIndex(2, 3, 2));
        Fixed64 canonicalCost = GetCanonicalVolumeCost(
            context,
            lease.Graph,
            sourceAddress,
            targetAddress,
            TraversalMedium.Gas,
            profile);

        NavigationRayResult result = RunRay(
            request,
            out NavigationWorkMeter meter,
            Budget(),
            pageCapacity: 16,
            coveredAddressCapacity: 256);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.Equal(canonicalCost, result.TraversalCost);
        Assert.True(meter.CoveredVoxelIntervals > 0);
        Assert.Equal(
            NavigationRayStatus.Success,
            RunRay(
                request,
                Budget(maxLookupProbes: meter.LookupProbes),
                pageCapacity: 16,
                coveredAddressCapacity: 256).Status);
        Assert.Equal(
            NavigationRayStatus.BudgetExceeded,
            RunRay(
                request,
                Budget(maxLookupProbes: meter.LookupProbes - 1),
                pageCapacity: 16,
                coveredAddressCapacity: 256).Status);
        Assert.Equal(
            NavigationRayStatus.Success,
            RunRay(
                request,
                Budget(maxCoveredVoxelIntervals: meter.CoveredVoxelIntervals),
                pageCapacity: 16,
                coveredAddressCapacity: 256).Status);
        Assert.Equal(
            NavigationRayStatus.BudgetExceeded,
            RunRay(
                request,
                Budget(maxCoveredVoxelIntervals: meter.CoveredVoxelIntervals - 1),
                pageCapacity: 16,
                coveredAddressCapacity: 256).Status);
        Assert.Equal(
            NavigationRayStatus.Success,
            RunRay(
                request,
                Budget(),
                pageCapacity: 16,
                coveredAddressCapacity: 144).Status);
        Assert.Equal(
            NavigationRayStatus.CapacityExceeded,
            RunRay(
                request,
                Budget(),
                pageCapacity: 16,
                coveredAddressCapacity: 143).Status);
    }

    [Fact]
    public void Ray_ShouldCertifyOversizedZeroLengthPlacementThroughCoveredUnion()
    {
        var indices = new VoxelIndex[9];
        int count = 0;
        for (int x = 0; x <= 2; x++)
        {
            for (int z = 0; z <= 2; z++)
                indices[count++] = new VoxelIndex(x, 1, z);
        }
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            indices);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var address = new NavigationCellAddress("map", new VoxelIndex(1, 1, 1));
        NavigationAgentProfile profile = Profile(
            TraversalMedia.Gas,
            radius: (Fixed64)3 / (Fixed64)2);
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            address,
            address,
            TraversalMedium.Gas,
            profile);

        NavigationRayResult result = RunRay(request, out NavigationWorkMeter meter);

        Assert.Equal(NavigationRayStatus.Success, result.Status);
        Assert.True(meter.CoveredVoxelIntervals > 0);
    }

    [Fact]
    public void Ray_ShouldBlockOffCenterBodyWhenCoveredUnionHasMissingClosure()
    {
        VoxelIndex[] semantic =
        {
            default,
            new VoxelIndex(0, 1, 0),
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(1, 1, 0)
        };
        VoxelIndex[] physical = { default, new VoxelIndex(0, 1, 0) };
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            semantic,
            physical);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationAgentProfile profile = Profile(TraversalMedia.Gas, radius: Fixed64.Half);
        NavigationRayRequest centered = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);
        Vector3d offset = new(
            Fixed64.Half + Fixed64.Half / (Fixed64)2,
            Fixed64.Zero,
            Fixed64.Zero);
        var request = new NavigationRayRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            lease.Graph,
            profile,
            Policy,
            TraversalMedium.Gas,
            centered.Start + offset,
            centered.End + offset,
            NavigationRayEndpointAllowance.None);

        Assert.Equal(NavigationRayStatus.Blocked, RunRay(request).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Ray_ShouldRejectWrongMediumCapabilityClearancePolicyOrPhysicalEvidence(
        int rejectionKind)
    {
        VoxelIndex[] indices = { default, new VoxelIndex(0, 1, 0) };
        TraversalMedia targetMedia = rejectionKind == 0
            ? TraversalMedia.Liquid
            : TraversalMedia.Gas;
        TraversalCapability required = rejectionKind == 1
            ? TraversalCapability.Fly
            : TraversalCapability.None;
        NavigationAreaId targetArea = default;
        Fixed64 clearance = rejectionKind == 2
            ? Fixed64.Half / (Fixed64)2
            : (Fixed64)4;
        NavigationCell[] cells =
        {
            Cell(TraversalMedia.Gas),
            Cell(targetMedia, required, targetArea, clearance)
        };
        VoxelIndex[]? physical = rejectionKind == 4
            ? new[] { default(VoxelIndex) }
            : null;
        NavigationAreaPolicy policy = rejectionKind == 3
            ? new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("volume-ray-blocked", 1),
                new[] { new NavigationAreaRule(false, Fixed64.Zero) })
            : Policy;
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            indices,
            physical,
            cells,
            policy);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(indices[0]),
            MapAddress(indices[1]),
            TraversalMedium.Gas,
            areaPolicy: policy);

        Assert.Equal(NavigationRayStatus.Blocked, RunRay(request).Status);
    }

    [Fact]
    public void Ray_ShouldNotCrossOrSkipSemanticTransition()
    {
        VoxelIndex source = default;
        var target = new VoxelIndex(0, 2, 0);
        var targetAddress = new NavigationCellAddress("map", target);
        var transition = new TraversalTransitionDefinition(
            "volume-ray-jump",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Gas,
            targetAddress,
            TraversalMedium.Gas,
            additionalCost: Fixed64.Zero);
        VoxelIndex[] indices = { source, target };
        using TrailblazerWorldContext context = CreateContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedia.Gas,
            indices,
            transition: transition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(source),
            MapAddress(target),
            TraversalMedium.Gas);

        Assert.Equal(NavigationRayStatus.Blocked, RunRay(request).Status);
    }

    [Fact]
    public void Ray_ShouldHonorExactEvaluatedEdgeBudgetAndOneBelow()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);
        NavigationRayResult baseline = RunRay(request, out NavigationWorkMeter meter);
        Assert.Equal(NavigationRayStatus.Success, baseline.Status);
        Assert.True(meter.EvaluatedEdges > 0);

        Assert.Equal(
            NavigationRayStatus.Success,
            RunRay(request, Budget(maxEvaluatedEdges: meter.EvaluatedEdges)).Status);
        Assert.Equal(
            NavigationRayStatus.BudgetExceeded,
            RunRay(request, Budget(maxEvaluatedEdges: meter.EvaluatedEdges - 1)).Status);
    }

    [Fact]
    public void Ray_ShouldFailClosedAtDependencyCapacityOneBelow()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);

        Assert.Equal(
            NavigationRayStatus.CapacityExceeded,
            RunRay(request, Budget(), pageCapacity: 0).Status);
        Assert.Equal(
            NavigationRayStatus.Success,
            RunRay(request, Budget(), pageCapacity: 1).Status);
    }

    [Fact]
    public void Ray_ShouldAllocateZeroBytesAfterWarmup()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);
        var work = new NavigationRayWork(new NavigationRayWorkspace(4, 16, 16, 256, 128));
        var meter = new NavigationWorkMeter(Budget());
        for (int i = 0; i < 8; i++)
        {
            meter.Reset(Budget());
            work.Begin(request);
            Assert.Equal(NavigationRayStatus.Success, work.Advance(meter));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            meter.Reset(Budget());
            work.Begin(request);
            if (work.Advance(meter) != NavigationRayStatus.Success)
                throw new Xunit.Sdk.XunitException("Warmed volume ray did not complete.");
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Ray_ShouldInvalidateOnlyAffectedPageDependencies()
    {
        using (TrailblazerWorldContext affected = CreateVerticalContext(TraversalMedia.Gas))
        using (NavigationWorldGraphLease lease = affected.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationRayRequest request = CreateRequest(
                affected,
                lease.Graph,
                MapAddress(default),
                MapAddress(new VoxelIndex(0, 1, 0)),
                TraversalMedium.Gas);
            Assert.True(lease.Graph.TryGetMap("map", out NavigationMapInstance? instance));
            NavigationMap replacement = new NavigationMapBuilder(
                "map",
                instance!.Map.GridBinding)
                .AddCell(default, Cell(TraversalMedia.Gas))
                .AddCell(
                    new VoxelIndex(0, 1, 0),
                    new NavigationCell(
                        TraversalMedia.Gas,
                        TraversalCapability.None,
                        default,
                        Fixed64.One,
                        (Fixed64)4,
                        (Fixed64)4))
                .Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(replacement, bakeVersion: 2),
                OverlayReplacementPolicy.Clear,
                operationSequence: 3,
                effectiveFrame: affected.FrameCount + 1);
            Assert.True(affected.Pathing.Admit(operation));
            SimulateUntilTerminal(affected, operation.Receipt);
            Assert.Equal(NavigationOperationStatus.Applied, operation.Receipt.Status);

            Assert.Equal(NavigationRayStatus.Stale, RunRay(request).Status);
        }

        using TrailblazerWorldContext unaffected = CreateCrossGridContext();
        using NavigationWorldGraphLease unaffectedLease =
            unaffected.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest unaffectedRequest = CreateRequest(
            unaffected,
            unaffectedLease.Graph,
            new NavigationCellAddress("a-source", default),
            new NavigationCellAddress("b-target", default),
            TraversalMedium.Gas,
            Profile(TraversalMedia.Gas));
        GridConfiguration unrelatedConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        Assert.True(unaffected.World.TryAddGrid(unrelatedConfiguration, out _));
        Assert.True(unrelatedConfiguration.TryNormalize(
            out NormalizedGridConfiguration unrelatedBinding));
        var unrelatedOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("c-unrelated", unrelatedBinding)
                    .AddCell(default, Cell(TraversalMedia.Gas))
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 10,
            effectiveFrame: unaffected.FrameCount + 1);
        Assert.True(unaffected.Pathing.Admit(unrelatedOperation));
        SimulateUntilTerminal(unaffected, unrelatedOperation.Receipt);
        Assert.Equal(NavigationOperationStatus.Applied, unrelatedOperation.Receipt.Status);

        Assert.Equal(NavigationRayStatus.Success, RunRay(unaffectedRequest).Status);
    }

    [Fact]
    public void Ray_ShouldRejectRawWorldMutationBetweenTraceAndEvaluation()
    {
        using TrailblazerWorldContext context = CreateVerticalContext(TraversalMedia.Gas);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationRayRequest request = CreateRequest(
            context,
            lease.Graph,
            MapAddress(default),
            MapAddress(new VoxelIndex(0, 1, 0)),
            TraversalMedium.Gas);
        var work = new NavigationRayWork(new NavigationRayWorkspace(4, 16, 16, 256, 128));
        var meter = new NavigationWorkMeter(Budget());
        work.Begin(request);
        Assert.Equal(NavigationRayStatus.Pending, InvokePhase(work, "Trace", meter));
        Assert.Equal(NavigationRayStatus.Pending, InvokePhase(work, "MapIntervals", meter));
        Assert.True(lease.Graph.TryGetMap("map", out NavigationMapInstance? instance));
        Assert.True(context.World.ActiveGrids[instance!.GridIdentity.GridIndex]
            .TryRemoveVoxel(new VoxelIndex(0, 1, 0)));

        Assert.Equal(NavigationRayStatus.Stale, InvokePhase(work, "EvaluateChain", meter));
    }

    private static TrailblazerWorldContext CreateVerticalContext(TraversalMedia media)
        => CreateVerticalContext(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            media);

    private static TrailblazerWorldContext CreateVerticalContext(
        GridTopologyKind topology,
        HexOrientation orientation,
        TraversalMedia media)
        => CreateContext(
            topology,
            orientation,
            media,
            new[] { default(VoxelIndex), new VoxelIndex(0, 1, 0) });

    private static VoxelIndex FindCompleteHexCenter(HexOrientation orientation)
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(12, 12, 12),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation),
            storageKind: GridStorageKind.Sparse);
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));
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
        throw new InvalidOperationException("The hex test grid has no complete neighborhood.");
    }

    private static TrailblazerWorldContext CreateContext(
        GridTopologyKind topology,
        HexOrientation orientation,
        TraversalMedia media,
        VoxelIndex[] indices,
        VoxelIndex[]? physicalIndices = null,
        NavigationCell[]? cells = null,
        NavigationAreaPolicy? areaPolicy = null,
        TraversalTransitionDefinition? transition = null)
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
            Assert.True(context.World.TryAddGrid(
                configuration,
                physicalIndices ?? indices,
                out _));
            Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));
            NavigationAreaPolicy publishedPolicy = areaPolicy ?? Policy;
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                publishedPolicy,
                1,
                context.FrameCount + 1);
            Assert.True(context.Pathing.Admit(policyOperation));
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < indices.Length; i++)
            {
                builder.AddCell(
                    indices[i],
                    cells == null ? Cell(media) : cells[i]);
            }
            if (transition.HasValue)
                builder.AddTransition(transition.Value);
            NavigationMap map = builder.Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 2,
                effectiveFrame: context.FrameCount + 1);
            Assert.True(context.Pathing.Admit(operation));
            while (policyOperation.Receipt.Status == NavigationOperationStatus.Pending
                || operation.Receipt.Status == NavigationOperationStatus.Pending)
            {
                context.Simulate();
            }
            Assert.Equal(NavigationOperationStatus.Applied, policyOperation.Receipt.Status);
            Assert.Equal(NavigationOperationStatus.Applied, operation.Receipt.Status);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateCrossGridContext() =>
        CreateCrossGridContext(Vector3d.Zero);

    private static TrailblazerWorldContext CreateCrossGridContext(
        Vector3d targetPosition)
    {
        GridConfiguration sourceConfiguration = new(
            new Vector3d(-1, 0, 0),
            new Vector3d(-1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        GridConfiguration targetConfiguration = new(
            targetPosition,
            targetPosition,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        return CreateCrossGridContext(sourceConfiguration, targetConfiguration);
    }

    private static TrailblazerWorldContext CreateCrossGridContext(
        GridConfiguration sourceConfiguration,
        GridConfiguration targetConfiguration)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            Assert.True(context.World.TryAddGrid(sourceConfiguration, out _));
            Assert.True(context.World.TryAddGrid(targetConfiguration, out _));
            Assert.True(sourceConfiguration.TryNormalize(
                out NormalizedGridConfiguration sourceBinding));
            Assert.True(targetConfiguration.TryNormalize(
                out NormalizedGridConfiguration targetBinding));
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                Policy,
                1,
                context.FrameCount + 1);
            Assert.True(context.Pathing.Admit(policyOperation));
            NavigationMap[] maps =
            {
                new NavigationMapBuilder("a-source", sourceBinding)
                    .AddCell(default, Cell(TraversalMedia.Gas))
                    .Build(),
                new NavigationMapBuilder("b-target", targetBinding)
                    .AddCell(default, Cell(TraversalMedia.Gas))
                    .Build()
            };
            var receipts = new NavigationOperationReceipt[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                var operation = new NavigationMapCommitOperation(
                    new PreparedNavigationMap(maps[i], bakeVersion: 1),
                    OverlayReplacementPolicy.Clear,
                    operationSequence: i + 2,
                    effectiveFrame: context.FrameCount + 1);
                Assert.True(context.Pathing.Admit(operation));
                receipts[i] = operation.Receipt;
            }
            for (int frame = 0;
                frame < 256 && receipts[1].Status == NavigationOperationStatus.Pending;
                frame++)
            {
                context.Simulate();
            }
            Assert.Equal(NavigationOperationStatus.Applied, policyOperation.Receipt.Status);
            Assert.All(
                receipts,
                receipt => Assert.Equal(NavigationOperationStatus.Applied, receipt.Status));
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateCrossGridClosureContext()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration sourceConfiguration = new(
                new Vector3d(-2, 0, 0),
                new Vector3d(-1, 2, 2),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse);
            GridConfiguration targetConfiguration = new(
                Vector3d.Zero,
                new Vector3d(1, 2, 2),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse);
            var indices = new VoxelIndex[18];
            int count = 0;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int z = 0; z < 3; z++)
                        indices[count++] = new VoxelIndex(x, y, z);
                }
            }
            Assert.True(context.World.TryAddGrid(sourceConfiguration, indices, out _));
            Assert.True(context.World.TryAddGrid(targetConfiguration, indices, out _));
            Assert.True(sourceConfiguration.TryNormalize(
                out NormalizedGridConfiguration sourceBinding));
            Assert.True(targetConfiguration.TryNormalize(
                out NormalizedGridConfiguration targetBinding));
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                Policy,
                1,
                context.FrameCount + 1);
            Assert.True(context.Pathing.Admit(policyOperation));
            var sourceBuilder = new NavigationMapBuilder("a-source", sourceBinding);
            var targetBuilder = new NavigationMapBuilder("b-target", targetBinding);
            for (int i = 0; i < indices.Length; i++)
            {
                sourceBuilder.AddCell(indices[i], Cell(TraversalMedia.Gas));
                targetBuilder.AddCell(indices[i], Cell(TraversalMedia.Gas));
            }
            NavigationMap[] maps = { sourceBuilder.Build(), targetBuilder.Build() };
            var receipts = new NavigationOperationReceipt[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                var operation = new NavigationMapCommitOperation(
                    new PreparedNavigationMap(maps[i], bakeVersion: 1),
                    OverlayReplacementPolicy.Clear,
                    operationSequence: i + 2,
                    effectiveFrame: context.FrameCount + 1);
                Assert.True(context.Pathing.Admit(operation));
                receipts[i] = operation.Receipt;
            }
            for (int frame = 0;
                frame < 256 && receipts[1].Status == NavigationOperationStatus.Pending;
                frame++)
            {
                context.Simulate();
            }
            Assert.Equal(NavigationOperationStatus.Applied, policyOperation.Receipt.Status);
            Assert.All(
                receipts,
                receipt => Assert.Equal(NavigationOperationStatus.Applied, receipt.Status));
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationRayRequest CreateRequest(
        TrailblazerWorldContext context,
        NavigationWorldGraph graph,
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        TraversalMedium medium,
        NavigationAgentProfile? requestedProfile = null,
        NavigationAreaPolicy? areaPolicy = null)
    {
        Assert.True(graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef source));
        Assert.True(graph.TryGetNodeRef(targetAddress, out NavigationNodeRef target));
        Assert.True(graph.TryGetRawNodeState(source, out NavigationNodeState sourceState));
        Assert.True(graph.TryGetRawNodeState(target, out NavigationNodeState targetState));
        NavigationAgentProfile profile = requestedProfile
            ?? Profile(NavigationCell.ToMedia(medium));
        Assert.True(sourceState.TryGetCenteredVolumeFootAnchor(
            profile.Shape.Height,
            out Vector3d start));
        Assert.True(targetState.TryGetCenteredVolumeFootAnchor(
            profile.Shape.Height,
            out Vector3d end));
        return new NavigationRayRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            profile,
            areaPolicy ?? Policy,
            medium,
            start,
            end,
            NavigationRayEndpointAllowance.None);
    }

    private static NavigationCellAddress MapAddress(VoxelIndex index) =>
        new("map", index);

    private static NavigationCell Cell(
        TraversalMedia media,
        TraversalCapability requiredCapabilities = TraversalCapability.None,
        NavigationAreaId area = default,
        Fixed64 clearanceRadius = default,
        Fixed64 clearanceHeight = default) => new(
        media,
        requiredCapabilities,
        area,
        Fixed64.Zero,
        clearanceRadius == Fixed64.Zero ? (Fixed64)4 : clearanceRadius,
        clearanceHeight == Fixed64.Zero ? (Fixed64)4 : clearanceHeight);

    private static NavigationAgentProfile Profile(
        TraversalMedia media,
        Fixed64 radius = default,
        TraversalCapability capabilities = TraversalCapability.None) => new(
        new KinematicBodyShape(
            radius == Fixed64.Zero ? Fixed64.Half : radius,
            Fixed64.One,
            Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        media,
        capabilities);

    private static NavigationRayResult RunRay(
        NavigationRayRequest request,
        NavigationWorkBudget? budget = null,
        int pageCapacity = 16,
        int coveredAddressCapacity = 256) => RunRay(
            request,
            out _,
            budget,
            pageCapacity,
            coveredAddressCapacity);

    private static Fixed64 GetCanonicalVolumeCost(
        TrailblazerWorldContext context,
        NavigationWorldGraph graph,
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        TraversalMedium medium,
        NavigationAgentProfile profile)
    {
        Assert.True(graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef source));
        Assert.True(graph.TryGetNodeRef(targetAddress, out NavigationNodeRef target));
        var workspace = new NavigationRayWorkspace(4, 16, 16, 256, 128);
        var edges = new NavigationTraversalEdgeEnumerator(
            context.World,
            graph,
            new NavigationMediumStateRef(source, medium),
            profile,
            Policy,
            workspace,
            allowTransitions: false);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = int.MaxValue;
        for (int step = 0; step < 256; step++)
        {
            NavigationTraversalEdgeAdvanceStatus status = edges.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Edge
                && edges.CurrentKind == NavigationTraversalEdgeKind.Volume
                && edges.CurrentTarget == new NavigationMediumStateRef(target, medium))
            {
                return edges.CurrentCost;
            }
            Assert.DoesNotContain(
                status,
                new[]
                {
                    NavigationTraversalEdgeAdvanceStatus.Blocked,
                    NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
                    NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
                    NavigationTraversalEdgeAdvanceStatus.CostOverflow,
                    NavigationTraversalEdgeAdvanceStatus.Stale
                });
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                break;
        }
        Assert.Fail("The canonical volume dispatcher did not emit the requested edge.");
        return default;
    }

    private static NavigationRayResult RunRay(
        NavigationRayRequest request,
        out NavigationWorkMeter meter,
        NavigationWorkBudget? budget = null,
        int pageCapacity = 16,
        int coveredAddressCapacity = 256)
    {
        var workspace = new NavigationRayWorkspace(
            4,
            pageCapacity,
            16,
            coveredAddressCapacity,
            Math.Min(128, coveredAddressCapacity));
        var work = new NavigationRayWork(workspace);
        work.Begin(request);
        meter = new NavigationWorkMeter(budget ?? Budget());
        NavigationRayStatus status = work.Advance(meter);
        Assert.NotEqual(NavigationRayStatus.Pending, status);
        return work.Result;
    }

    private static NavigationWorkBudget Budget(
        int maxEvaluatedEdges = 4_096,
        int maxLookupProbes = 4_096,
        int maxCoveredVoxelIntervals = 4_096) => new(
        maxLookupProbes: maxLookupProbes,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: maxEvaluatedEdges,
        maxConnectionLegs: 0,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 64,
        maxCoveredVoxelIntervals: maxCoveredVoxelIntervals,
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

    private static NavigationRayStatus InvokePhase(
        NavigationRayWork work,
        string methodName,
        NavigationWorkMeter meter)
    {
        MethodInfo method = typeof(NavigationRayWork).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] arguments = { meter, default(GuideSampleWorkMeter), false };
        return (NavigationRayStatus)method.Invoke(work, arguments)!;
    }
}
