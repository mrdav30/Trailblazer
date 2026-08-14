//=======================================================================
// NavigationNativeSurfaceEdgeTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
public sealed class NavigationNativeSurfaceEdgeTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void RectangularSurfaceEdges_ShouldUseCanonicalCenterAndBoundaryOrder()
    {
        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Dense);
        VoxelIndex[] cells = CreateRectangularCells();
        using TrailblazerWorldContext context = CreateContext(configuration, cells, cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        VoxelIndex center = new(1, 0, 1);

        NavigationNodeRef centerNode = Resolve(lease.Graph, center);
        ReadTargets(lease.Graph, centerNode).Should().Equal(
            Address(new VoxelIndex(0, 0, 1)),
            Address(new VoxelIndex(1, 0, 0)),
            Address(new VoxelIndex(1, 0, 2)),
            Address(new VoxelIndex(2, 0, 1)));

        NavigationNodeRef boundaryNode = Resolve(lease.Graph, default);
        ReadTargets(lease.Graph, boundaryNode).Should().Equal(
            Address(new VoxelIndex(0, 0, 1)),
            Address(new VoxelIndex(1, 0, 0)));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, GridStorageKind.Dense)]
    [InlineData(HexOrientation.PointyTop, GridStorageKind.Sparse)]
    [InlineData(HexOrientation.FlatTop, GridStorageKind.Dense)]
    [InlineData(HexOrientation.FlatTop, GridStorageKind.Sparse)]
    public void HexSurfaceEdges_ShouldUseCanonicalAxialOrder(
        HexOrientation orientation,
        GridStorageKind storage)
    {
        GridConfiguration configuration = CreateHexConfiguration(orientation, storage);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex center = FindHexCenter(binding);
        VoxelIndex[] cells = CreateHexNeighborhood(center);
        using TrailblazerWorldContext context = CreateContext(configuration, cells, cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;

        ReadTargets(lease.Graph, Resolve(lease.Graph, center)).Should().Equal(
            Address(Offset(center, HexDirection.QNegative)),
            Address(Offset(center, HexDirection.QNegativeRPositive)),
            Address(Offset(center, HexDirection.RNegative)),
            Address(Offset(center, HexDirection.RPositive)),
            Address(Offset(center, HexDirection.QPositiveRNegative)),
            Address(Offset(center, HexDirection.QPositive)));
    }

    [Fact]
    public void NativeSurfaceEdges_ShouldEnumerateReverseDirectionNaturally()
    {
        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Dense);
        VoxelIndex west = default;
        VoxelIndex east = new(1, 0, 0);
        VoxelIndex[] cells = { west, east };
        using TrailblazerWorldContext context = CreateContext(configuration, cells, cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;

        ReadTargets(lease.Graph, Resolve(lease.Graph, west)).Should().Equal(Address(east));
        ReadTargets(lease.Graph, Resolve(lease.Graph, east)).Should().Equal(Address(west));
    }

    [Fact]
    public void DenseAndSparsePhysicalStorage_ShouldProduceEquivalentEdges()
    {
        VoxelIndex[] cells = CreateRectangularCells();
        using TrailblazerWorldContext dense = CreateContext(
            CreateRectangularConfiguration(GridStorageKind.Dense),
            cells,
            cells);
        using TrailblazerWorldContext sparse = CreateContext(
            CreateRectangularConfiguration(GridStorageKind.Sparse),
            cells,
            cells);
        using NavigationWorldGraphLease denseLease = dense.Pathing.TryAcquireNavigationGraph()!;
        using NavigationWorldGraphLease sparseLease = sparse.Pathing.TryAcquireNavigationGraph()!;
        VoxelIndex center = new(1, 0, 1);

        ReadTargets(denseLease.Graph, Resolve(denseLease.Graph, center)).Should().Equal(
            ReadTargets(sparseLease.Graph, Resolve(sparseLease.Graph, center)));
    }

    [Fact]
    public void NativeSurfaceEdges_ShouldOmitSparseAndSuppressedCells_AndIncludeDynamicBlockedCells()
    {
        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Sparse);
        VoxelIndex center = new(1, 0, 1);
        VoxelIndex west = new(0, 0, 1);
        VoxelIndex south = new(1, 0, 0);
        VoxelIndex north = new(1, 0, 2);
        VoxelIndex east = new(2, 0, 1);
        VoxelIndex[] baked = { center, west, south, east };
        VoxelIndex[] physical = { center, south, north, east };
        using TrailblazerWorldContext context = CreateContext(configuration, baked, physical);
        CommitCellOverlay(
            context,
            NavigationCellOverlayOperation.Suppress(south),
            NavigationCellOverlayOperation.Set(north, SolidCell));
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(east, out Voxel? eastVoxel).Should().BeTrue();
        grid.TryAddObstacle(eastVoxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;

        ReadTargets(lease.Graph, Resolve(lease.Graph, center)).Should().Equal(
            Address(north),
            Address(east));
        NavigationNodeRef eastNode = Resolve(lease.Graph, east);
        lease.Graph.TryGetNodeState(eastNode, out NavigationNodeState eastState).Should().BeTrue();
        eastState.IsPresent.Should().BeTrue();
        eastState.IsBlocked.Should().BeTrue();
        eastState.ObstacleCount.Should().Be(1);
    }

    [Fact]
    public void SnapshotNodeRefs_ShouldBeSmallStableValues_AndRejectForeignMissingSlots()
    {
        Unsafe.SizeOf<NavigationNodeRef>().Should().Be(8);
        default(NavigationNodeRef).IsValid.Should().BeFalse();
        var invalidOrdinal = new NavigationNodeRef(-1, 0);
        var invalidSlot = new NavigationNodeRef(0, -1);
        invalidOrdinal.IsValid.Should().BeFalse();
        invalidSlot.IsValid.Should().BeFalse();

        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Dense);
        VoxelIndex center = default;
        VoxelIndex east = new(1, 0, 0);
        using TrailblazerWorldContext first = CreateContext(
            configuration,
            new[] { center, east },
            new[] { center, east });
        using TrailblazerWorldContext second = CreateContext(
            configuration,
            new[] { center },
            new[] { center });
        using NavigationWorldGraphLease firstLease = first.Pathing.TryAcquireNavigationGraph()!;
        using NavigationWorldGraphLease secondLease = second.Pathing.TryAcquireNavigationGraph()!;

        NavigationNodeRef foreign = Resolve(firstLease.Graph, east);
        NavigationNodeRef equal = new(foreign.MapOrdinal, foreign.CellSlot);
        foreign.Should().Be(equal);
        foreign.GetHashCode().Should().Be(equal.GetHashCode());
        foreign.Should().NotBe(Resolve(firstLease.Graph, center));
        secondLease.Graph.TryGetNodeAddress(foreign, out _).Should().BeFalse();
        secondLease.Graph.TryGetNodeState(foreign, out _).Should().BeFalse();
        ReadTargets(secondLease.Graph, foreign).Should().BeEmpty();
        secondLease.Graph.TryGetNodeAddress(default, out _).Should().BeFalse();
        secondLease.Graph.TryGetNodeState(new NavigationNodeRef(100, 100), out _).Should().BeFalse();
    }

    [Fact]
    public void WarmedNativeSurfaceEnumeration_ShouldAllocateZeroBytes()
    {
        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Dense);
        VoxelIndex[] cells = CreateRectangularCells();
        using TrailblazerWorldContext context = CreateContext(configuration, cells, cells);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = Resolve(lease.Graph, new VoxelIndex(1, 0, 1));
        int checksum = 0;
        Action enumerate = () => checksum = ConsumeEdges(lease.Graph, source, 10_000);
        enumerate();

        long allocated = AllocationTestUtility.MeasureAllocatedBytes(enumerate);

        allocated.Should().Be(0);
        checksum.Should().NotBe(0);
    }

    [Fact]
    public void RectangularNativePortalTemplates_ShouldMatchDirectGridForgeCompilationAtMultipleCells()
    {
        GridConfiguration configuration = new(
            new Vector3d(-5, 2, -7),
            new Vector3d(7, 4, 8),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)3));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding).Build();
        VoxelIndex[] offsets =
        {
            new(-1, 0, 0),
            new(0, 0, -1),
            new(0, 0, 1),
            new(1, 0, 0)
        };

        map.NativePortalTemplateCount.Should().Be(4);
        AssertPortalTemplatesMatchDirectCompilation(
            map,
            binding,
            offsets,
            new VoxelIndex(1, 0, 1),
            new VoxelIndex(3, 0, 3));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexNativePortalTemplates_ShouldMatchDirectGridForgeCompilationAtMultipleCells(
        HexOrientation orientation)
    {
        GridConfiguration configuration = new(
            new Vector3d(-8, 3, -8),
            new Vector3d(8, 5, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)2, orientation));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding).Build();
        VoxelIndex[] offsets =
        {
            HexDirectionUtility.GetOffset(HexDirection.QNegative),
            HexDirectionUtility.GetOffset(HexDirection.QNegativeRPositive),
            HexDirectionUtility.GetOffset(HexDirection.RNegative),
            HexDirectionUtility.GetOffset(HexDirection.RPositive),
            HexDirectionUtility.GetOffset(HexDirection.QPositiveRNegative),
            HexDirectionUtility.GetOffset(HexDirection.QPositive)
        };
        VoxelIndex first = FindHexCenter(binding);
        VoxelIndex second = FindHexCenter(binding, first);

        map.NativePortalTemplateCount.Should().Be(6);
        AssertPortalTemplatesMatchDirectCompilation(map, binding, offsets, first, second);
    }

    [Fact]
    public void NodeState_ShouldDeriveExactBakedAndDynamicAnchorsWithoutPerCellAnchorArrays()
    {
        GridConfiguration configuration = new(
            new Vector3d(-4, 6, -8),
            new Vector3d(2, 8, -2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)2, (Fixed64)3),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex baked = default;
        VoxelIndex dynamic = new(1, 0, 0);
        using TrailblazerWorldContext context = CreateContext(
            configuration,
            new[] { baked },
            new[] { baked, dynamic });
        CommitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell));
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;

        AssertNodeAnchors(lease.Graph, binding, baked);
        AssertNodeAnchors(lease.Graph, binding, dynamic);
        typeof(NavigationMap).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            .Should().NotContain(field => field.FieldType == typeof(Vector3d[]));
        typeof(NavigationMapInstance).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            .Should().NotContain(field => field.FieldType == typeof(Vector3d[]));
    }

    [Fact]
    public void NativePortalTemplateStorage_ShouldBeCountedOnceAndReusedBySnapshots()
    {
        GridConfiguration configuration = CreateRectangularConfiguration(GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        long expectedTemplateBytes = 24L
            + ((long)map.NativePortalTemplateCount * Unsafe.SizeOf<GridNavigationPortal>());
        long expectedPreparedBytes = 128L
            + (map.MapId.Length * sizeof(char))
            + ((long)map.Cells.Count * 96L)
            + prepared.BakedCellLookup.RetainedBytes
            + expectedTemplateBytes;

        map.NativePortalTemplateRetainedBytes.Should().Be(expectedTemplateBytes);
        prepared.RetainedBytes.Should().Be(expectedPreparedBytes);

        using TrailblazerWorldContext context = CreateContext(
            configuration,
            new[] { default(VoxelIndex) },
            new[] { default(VoxelIndex) });
        using NavigationWorldGraphLease first = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationMap storageOwner = first.Graph.GetInstance(0).Map;
        CommitCellOverlay(context, NavigationCellOverlayOperation.Set(default, SolidCell));
        using NavigationWorldGraphLease second = context.Pathing.TryAcquireNavigationGraph()!;

        second.Graph.GetInstance(0).Map.Should().BeSameAs(storageOwner);
        second.Graph.GetInstance(0).PreparedMapRetainedBytes.Should().Be(prepared.RetainedBytes);
    }

    private static TrailblazerWorldContext CreateContext(
        GridConfiguration configuration,
        IReadOnlyList<VoxelIndex> authoredCells,
        VoxelIndex[] physicalCells)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            bool added = configuration.StorageKind == GridStorageKind.Sparse
                ? context.World.TryAddGrid(configuration, physicalCells, out _)
                : context.World.TryAddGrid(configuration, out _);
            added.Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < authoredCells.Count; i++)
                builder.AddCell(authoredCells[i], SolidCell);
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

    private static void CommitCellOverlay(
        TrailblazerWorldContext context,
        params NavigationCellOverlayOperation[] cells)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta("map", cells) })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        while (operation.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    private static NavigationNodeRef Resolve(NavigationWorldGraph graph, VoxelIndex index)
    {
        graph.TryGetNodeRef(0, index, out NavigationNodeRef node).Should().BeTrue();
        graph.TryGetNodeAddress(node, out NavigationCellAddress address).Should().BeTrue();
        address.Should().Be(Address(index));
        return node;
    }

    private static List<NavigationCellAddress> ReadTargets(
        NavigationWorldGraph graph,
        NavigationNodeRef source)
    {
        var targets = new List<NavigationCellAddress>(6);
        NavigationNativeSurfaceEdgeEnumerator edges = graph.EnumerateNativeSurfaceEdges(source);
        while (edges.MoveNext())
        {
            edges.Current.Kind.Should().Be(NavigationGraphEdgeKind.Native);
            edges.Current.NativePortal.IsValid.Should().BeTrue();
            graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress address)
                .Should().BeTrue();
            targets.Add(address);
        }
        return targets;
    }

    private static int ConsumeEdges(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        int repetitions)
    {
        int checksum = 0;
        for (int i = 0; i < repetitions; i++)
        {
            NavigationNativeSurfaceEdgeEnumerator edges = graph.EnumerateNativeSurfaceEdges(source);
            while (edges.MoveNext())
                checksum += edges.Current.Target.GetHashCode();
        }
        return checksum;
    }

    private static void AssertPortalTemplatesMatchDirectCompilation(
        NavigationMap map,
        NormalizedGridConfiguration binding,
        VoxelIndex[] offsets,
        params VoxelIndex[] sources)
    {
        for (int sourceOrdinal = 0; sourceOrdinal < sources.Length; sourceOrdinal++)
        {
            VoxelIndex source = sources[sourceOrdinal];
            binding.TryGetCellPrism(source, out GridCellPrism sourcePrism).Should().BeTrue();
            for (int direction = 0; direction < offsets.Length; direction++)
            {
                VoxelIndex offset = offsets[direction];
                var target = new VoxelIndex(
                    source.x + offset.x,
                    source.y + offset.y,
                    source.z + offset.z);
                binding.TryGetCellPrism(target, out GridCellPrism targetPrism).Should().BeTrue();
                GridCellGeometry.TryCreateNavigationPortal(
                        sourcePrism,
                        targetPrism,
                        out GridNavigationPortal direct)
                    .Should().BeTrue();
                map.GetNativePortalTemplate(direction)
                    .TryTranslate(sourcePrism.Center, out GridNavigationPortal translated)
                    .Should().BeTrue();
                AssertPortal(translated, direct);
            }
        }
    }

    private static void AssertPortal(GridNavigationPortal actual, GridNavigationPortal expected)
    {
        actual.IsValid.Should().BeTrue();
        actual.FaceKind.Should().Be(expected.FaceKind);
        actual.SourceToTarget.Should().Be(expected.SourceToTarget);
        actual.CanonicalFacePoint.Should().Be(expected.CanonicalFacePoint);
        actual.MaximumHorizontalRadius.Should().Be(expected.MaximumHorizontalRadius);
        actual.MaximumBodyHeight.Should().Be(expected.MaximumBodyHeight);
    }

    private static void AssertNodeAnchors(
        NavigationWorldGraph graph,
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        graph.TryGetNodeState(Resolve(graph, index), out NavigationNodeState state).Should().BeTrue();
        state.Center.Should().Be(prism.Center);
        state.FootAnchor.Should().Be(new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z));
    }

    private static NavigationCellAddress Address(VoxelIndex index) => new("map", index);

    private static GridConfiguration CreateRectangularConfiguration(GridStorageKind storage) => new(
        Vector3d.Zero,
        new Vector3d(2, 0, 2),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: storage);

    private static GridConfiguration CreateHexConfiguration(
        HexOrientation orientation,
        GridStorageKind storage) => new(
        Vector3d.Zero,
        new Vector3d(6, 0, 6),
        topologyKind: GridTopologyKind.HexPrism,
        topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation),
        storageKind: storage);

    private static VoxelIndex[] CreateRectangularCells()
    {
        var cells = new VoxelIndex[9];
        int count = 0;
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
                cells[count++] = new VoxelIndex(x, 0, z);
        }
        return cells;
    }

    private static VoxelIndex FindHexCenter(
        NormalizedGridConfiguration binding,
        VoxelIndex excluded = default)
    {
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int r = 1; r < binding.Length - 1; r++)
            {
                VoxelIndex candidate = new(q, 0, r);
                if (candidate.Equals(excluded))
                    continue;
                VoxelIndex[] neighborhood = CreateHexNeighborhood(candidate);
                bool valid = true;
                for (int i = 0; i < neighborhood.Length; i++)
                    valid &= binding.IsValidIndex(neighborhood[i]);
                if (valid)
                    return candidate;
            }
        }
        throw new InvalidOperationException("The test configuration has no interior hex cell.");
    }

    private static VoxelIndex[] CreateHexNeighborhood(VoxelIndex center) =>
    new[]
    {
        center,
        Offset(center, HexDirection.QNegative),
        Offset(center, HexDirection.QNegativeRPositive),
        Offset(center, HexDirection.RNegative),
        Offset(center, HexDirection.RPositive),
        Offset(center, HexDirection.QPositiveRNegative),
        Offset(center, HexDirection.QPositive)
    };

    private static VoxelIndex Offset(VoxelIndex source, HexDirection direction)
    {
        VoxelIndex offset = HexDirectionUtility.GetOffset(direction);
        return new VoxelIndex(source.x + offset.x, source.y + offset.y, source.z + offset.z);
    }
}
