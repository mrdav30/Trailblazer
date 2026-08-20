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
public sealed class NavigationPublicGuideMatrixTests
{
    [Fact]
    public void RequestGuide_ShouldPreserveTheCanonicalRectangularTieRouteAcrossDenseAndSparseStorage()
    {
        RouteResult dense = RequestRingRoute(GridStorageKind.Dense, reverseInsertion: false);
        RouteResult denseReversed = RequestRingRoute(GridStorageKind.Dense, reverseInsertion: true);
        RouteResult sparse = RequestRingRoute(GridStorageKind.Sparse, reverseInsertion: false);
        RouteResult sparseReversed = RequestRingRoute(GridStorageKind.Sparse, reverseInsertion: true);
        var expected = new[]
        {
            new NavigationCellAddress("storage-matrix", new VoxelIndex(0, 0, 1)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(0, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(0, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(1, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(1, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(2, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(2, 0, 0)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(2, 0, 1)),
            new NavigationCellAddress("storage-matrix", new VoxelIndex(2, 0, 1))
        };

        dense.Addresses.Should().Equal(expected);
        denseReversed.Addresses.Should().Equal(expected);
        sparse.Addresses.Should().Equal(expected);
        sparseReversed.Addresses.Should().Equal(expected);
        dense.Addresses.Should().Equal(denseReversed.Addresses);
        dense.Addresses.Should().Equal(sparse.Addresses);
        dense.Addresses.Should().Equal(sparseReversed.Addresses);
        dense.TotalCost.Should().Be((Fixed64)4);
        denseReversed.TotalCost.Should().Be(dense.TotalCost);
        sparse.TotalCost.Should().Be(dense.TotalCost);
        sparseReversed.TotalCost.Should().Be(dense.TotalCost);
    }

    [Fact]
    public void DisposedGuideLease_ShouldFailClosedAndReleaseItsPayload()
    {
        VoxelIndex[] cells = RingCells();
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            GridStorageKind.Sparse,
            cells,
            out NormalizedGridConfiguration binding);
        PathQuery query = CreateQuery(binding, cells[0], cells[4]);
        context.Guides.RequestGuide(query, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease lease = TestRequire.NotNull(acquired);
        NavigationAStarPayloadCache cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(1);
        lease.TotalCost.Should().Be((Fixed64)4);

        lease.Dispose();
        lease.Dispose();

        lease.Status.Should().Be(NavigationGuideStatus.Stale);
        lease.CurrentWaypointIndex.Should().Be(-1);
        lease.WaypointCount.Should().Be(0);
        lease.TotalCost.Should().Be(Fixed64.Zero);
        lease.TryGetCurrentWaypoint(out NavigationCellAddress address, out Vector3d foot)
            .Should().Be(NavigationGuideStatus.Stale);
        address.Should().Be(default);
        foot.Should().Be(default);
        lease.TryAdvanceWaypoint().Should().Be(NavigationGuideStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void RequestGuide_ShouldReportNoPathForDisconnectedMappedEndpoints()
    {
        var cells = new[]
        {
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            GridStorageKind.Sparse,
            cells,
            out NormalizedGridConfiguration binding);

        NavigationGuideStatus status = context.Guides.RequestGuide(
            CreateQuery(binding, cells[0], cells[1]),
            out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.NoPath);
        lease.Should().BeNull();
    }

    [Fact]
    public void RequestGuide_ShouldRejectTheDefaultQueryAsAnInvalidProfile()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

        NavigationGuideStatus status = context.Guides.RequestGuide(
            default,
            out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.InvalidProfile);
        lease.Should().BeNull();
    }

    [Theory]
    [InlineData(true, NavigationGuideStatus.InvalidStart)]
    [InlineData(false, NavigationGuideStatus.InvalidEnd)]
    public void RequestGuide_ShouldReportTheUnmappedStrictEndpoint(
        bool invalidateStart,
        NavigationGuideStatus expected)
    {
        VoxelIndex mapped = invalidateStart
            ? new VoxelIndex(2, 0, 0)
            : new VoxelIndex(0, 0, 0);
        VoxelIndex missing = invalidateStart
            ? new VoxelIndex(0, 0, 0)
            : new VoxelIndex(2, 0, 0);
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            GridStorageKind.Sparse,
            new[] { mapped },
            out NormalizedGridConfiguration binding);
        PathQuery query = invalidateStart
            ? CreateQuery(binding, missing, mapped)
            : CreateQuery(binding, mapped, missing);

        NavigationGuideStatus status = context.Guides.RequestGuide(
            query,
            out NavigationGuideLease? lease);

        status.Should().Be(expected);
        lease.Should().BeNull();
    }

    [Fact]
    public void RequestGuide_ShouldReportBudgetExceededWhenSearchCannotExpandTheMappedStart()
    {
        var cells = new[]
        {
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 0, 0)
        };
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            GridStorageKind.Sparse,
            cells,
            out NormalizedGridConfiguration binding);
        PathQuery complete = CreateQuery(binding, cells[0], cells[1]);
        var budgetLimited = new PathQuery(
            complete.Start,
            complete.End,
            complete.Agent,
            complete.AreaPolicy,
            complete.Traversal,
            complete.Algorithm,
            new NavigationWorkBudget(8_192, 32, 0, 1_024, 1_024, 0, 0, 0, 0, 0, 0),
            complete.AllowTransitions,
            complete.FlowField);

        NavigationGuideStatus status = context.Guides.RequestGuide(
            budgetLimited,
            out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        lease.Should().BeNull();
    }

    [Fact]
    public void PhysicalBlocker_ShouldStaleTheLeaseAndRestoreTheSameRouteAfterRemoval()
    {
        var cells = new[]
        {
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            GridStorageKind.Sparse,
            cells,
            out NormalizedGridConfiguration binding);
        PathQuery query = CreateQuery(binding, cells[0], cells[2]);
        context.Guides.RequestGuide(query, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease original = TestRequire.NotNull(acquired);
        RouteResult initialRoute = ReadRoute(original);
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(cells[1], out Voxel? middle).Should().BeTrue();
        var obstacle = context.World.AllocateObstacleToken();

        grid.TryAddObstacle(middle!, obstacle).Should().BeTrue();
        context.Simulate();

        original.TryGetCurrentWaypoint(out _, out _).Should().Be(NavigationGuideStatus.Stale);
        original.Status.Should().Be(NavigationGuideStatus.Stale);
        context.Guides.RequestGuide(query, out NavigationGuideLease? blocked)
            .Should().Be(NavigationGuideStatus.NoPath);
        blocked.Should().BeNull();
        original.Dispose();

        grid.TryRemoveObstacle(middle!, obstacle).Should().BeTrue();
        context.Simulate();
        context.Guides.RequestGuide(query, out NavigationGuideLease? restored)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease restoredLease = TestRequire.NotNull(restored);
        RouteResult restoredRoute = ReadRoute(restoredLease);

        initialRoute.Addresses.Should().Equal(
            new NavigationCellAddress("storage-matrix", cells[0]),
            new NavigationCellAddress("storage-matrix", cells[1]),
            new NavigationCellAddress("storage-matrix", cells[1]),
            new NavigationCellAddress("storage-matrix", cells[2]),
            new NavigationCellAddress("storage-matrix", cells[2]));
        initialRoute.TotalCost.Should().Be((Fixed64)2);
        restoredRoute.Addresses.Should().Equal(initialRoute.Addresses);
        restoredRoute.TotalCost.Should().Be(initialRoute.TotalCost);
    }

    private static RouteResult RequestRingRoute(
        GridStorageKind storageKind,
        bool reverseInsertion)
    {
        VoxelIndex[] logicalCells = RingCells();
        VoxelIndex[] authoredCells = (VoxelIndex[])logicalCells.Clone();
        if (reverseInsertion)
            Array.Reverse(authoredCells);
        using TrailblazerWorldContext context = CreateContextAndPublishMap(
            storageKind,
            authoredCells,
            out NormalizedGridConfiguration binding);
        PathQuery query = CreateQuery(binding, logicalCells[0], logicalCells[4]);
        context.Guides.RequestGuide(query, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease lease = TestRequire.NotNull(acquired);
        return ReadRoute(lease);
    }

    private static RouteResult ReadRoute(NavigationGuideLease lease)
    {
        var addresses = new List<NavigationCellAddress>(lease.WaypointCount);
        for (int ordinal = 0; ordinal < lease.WaypointCount; ordinal++)
        {
            lease.TryGetCurrentWaypoint(out NavigationCellAddress address, out _)
                .Should().Be(NavigationGuideStatus.Success);
            addresses.Add(address);
            if (ordinal + 1 < lease.WaypointCount)
                lease.TryAdvanceWaypoint().Should().Be(NavigationGuideStatus.Success);
        }

        return new RouteResult(addresses.ToArray(), lease.TotalCost);
    }

    private static TrailblazerWorldContext CreateContextAndPublishMap(
        GridStorageKind storageKind,
        VoxelIndex[] cells,
        out NormalizedGridConfiguration binding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, (Fixed64)2, Fixed64.One),
            storageKind: storageKind);
        bool added = storageKind == GridStorageKind.Dense
            ? context.World.TryAddGrid(configuration, out _)
            : context.World.TryAddGrid(configuration, cells, out _);
        added.Should().BeTrue();
        configuration.TryNormalize(out binding)
            .Should().BeTrue();

        var builder = new NavigationMapBuilder("storage-matrix", binding);
        for (int i = 0; i < cells.Length; i++)
            builder.AddCell(cells[i], NavigationAStarExitTestHarness.Cell);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            publicationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        for (int frame = 0;
            frame < 1_024
            && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
            frame++)
        {
            context.Simulate();
        }
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return context;
    }

    private static PathQuery CreateQuery(
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        VoxelIndex end) => NavigationAStarExitTestHarness.Query(
        NavigationAStarExitTestHarness.GetFoot(binding, start),
        "storage-matrix",
        NavigationAStarExitTestHarness.GetFoot(binding, end),
        "storage-matrix",
        NavigationAStarExitTestHarness.Profile());

    private static VoxelIndex[] RingCells() => new[]
    {
        new VoxelIndex(0, 0, 1),
        new VoxelIndex(0, 0, 0),
        new VoxelIndex(1, 0, 0),
        new VoxelIndex(2, 0, 0),
        new VoxelIndex(2, 0, 1),
        new VoxelIndex(2, 0, 2),
        new VoxelIndex(1, 0, 2),
        new VoxelIndex(0, 0, 2)
    };

    private readonly struct RouteResult
    {
        internal RouteResult(NavigationCellAddress[] addresses, Fixed64 totalCost)
        {
            Addresses = addresses;
            TotalCost = totalCost;
        }

        internal NavigationCellAddress[] Addresses { get; }
        internal Fixed64 TotalCost { get; }
    }
}
