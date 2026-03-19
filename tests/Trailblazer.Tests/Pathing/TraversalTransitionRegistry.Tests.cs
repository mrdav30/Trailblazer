using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class TraversalTransitionRegistryTests : IDisposable
{
    public TraversalTransitionRegistryTests()
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
    public void Register_ShouldStoreAndQueryChartToChartTransition()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d destination = new(1, 0, 0);

        var transition = new TraversalTransition(
            id: "jump-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(source),
            destination: TraversalTransitionAnchor.Chart(destination),
            pathCostModifier: 3,
            isBidirectional: true);

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.IsRegistered("jump-link"));
        Assert.True(TraversalTransitionRegistry.TryGet("jump-link", out TraversalTransition storedTransition));
        Assert.Equal(TraversalTransitionType.Jump, storedTransition.Type);
        Assert.True(storedTransition.IsBidirectional);
        Assert.Equal(3, storedTransition.PathCostModifier);

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(source);
        TraversalTransition[] incoming = TraversalTransitionRegistry.GetIncomingTransitions(destination);

        Assert.Single(outgoing);
        Assert.Single(incoming);
        Assert.Equal("jump-link", outgoing[0].Id);
        Assert.Equal("jump-link", incoming[0].Id);

        Assert.True(TraversalTransitionRegistry.TryGetResolvedEndpoints(
            "jump-link",
            out GlobalVoxelIndex sourceVoxelIndex,
            out GlobalVoxelIndex destinationVoxelIndex));

        Assert.True(GlobalGridManager.TryGetVoxel(source, out Voxel sourceVoxel));
        Assert.True(GlobalGridManager.TryGetVoxel(destination, out Voxel destinationVoxel));
        Assert.Equal(sourceVoxel.GlobalIndex, sourceVoxelIndex);
        Assert.Equal(destinationVoxel.GlobalIndex, destinationVoxelIndex);
    }

    [Fact]
    public void Register_ShouldSupportChartToVolumeTransitionShape()
    {
        var transition = new TraversalTransition(
            id: "shoreline-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Volume(new Vector3d(0, 0, 1), VolumeTraversalMode.Water),
            pathCostModifier: 2);

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.TryGet("shoreline-entry", out TraversalTransition storedTransition));
        Assert.Equal(TraversalTransitionAnchorKind.Chart, storedTransition.Source.Kind);
        Assert.Equal(TraversalTransitionAnchorKind.Volume, storedTransition.Destination.Kind);
        Assert.Equal(VolumeTraversalMode.Water, storedTransition.Destination.VolumeMode);
    }

    [Fact]
    public void Register_ShouldRejectDuplicateIds_AndMissingVoxels()
    {
        var valid = new TraversalTransition(
            id: "duplicate-check",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)));

        var missingVoxel = new TraversalTransition(
            id: "missing-voxel",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(64, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(valid));
        Assert.False(TraversalTransitionRegistry.Register(valid));
        Assert.False(TraversalTransitionRegistry.Register(missingVoxel));
    }

    [Fact]
    public void PathManagerReset_ShouldClearTransitionRegistry()
    {
        var transition = new TraversalTransition(
            id: "reset-check",
            type: TraversalTransitionType.Takeoff,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Volume(new Vector3d(0, 1, 0), VolumeTraversalMode.Open));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.NotEmpty(TraversalTransitionRegistry.AllTransitions);

        PathManager.Reset();

        Assert.False(TraversalTransitionRegistry.IsRegistered("reset-check"));
        Assert.Empty(TraversalTransitionRegistry.AllTransitions);
    }
}
