using System;
using System.Linq;
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

public sealed partial class NavigationTopDownTests
{
    private static readonly NavigationCell Open = new(
        TraversalMedia.Solid, TraversalCapability.None, default,
        Fixed64.Zero, Fixed64.Two, Fixed64.Zero);
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("circle", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void Endpoint_AdmitsTangencyAndRejectsOneRawPenetrationIntoAnUnauthoredRow()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(1, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid)
            .AddCell(new VoxelIndex(0, 1, 1), Open)
            .AddCell(new VoxelIndex(1, 1, 1), Open)
            .AddCell(new VoxelIndex(2, 1, 1), Open).Build(), 2);

        Resolve(context, Vector2d.Zero, Fixed64.Half, out var address).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(new NavigationCellAddress("plane", new VoxelIndex(1, 1, 1)));
        Resolve(context, Vector2d.Zero, Fixed64.Half + Fixed64.FromRaw(1), out address)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        address.Should().Be(default(NavigationCellAddress));
    }

    [Fact]
    public void Endpoint_UsesCanonicalAddressOnAClosedBoundaryAndHasNoWarmAllocations()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        var center = new Vector2d(Fixed64.Half, Fixed64.Zero);
        var expected = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        Resolve(context, center, Fixed64.Quarter, out var address).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(expected);
        for (int i = 0; i < 16; i++)
            Resolve(context, center, Fixed64.Quarter, out _);

        long start = GC.GetAllocatedBytesForCurrentThread();
        bool exact = true;
        for (int i = 0; i < 64; i++)
            exact &= Resolve(context, center, Fixed64.Quarter, out address) == NavigationGuideStatus.Success
                && address.Equals(expected);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        exact.Should().BeTrue();
        allocated.Should().Be(0);
    }

    [Fact]
    public void Endpoint_RequiresTheSelectedLayerAndCurrentPhysicalGeneration()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0),
            storageKind: GridStorageKind.Sparse);
        var selected = new VoxelIndex(0, 1, 0);
        context.World.TryAddGrid(grid, new[] { default(VoxelIndex), selected }, out ushort gridIndex)
            .Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Success);

        TestRequire.Grid(context, gridIndex).TryRemoveVoxel(selected).Should().BeTrue();
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Stale);
        context.Simulate();
        WaitForMaterialization(context, "plane");
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.InvalidStart);
    }

    [Theory]
    [InlineData("compatible")]
    [InlineData("spatial")]
    [InlineData("side-view")]
    [InlineData("other embedding")]
    public void Endpoint_BorrowsCoverageOnlyFromACompatiblePublishedLayer(string neighborKind)
    {
        using var context = CreateContext();
        var first = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        var second = new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, TopDown("plane", first).SetDefaultCell(Open).Build(), 2);
        var neighbor = new NavigationMapBuilder("neighbor", second).SetDefaultCell(Open);
        if (neighborKind != "spatial")
            neighbor.SetPlanarLayer(neighborKind == "side-view" ? NavigationMode2D.SideView : NavigationMode2D.TopDown, 1,
                neighborKind is "compatible" or "side-view" ? Fixed64.Half : Fixed64.One);
        Publish(context, neighbor.Build(), 3);

        var center = new Vector2d(Fixed64.FromFraction(3, 8), Fixed64.Zero);
        bool compatible = neighborKind == "compatible";
        Resolve(context, center, Fixed64.Quarter, out var address)
            .Should().Be(compatible ? NavigationGuideStatus.Success : NavigationGuideStatus.InvalidStart);
        if (compatible)
            address.Should().Be(new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0)));
        if (neighborKind == "side-view")
            context.Pathing.ResolveTopDownEndpoint("neighbor", Vector2d.Right, Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, TraversalCapability.None, Budget(), out _).Should().Be(NavigationGuideStatus.NoMap);
    }

    [Theory]
    [InlineData(0, 16, 128)]
    [InlineData(1, 16, 128)]
    [InlineData(2, 16, 128)]
    [InlineData(256, 0, 128)]
    [InlineData(256, 16, 0)]
    public void Endpoint_UsesExistingQueryLimits(int lookups, int candidates, int covered)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);

        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out var address, Budget(lookups, candidates, covered))
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        address.Should().Be(default(NavigationCellAddress));
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void Endpoint_IgnoresDormantNeighborsAndRejectsADormantSource()
    {
        using var context = CreateContext();
        var live = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        var absent = new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(live, out _).Should().BeTrue();
        Publish(context, TopDown("plane", live).SetDefaultCell(Open).Build(), 2);
        Publish(context, TopDown("dormant", absent).SetDefaultCell(Open).Build(), 3, materialize: false);

        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Success);
        context.Pathing.ResolveTopDownEndpoint("dormant", new Vector2d(Fixed64.One, Fixed64.Zero), Fixed64.Quarter,
            Policy.Key, TraversalMedium.Solid, TraversalCapability.None, Budget(), out _)
            .Should().Be(NavigationGuideStatus.NoMap);
    }

    [Fact]
    public void Endpoint_RejectsHexBroadPhaseCornerAndAcceptsTheActualCellCenter()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder("plane", grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build(), 2);

        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out var address).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(new NavigationCellAddress("plane", default));
        Fixed64 nearCorner = Fixed64.FromFraction(15, 16);
        Resolve(context, new Vector2d(nearCorner, nearCorner), Fixed64.Quarter, out _)
            .Should().Be(NavigationGuideStatus.InvalidStart);
    }

    [Fact]
    public void Endpoint_UsesCurrentOverlayAndObstacleStateUnderTheWholeCircle()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out ushort gridIndex).Should().BeTrue();
        var center = new Vector2d(Fixed64.FromFraction(3, 8), Fixed64.Zero);
        var neighbor = new VoxelIndex(1, 1, 0);
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).AddCell(neighbor, Open).Build(), 2);
        var live = TestRequire.Grid(context, gridIndex);
        live.TryGetVoxel(neighbor, out var voxel).Should().BeTrue();
        var occupied = TestRequire.NotNull(voxel);
        var blocker = context.World.AllocateObstacleToken();
        live.TryAddObstacle(occupied, blocker).Should().BeTrue();
        context.Simulate();
        WaitForMaterialization(context, "plane");
        Resolve(context, center, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        live.TryRemoveObstacle(occupied, blocker).Should().BeTrue();
        context.Simulate();
        WaitForMaterialization(context, "plane");
        Resolve(context, center, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Success);

        var denied = new NavigationAreaPolicy(new NavigationAreaPolicyKey("denied", 1),
            new[] { new NavigationAreaRule(false, Fixed64.Zero) });
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(denied, 3, context.FrameCount + 1))
            .Should().BeTrue();
        context.Simulate();
        context.Pathing.ResolveTopDownEndpoint("plane", center, Fixed64.Quarter, denied.Key,
            TraversalMedium.Solid, TraversalCapability.None, Budget(), out _)
            .Should().Be(NavigationGuideStatus.InvalidStart);

        var overlay = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
            new[] { new NavigationMapOverlayDelta("plane", new[] { NavigationCellOverlayOperation.Suppress(neighbor) }) })),
            4, context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        Resolve(context, center, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.InvalidStart);
    }

    [Fact]
    public void Endpoint_ReportsMissingInputsAndArithmeticOverflow()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        var center = new Vector2d(Fixed64.One, Fixed64.Zero);

        Resolve(context, center, Fixed64.MaxValue, out _).Should().Be(NavigationGuideStatus.CostOverflow);
        Resolve(context, center, Fixed64.Zero, out _).Should().Be(NavigationGuideStatus.InvalidProfile);
        context.Pathing.ResolveTopDownEndpoint("plane", center, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Unknown, TraversalCapability.None, Budget(), out _)
            .Should().Be(NavigationGuideStatus.InvalidProfile);
        context.Pathing.ResolveTopDownEndpoint("plane", center, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, (TraversalCapability)0x400, Budget(), out _)
            .Should().Be(NavigationGuideStatus.InvalidProfile);
        context.Pathing.ResolveTopDownEndpoint("plane", center, Fixed64.Quarter, new NavigationAreaPolicyKey("absent", 1),
            TraversalMedium.Solid, TraversalCapability.None, Budget(), out _)
            .Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.ResolveTopDownEndpoint("missing", center, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, Budget(), out _)
            .Should().Be(NavigationGuideStatus.NoMap);
    }

    [Fact]
    public void Endpoint_RejectsClosedPublicationAndDiscardsSupersededResults()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        context.World.TryAddGrid(grid, out ushort index).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out var address).Should().Be(NavigationGuideStatus.Success);
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        NavigationWorldGraph original = store.Current;
        var world = context.World;
        var captured = (world.SpawnToken, world.Version, world.ChangeSequence);
        world.IncrementGridVersion(index, significant: true);
        TrailblazerPathingService.FinishTopDownQuery(NavigationGuideStatus.Success, original, original, captured,
            (world.SpawnToken, world.Version, world.ChangeSequence), ref address).Should().Be(NavigationGuideStatus.Stale);
        address.Should().Be(default(NavigationCellAddress));

        NavigationWorldGraph closed = original.WithClosedStructuralComponents(NavigationSurfaceComponentKeySet.Empty,
            closeAllStructuralComponents: true, original.GraphVersion + 1);
        store.TryPublish(closed).Should().Be(NavigationCandidatePublication.Published);
        Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.Stale);
        address = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        FindRoute(context, address, address, Fixed64.Quarter, PathAlgorithm.AStar, new NavigationCellAddress[1], out _, out _)
            .Should().Be(NavigationGuideStatus.Stale);
        TrailblazerPathingService.FinishTopDownQuery(NavigationGuideStatus.Success, original, closed, captured,
            captured, ref address).Should().Be(NavigationGuideStatus.Stale);
        address.Should().Be(default(NavigationCellAddress));
    }

    [Fact]
    public void Endpoint_ReturnsCapacityForExhaustedSnapshotLeases()
    {
        using var context = CreateContext();
        var leases = new NavigationWorldGraphLease?[context.Settings.MaxConcurrentSnapshotLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++)
                leases[i] = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
            Resolve(context, Vector2d.Zero, Fixed64.Quarter, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            foreach (NavigationWorldGraphLease? lease in leases)
                lease?.Dispose();
        }
    }

    [Theory]
    [InlineData(1, 128)]
    [InlineData(2, 0)]
    public void Endpoint_UsesFixedGenerationAndBodyScratchCapacity(int maps, int addresses)
    {
        using var context = CreateContext();
        var first = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        var second = new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 1, 0));
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, TopDown("plane", first).SetDefaultCell(Open).Build(), 2);
        Publish(context, TopDown("neighbor", second).SetDefaultCell(Open).Build(), 3);
        using var lease = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
        lease.Graph.TryGetMap("plane", out var instance).Should().BeTrue();
        var work = new NavigationPlanarWork(new NavigationRayWorkspace(maps, 1, 1, addresses, 0));

        work.Resolve(context.World, lease.Graph, instance, instance.Map.PlanarBinding!.Value,
            Vector2d.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None, Policy,
            new NavigationWorkMeter(Budget()), out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
    }

    private static TrailblazerWorldContext CreateContext()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1))
            .Should().BeTrue();
        return context;
    }

    private static NavigationMapBuilder TopDown(string id, GridConfiguration grid) =>
        new NavigationMapBuilder(id, grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.Half);

    private static void Publish(TrailblazerWorldContext context, NavigationMap map, long sequence,
        bool materialize = true)
    {
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear, sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        for (int i = 0; i < 16 && operation.Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        if (materialize)
            WaitForMaterialization(context, map.MapId);
    }

    private static void WaitForMaterialization(TrailblazerWorldContext context, string mapId)
    {
        for (int i = 0; i < 16 && !IsMaterialized(context, mapId); i++)
            context.Simulate();
        IsMaterialized(context, mapId).Should().BeTrue();
    }

    private static bool IsMaterialized(TrailblazerWorldContext context, string mapId) =>
        context.Pathing.GetNavigationGraphDiagnostics().Maps.Single(map => map.MapId == mapId).IsMaterialized;

    private static NavigationGuideStatus Resolve(TrailblazerWorldContext context, Vector2d center, Fixed64 radius,
        out NavigationCellAddress address, NavigationWorkBudget? budget = null) =>
        context.Pathing.ResolveTopDownEndpoint("plane", center, radius, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, budget ?? Budget(), out address);

    private static NavigationWorkBudget Budget(int lookups = 256, int candidates = 16, int covered = 128) =>
        new(lookups, candidates, 0, 0, 0, 0, 0, 0, 0, covered, 0);
}
