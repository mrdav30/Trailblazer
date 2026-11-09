using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Fact]
    public void Leg_ProvesTheMiddleOfAHexSweepEvenWhenBothEndpointsFit()
    {
        using var context = CreateContext();
        var metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(12, 0, 6),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        context.World.TryAddGrid(grid, out ushort slot).Should().BeTrue();
        var source = new NavigationCellAddress("plane", new VoxelIndex(2, 0, 2));
        var target = new NavigationCellAddress("plane", new VoxelIndex(3, 0, 2));
        var north = new VoxelIndex(2, 0, 3);
        var south = new VoxelIndex(3, 0, 1);
        Publish(context, new NavigationMapBuilder("plane", grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero)
            .AddCell(source.Index, Open).AddCell(target.Index, Open).Build(), 2);

        Fixed64 penetratingRadius = Fixed64.Half + Fixed64.FromRaw(1);
        var live = TestRequire.Grid(context, slot);
        live.TryGetVoxel(source.Index, out var sourceVoxel).Should().BeTrue();
        live.TryGetVoxel(target.Index, out var targetVoxel).Should().BeTrue();
        Resolve(context, TestRequire.NotNull(sourceVoxel).WorldPosition.ToVector2d(), penetratingRadius, out var address)
            .Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(source);
        Resolve(context, TestRequire.NotNull(targetVoxel).WorldPosition.ToVector2d(), penetratingRadius, out address)
            .Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(target);
        ValidateLeg(context, source, target, Fixed64.Half).Should().Be(NavigationGuideStatus.Success);
        ValidateLeg(context, source, target, penetratingRadius).Should().Be(NavigationGuideStatus.NoPath);
        ValidateLeg(context, target, source, penetratingRadius).Should().Be(NavigationGuideStatus.NoPath);

        var overlay = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
            new[] { new NavigationMapOverlayDelta("plane", new[]
            {
                NavigationCellOverlayOperation.Set(north, Open), NavigationCellOverlayOperation.Set(south, Open)
            }) })), 3, context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        ValidateLeg(context, source, target, penetratingRadius).Should().Be(NavigationGuideStatus.Success);
        ValidateLeg(context, target, source, penetratingRadius).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void Leg_RequiresOrdinaryDistinctNeighborsAndSelectedLayerAddresses()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(2, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        var source = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var neighbor = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 0));
        var diagonal = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 1));
        var far = new NavigationCellAddress("plane", new VoxelIndex(2, 1, 0));
        var offLayer = new NavigationCellAddress("plane", new VoxelIndex(0, 0, 0));
        var outside = new NavigationCellAddress("plane", new VoxelIndex(3, 1, 0));
        ValidateLeg(context, source, neighbor, Fixed64.Quarter).Should().Be(NavigationGuideStatus.Success);
        ValidateLeg(context, source, new NavigationCellAddress("plane", new VoxelIndex(0, 1, 1)), Fixed64.Quarter)
            .Should().Be(NavigationGuideStatus.Success);
        ValidateLeg(context, source, diagonal, Fixed64.Quarter).Should().Be(NavigationGuideStatus.NoPath);
        ValidateLeg(context, source, far, Fixed64.Quarter).Should().Be(NavigationGuideStatus.NoPath);
        ValidateLeg(context, source, source, Fixed64.Quarter).Should().Be(NavigationGuideStatus.NoPath);
        ValidateLeg(context, offLayer, neighbor, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidStart);
        ValidateLeg(context, source, offLayer, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidEnd);
        ValidateLeg(context, outside, neighbor, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidStart);
        ValidateLeg(context, source, outside, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidEnd);
        ValidateLeg(context, default, neighbor, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidStart);
        ValidateLeg(context, source, default, Fixed64.Quarter).Should().Be(NavigationGuideStatus.InvalidEnd);
    }

    [Theory]
    [InlineData("adjacent", NavigationGuideStatus.Success)]
    [InlineData("coincident", NavigationGuideStatus.NoPath)]
    [InlineData("incongruent", NavigationGuideStatus.NoPath)]
    [InlineData("different embedding", NavigationGuideStatus.NoPath)]
    [InlineData("spatial", NavigationGuideStatus.NoMap)]
    [InlineData("dormant", NavigationGuideStatus.NoMap)]
    public void Leg_RequiresCompatibleNeighborFootprintsAcrossMaps(string kind, NavigationGuideStatus expected)
    {
        using var context = CreateContext();
        var first = new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0));
        int x = kind == "coincident" ? 0 : 1;
        var metrics = kind == "incongruent"
            ? GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.Two) : default;
        var second = new GridConfiguration(new Vector3d(x, 0, 0), new Vector3d(x, 0, 0), topologyMetrics: metrics);
        context.World.TryAddGrid(first, out _).Should().BeTrue();
        if (kind != "dormant")
            context.World.TryAddGrid(second, out _).Should().BeTrue();
        Publish(context, TopDown("plane", first).SetDefaultCell(Open).Build(), 2);
        var neighbor = new NavigationMapBuilder("neighbor", second).SetDefaultCell(Open);
        if (kind != "spatial")
            neighbor.SetPlanarLayer(NavigationMode2D.TopDown, 0,
                kind == "different embedding" ? Fixed64.Zero : Fixed64.Half);
        Publish(context, neighbor.Build(), 3, materialize: kind != "dormant");

        var source = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var target = new NavigationCellAddress("neighbor", default);
        ValidateLeg(context, source, target, Fixed64.Quarter).Should().Be(expected);
        ValidateLeg(context, target, source, Fixed64.Quarter).Should().Be(expected);
    }

    [Fact]
    public void Leg_UsesOneEdgeBudgetAndRevalidatesThePhysicalGenerationWithoutWarmAllocations()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 0), storageKind: GridStorageKind.Sparse);
        var source = new NavigationCellAddress("plane", new VoxelIndex(0, 1, 0));
        var target = new NavigationCellAddress("plane", new VoxelIndex(1, 1, 0));
        context.World.TryAddGrid(grid, new[] { source.Index, target.Index }, out ushort slot).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        ValidateLeg(context, source, target, Fixed64.Quarter, edges: 0).Should().Be(NavigationGuideStatus.BudgetExceeded);
        ValidateLeg(context, source, target, Fixed64.Quarter, lookups: 2).Should().Be(NavigationGuideStatus.BudgetExceeded);
        ValidateLeg(context, source, target, Fixed64.Quarter, covered: 0).Should().Be(NavigationGuideStatus.BudgetExceeded);
        for (int i = 0; i < 16; i++)
            ValidateLeg(context, source, target, Fixed64.Quarter).Should().Be(NavigationGuideStatus.Success);

        long start = GC.GetAllocatedBytesForCurrentThread();
        bool allFit = true;
        for (int i = 0; i < 64; i++)
            allFit &= ValidateLeg(context, source, target, Fixed64.Quarter) == NavigationGuideStatus.Success;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        allFit.Should().BeTrue();
        allocated.Should().Be(0);

        TestRequire.Grid(context, slot).TryRemoveVoxel(target.Index).Should().BeTrue();
        ValidateLeg(context, source, target, Fixed64.Quarter).Should().Be(NavigationGuideStatus.Stale);
        context.Simulate();
        WaitForMaterialization(context, "plane");
        ValidateLeg(context, source, target, Fixed64.Quarter).Should().Be(NavigationGuideStatus.NoPath);
    }

    private static NavigationGuideStatus ValidateLeg(TrailblazerWorldContext context, NavigationCellAddress source,
        NavigationCellAddress target, Fixed64 radius, int edges = 1, int lookups = 256, int covered = 128) =>
        context.Pathing.ValidateTopDownLeg(source, target, radius, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, new NavigationWorkBudget(lookups, 0, 0, edges, 0, 0, 0, 0, 0, covered, 0));
}
