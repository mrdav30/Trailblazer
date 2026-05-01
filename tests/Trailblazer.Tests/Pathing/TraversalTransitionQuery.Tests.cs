using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class TraversalTransitionQueryTests : IDisposable
{
    public TraversalTransitionQueryTests()
    {
        TrailblazerWorldManager.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GridScopedQueries_ShouldReturnOnlyDirectionsRelevantToThatGrid()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(16, -4, -4), new Vector3d(8, 8, 8)),
            out _));

        Vector3d firstGridPosition = Vector3d.Zero;
        Vector3d secondGridPosition = new(16, 0, 0);
        PathTestFactory.RegisterSingleWalkablePoint("QueryFirstGridPoint", firstGridPosition);
        PathTestFactory.RegisterSingleWalkablePoint("QuerySecondGridPoint", secondGridPosition);

        Voxel firstGridVoxel = TestRequire.VoxelAt(firstGridPosition);
        Voxel secondGridVoxel = TestRequire.VoxelAt(secondGridPosition);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cross-grid-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(firstGridPosition),
            destination: TraversalTransitionAnchor.Solid(secondGridPosition),
            pathCostModifier: 2,
            isBidirectional: true)));

        TraversalTransition[] fromFirstGrid = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(firstGridVoxel.GridIndex);
        TraversalTransition[] fromSecondGrid = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(secondGridVoxel.GridIndex);
        TraversalTransition[] toFirstGrid = TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(firstGridVoxel.GridIndex);
        TraversalTransition[] toSecondGrid = TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(secondGridVoxel.GridIndex);

        Assert.Single(fromFirstGrid);
        Assert.Equal(firstGridPosition, fromFirstGrid[0].Source.Position);
        Assert.Equal(secondGridPosition, fromFirstGrid[0].Destination.Position);

        Assert.Single(fromSecondGrid);
        Assert.Equal(secondGridPosition, fromSecondGrid[0].Source.Position);
        Assert.Equal(firstGridPosition, fromSecondGrid[0].Destination.Position);

        Assert.Single(toFirstGrid);
        Assert.Equal(secondGridPosition, toFirstGrid[0].Source.Position);
        Assert.Equal(firstGridPosition, toFirstGrid[0].Destination.Position);

        Assert.Single(toSecondGrid);
        Assert.Equal(firstGridPosition, toSecondGrid[0].Source.Position);
        Assert.Equal(secondGridPosition, toSecondGrid[0].Destination.Position);
    }

    [Fact]
    public void GridScopedQueries_ShouldRefreshWhenRegistryVersionChanges()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d mid = new(1, 0, 0);
        Vector3d end = new(2, 0, 0);
        PathTestFactory.RegisterSingleWalkablePoint("QueryRefreshSource", source);
        PathTestFactory.RegisterSingleWalkablePoint("QueryRefreshMid", mid);
        PathTestFactory.RegisterSingleWalkablePoint("QueryRefreshEnd", end);

        Voxel sourceVoxel = TestRequire.VoxelAt(source);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cache-refresh-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(source),
            destination: TraversalTransitionAnchor.Solid(mid))));

        TraversalTransition[] before = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Single(before);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cache-refresh-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(mid),
            destination: TraversalTransitionAnchor.Solid(end))));

        TraversalTransition[] after = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Equal(2, after.Length);
    }

    [Fact]
    public void FilteredDirectedQueries_ShouldReturnOnlyRequestedTypeAndMediumPairViews()
    {
        Vector3d solidSource = Vector3d.Zero;
        Vector3d liquidPoint = new(1, 0, 0);
        Vector3d solidDestination = new(2, 0, 0);

        PathTestFactory.RegisterSingleWalkablePoint("QueryFilteredSolidSource", solidSource);
        PathTestFactory.RegisterSingleWalkablePoint("QueryFilteredSolidDestination", solidDestination);
        PathTestFactory.RegisterGeneratedVolumePoint(liquidPoint, TraversalMedium.Liquid, "QueryFilteredLiquid");

        Voxel sourceVoxel = TestRequire.VoxelAt(solidSource);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "filtered-swim",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(solidSource),
            destination: TraversalTransitionAnchor.Liquid(liquidPoint),
            isBidirectional: true)));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "filtered-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(solidSource),
            destination: TraversalTransitionAnchor.Solid(solidDestination))));

        TraversalTransition[] jumpTransitions = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
            sourceVoxel.GridIndex,
            TraversalTransitionType.Jump);
        TraversalTransition[] solidToLiquid = TraversalTransitionQuery.GetDirectedTransitions(
            TraversalMedium.Solid,
            TraversalMedium.Liquid);
        TraversalTransition[] liquidToSolid = TraversalTransitionQuery.GetDirectedTransitions(
            TraversalMedium.Liquid,
            TraversalMedium.Solid);

        Assert.Single(jumpTransitions);
        Assert.Equal("filtered-jump", jumpTransitions[0].Id);

        Assert.Single(solidToLiquid);
        Assert.Equal("filtered-swim", solidToLiquid[0].Id);
        Assert.Equal(TraversalMedium.Solid, solidToLiquid[0].Source.Medium);
        Assert.Equal(TraversalMedium.Liquid, solidToLiquid[0].Destination.Medium);

        Assert.Single(liquidToSolid);
        Assert.Equal("filtered-swim", liquidToSolid[0].Id);
        Assert.Equal(TraversalMedium.Liquid, liquidToSolid[0].Source.Medium);
        Assert.Equal(TraversalMedium.Solid, liquidToSolid[0].Destination.Medium);
    }

    [Fact]
    public void FilteredGridQueries_ShouldRefreshWhenRegistryVersionChanges()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d mid = new(1, 0, 0);
        Vector3d end = new(2, 0, 0);
        PathTestFactory.RegisterSingleWalkablePoint("QueryFilteredRefreshSource", source);
        PathTestFactory.RegisterSingleWalkablePoint("QueryFilteredRefreshMid", mid);
        PathTestFactory.RegisterSingleWalkablePoint("QueryFilteredRefreshEnd", end);

        Voxel sourceVoxel = TestRequire.VoxelAt(source);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "filtered-refresh-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(source),
            destination: TraversalTransitionAnchor.Solid(mid))));

        TraversalTransition[] before = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
            sourceVoxel.GridIndex,
            TraversalTransitionType.Jump);
        Assert.Single(before);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "filtered-refresh-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(mid),
            destination: TraversalTransitionAnchor.Solid(end))));

        TraversalTransition[] after = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
            sourceVoxel.GridIndex,
            TraversalTransitionType.Jump);
        Assert.Equal(2, after.Length);
    }

    [Fact]
    public void DirectedQueries_ShouldOnlyUseActiveTransitions_WhenManualOverrideChanges()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d destination = new(1, 0, 0);
        PathTestFactory.RegisterSingleWalkablePoint("QueryOverrideSource", source);
        PathTestFactory.RegisterSingleWalkablePoint("QueryOverrideDestination", destination);

        Voxel sourceVoxel = TestRequire.VoxelAt(source);

        var generated = new TraversalTransition(
            id: "generated-directed",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(source),
            destination: TraversalTransitionAnchor.Solid(destination),
            pathCostModifier: 2);

        var manual = new TraversalTransition(
            id: "manual-directed",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(source),
            destination: TraversalTransitionAnchor.Solid(destination),
            pathCostModifier: 2);

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(generated));
        TraversalTransition[] before = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Single(before);
        Assert.Equal("generated-directed", before[0].Id);

        Assert.True(TraversalTransitionRegistry.Register(manual));
        TraversalTransition[] overridden = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Single(overridden);
        Assert.Equal("manual-directed", overridden[0].Id);

        Assert.True(TraversalTransitionRegistry.Unregister("manual-directed"));
        TraversalTransition[] reactivated = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Single(reactivated);
        Assert.Equal("generated-directed", reactivated[0].Id);
    }

    [Fact]
    public void GetDirectedTransitions_ShouldReturnEmptyAfterPathManagerReset()
    {
        PathTestFactory.RegisterSingleWalkablePoint("QueryStaleA", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("QueryStaleB", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("QueryStaleC", new Vector3d(2, 0, 0));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "stale-grid-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "stale-grid-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)))));

        PathManager.Reset();

        Exception? exception = Record.Exception(() => TraversalTransitionQuery.GetDirectedTransitions());
        Assert.Null(exception);
        Assert.Empty(TraversalTransitionQuery.GetDirectedTransitions());
    }

    [Fact]
    public void QueryCaches_ShouldReuseComputedViews_AndSupportEmptySnapshots()
    {
        TraversalTransition[] emptyFirst = TraversalTransitionQuery.GetDirectedTransitions(TraversalTransitionType.Jump);
        TraversalTransition[] emptySecond = TraversalTransitionQuery.GetDirectedTransitions(TraversalTransitionType.Jump);

        Assert.Empty(emptyFirst);
        Assert.Same(emptyFirst, emptySecond);

        PathTestFactory.RegisterSingleWalkablePoint("QueryCachedSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("QueryCachedDestination", new Vector3d(1, 0, 0));
        Voxel sourceVoxel = TestRequire.VoxelAt(Vector3d.Zero);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cached-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))));

        TraversalTransition[] byTypeFirst = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
            sourceVoxel.GridIndex,
            TraversalTransitionType.Jump);
        TraversalTransition[] byTypeSecond = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(
            sourceVoxel.GridIndex,
            TraversalTransitionType.Jump);

        Assert.Single(byTypeFirst);
        Assert.Same(byTypeFirst, byTypeSecond);

        int[] sourceGridIndicesFirst = TraversalTransitionQuery.GetSourceGridIndices(TraversalTransitionType.Jump);
        int[] sourceGridIndicesSecond = TraversalTransitionQuery.GetSourceGridIndices(TraversalTransitionType.Jump);

        Assert.Single(sourceGridIndicesFirst);
        Assert.Same(sourceGridIndicesFirst, sourceGridIndicesSecond);
        Assert.Equal(sourceVoxel.GridIndex, sourceGridIndicesFirst[0]);
    }
}
