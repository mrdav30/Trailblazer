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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GridScopedQueries_ShouldReturnOnlyDirectionsRelevantToThatGrid()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(16, -4, -4), new Vector3d(8, 8, 8)),
            out _));

        Vector3d firstGridPosition = Vector3d.Zero;
        Vector3d secondGridPosition = new(16, 0, 0);

        Assert.True(GlobalGridManager.TryGetVoxel(firstGridPosition, out Voxel firstGridVoxel));
        Assert.True(GlobalGridManager.TryGetVoxel(secondGridPosition, out Voxel secondGridVoxel));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cross-grid-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(firstGridPosition),
            destination: TraversalTransitionAnchor.Chart(secondGridPosition),
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

        Assert.True(GlobalGridManager.TryGetVoxel(source, out Voxel sourceVoxel));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cache-refresh-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(source),
            destination: TraversalTransitionAnchor.Chart(mid))));

        TraversalTransition[] before = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Single(before);

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "cache-refresh-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(mid),
            destination: TraversalTransitionAnchor.Chart(end))));

        TraversalTransition[] after = TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceVoxel.GridIndex);
        Assert.Equal(2, after.Length);
    }

    [Fact]
    public void DirectedQueries_ShouldOnlyUseActiveTransitions_WhenManualOverrideChanges()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d destination = new(1, 0, 0);

        Assert.True(GlobalGridManager.TryGetVoxel(source, out Voxel sourceVoxel));

        var generated = new TraversalTransition(
            id: "generated-directed",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(source),
            destination: TraversalTransitionAnchor.Chart(destination),
            pathCostModifier: 2);

        var manual = new TraversalTransition(
            id: "manual-directed",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(source),
            destination: TraversalTransitionAnchor.Chart(destination),
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
}
