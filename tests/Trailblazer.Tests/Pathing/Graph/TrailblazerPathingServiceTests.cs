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

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class TrailblazerPathingServiceTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    [Fact]
    public void TryResolveCommittedCell_ShouldRejectGridCellsOutsideThePublishedMap()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        PublishSingleCellMap(context, configuration, "bounded-map");
        VoxelGrid grid = TestRequire.Grid(context, gridIndex);
        var outside = new WorldVoxelIndex(
            context.World.SpawnToken,
            gridIndex,
            grid.SpawnToken,
            new VoxelIndex(1, 0, 0));

        NavigationCommittedCellResolveStatus status = context.Pathing.TryResolveCommittedCell(
            configuration.ToGridKey(),
            outside,
            out NavigationCellAddress address,
            out NavigationAreaId area,
            out long graphVersion);

        status.Should().Be(NavigationCommittedCellResolveStatus.NoCell);
        address.Should().Be(default);
        area.Should().Be(default);
        graphVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryResolveCommittedCell_ShouldRejectPublishedCellsAbsentFromSparseStorage()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(Vector3d.Zero, GridStorageKind.Sparse);
        context.World.TryAddGrid(
            configuration,
            Array.Empty<VoxelIndex>(),
            out ushort gridIndex).Should().BeTrue();
        PublishSingleCellMap(context, configuration, "sparse-map");
        VoxelGrid grid = TestRequire.Grid(context, gridIndex);
        var absent = new WorldVoxelIndex(
            context.World.SpawnToken,
            gridIndex,
            grid.SpawnToken,
            default);

        NavigationCommittedCellResolveStatus status = context.Pathing.TryResolveCommittedCell(
            configuration.ToGridKey(),
            absent,
            out NavigationCellAddress address,
            out NavigationAreaId area,
            out long graphVersion);

        status.Should().Be(NavigationCommittedCellResolveStatus.NoCell);
        address.Should().Be(default);
        area.Should().Be(default);
        graphVersion.Should().BeGreaterThan(0);
    }

    private static GridConfiguration CreateConfiguration(
        Vector3d boundsSize,
        GridStorageKind storageKind) => new(
        Vector3d.Zero,
        boundsSize,
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: storageKind);

    private static void PublishSingleCellMap(
        TrailblazerWorldContext context,
        GridConfiguration configuration,
        string mapId)
    {
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder(mapId, binding)
            .AddCell(default, Cell)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }
}
