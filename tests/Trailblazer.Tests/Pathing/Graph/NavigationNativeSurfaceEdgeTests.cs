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
    public void SnapshotNodeRefs_ShouldBeSmallStableValues_AndFailClosedAgainstAnotherRoot()
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

    private static VoxelIndex FindHexCenter(NormalizedGridConfiguration binding)
    {
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int r = 1; r < binding.Length - 1; r++)
            {
                VoxelIndex candidate = new(q, 0, r);
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
