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
        Assert.True(TraversalTransitionRegistry.IsActive("jump-link"));
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
            destination: TraversalTransitionAnchor.WaterVolume(new Vector3d(0, 0, 1)),
            pathCostModifier: 2);

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.TryGet("shoreline-entry", out TraversalTransition storedTransition));
        Assert.Equal(TraversalTransitionAnchorSpace.Chart, storedTransition.Source.Space);
        Assert.Equal(TraversalTransitionAnchorSpace.WaterVolume, storedTransition.Destination.Space);
        Assert.True(storedTransition.Destination.TryGetVolumeTraversalMode(out VolumeTraversalMode volumeMode));
        Assert.Equal(VolumeTraversalMode.Water, volumeMode);
    }

    [Fact]
    public void Register_ShouldSupportVoxelScopedAnchorsWithPointOverrides()
    {
        Assert.True(GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel sourceVoxel));
        Assert.True(GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel destinationVoxel));

        Vector3d pointOverride = sourceVoxel.WorldPosition + new Vector3d(
            GlobalGridManager.VoxelSize / 4,
            Fixed64.Zero,
            Fixed64.Zero);
        Assert.True(GlobalGridManager.TryGetVoxel(pointOverride, out Voxel overrideVoxel));
        Assert.Equal(sourceVoxel.GlobalIndex, overrideVoxel.GlobalIndex);

        var transition = new TraversalTransition(
            id: "voxel-override",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(sourceVoxel.WorldPosition, pointOverride),
            destination: TraversalTransitionAnchor.Chart(destinationVoxel.WorldPosition));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.TryGet("voxel-override", out TraversalTransition storedTransition));
        Assert.True(storedTransition.Source.HasPointOverride);
        Assert.Equal(sourceVoxel.GlobalIndex, storedTransition.Source.VoxelIndex);
        Assert.Equal(pointOverride, storedTransition.Source.Position);

        Vector3d alternateQueryPoint = sourceVoxel.WorldPosition + new Vector3d(
            Fixed64.Zero,
            GlobalGridManager.VoxelSize / 4,
            Fixed64.Zero);

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(alternateQueryPoint);
        Assert.Single(outgoing);
        Assert.Equal("voxel-override", outgoing[0].Id);
    }

    [Fact]
    public void ChartAnchor_ShouldRejectPointOverridesOutsideTheResolvedVoxel()
    {
        Assert.Throws<ArgumentException>(() =>
            TraversalTransitionAnchor.Chart(Vector3d.Zero, new Vector3d(1, 0, 0)));
    }

    [Fact]
    public void ChartAnchor_ShouldRejectMissingVoxelPositions()
    {
        Assert.Throws<ArgumentException>(() => TraversalTransitionAnchor.Chart(new Vector3d(64, 0, 0)));
    }

    [Fact]
    public void Register_ShouldRejectDuplicateIds()
    {
        var valid = new TraversalTransition(
            id: "duplicate-check",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(valid));
        Assert.False(TraversalTransitionRegistry.Register(valid));
    }

    [Fact]
    public void Register_ShouldRejectDuplicateManualTransitions_WhenEffectiveSemanticsMatch()
    {
        var first = new TraversalTransition(
            id: "manual-duplicate-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 3);

        var duplicate = new TraversalTransition(
            id: "manual-duplicate-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero, Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            pathCostModifier: 3);

        Assert.True(TraversalTransitionRegistry.Register(first));
        Assert.False(TraversalTransitionRegistry.Register(duplicate));
        Assert.True(TraversalTransitionRegistry.IsRegistered("manual-duplicate-a"));
        Assert.True(TraversalTransitionRegistry.IsActive("manual-duplicate-a"));
        Assert.False(TraversalTransitionRegistry.IsRegistered("manual-duplicate-b"));
        Assert.False(TraversalTransitionRegistry.IsActive("manual-duplicate-b"));
        Assert.Single(TraversalTransitionRegistry.AllTransitions);
    }

    [Fact]
    public void Register_ShouldAllowDistinctPointOverrideTransitionsToCoexist()
    {
        Assert.True(GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel sourceVoxel));

        Vector3d pointOverride = sourceVoxel.WorldPosition + new Vector3d(
            GlobalGridManager.VoxelSize / 4,
            Fixed64.Zero,
            Fixed64.Zero);

        var defaultTransition = new TraversalTransition(
            id: "point-default",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)));

        var offsetTransition = new TraversalTransition(
            id: "point-offset",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero, pointOverride),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(defaultTransition));
        Assert.True(TraversalTransitionRegistry.Register(offsetTransition));
        Assert.True(TraversalTransitionRegistry.IsActive("point-default"));
        Assert.True(TraversalTransitionRegistry.IsActive("point-offset"));

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Equal(2, outgoing.Length);
        Assert.Contains(outgoing, transition => transition.Id == "point-default");
        Assert.Contains(outgoing, transition => transition.Id == "point-offset");
    }

    [Fact]
    public void RegisterGenerated_ShouldRemainInactive_WhenEquivalentGeneratedAlreadyExists_ThenReactivateAfterRemoval()
    {
        var first = new TraversalTransition(
            id: "generated-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 2);

        var second = new TraversalTransition(
            id: "generated-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 2);

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(first));
        Assert.True(TraversalTransitionRegistry.RegisterGenerated(second));
        Assert.True(TraversalTransitionRegistry.IsActive("generated-a"));
        Assert.False(TraversalTransitionRegistry.IsActive("generated-b"));
        Assert.Single(TraversalTransitionRegistry.AllTransitions);
        Assert.True(TraversalTransitionRegistry.TryGet("generated-b", out TraversalTransition inactiveGenerated));
        Assert.Equal("generated-b", inactiveGenerated.Id);

        Assert.True(TraversalTransitionRegistry.Unregister("generated-a"));
        Assert.True(TraversalTransitionRegistry.IsActive("generated-b"));
        Assert.Single(TraversalTransitionRegistry.AllTransitions);
        Assert.Equal("generated-b", TraversalTransitionRegistry.AllTransitions[0].Id);
    }

    [Fact]
    public void Register_ShouldOverrideEquivalentGeneratedTransition_WithoutUnregisteringIt()
    {
        var generated = new TraversalTransition(
            id: "generated-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 1);

        var manual = new TraversalTransition(
            id: "manual-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 1);

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(generated));
        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(TraversalTransitionRegistry.IsRegistered("generated-link"));
        Assert.False(TraversalTransitionRegistry.IsActive("generated-link"));
        Assert.True(TraversalTransitionRegistry.IsActive("manual-link"));

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Single(outgoing);
        Assert.Equal("manual-link", outgoing[0].Id);

        Assert.True(TraversalTransitionRegistry.Unregister("manual-link"));
        Assert.True(TraversalTransitionRegistry.IsActive("generated-link"));
        Assert.Single(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
        Assert.Equal("generated-link", TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero)[0].Id);
    }

    [Fact]
    public void PathManagerReset_ShouldClearTransitionRegistry()
    {
        var transition = new TraversalTransition(
            id: "reset-check",
            type: TraversalTransitionType.Takeoff,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.OpenVolume(new Vector3d(0, 1, 0)));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.NotEmpty(TraversalTransitionRegistry.AllTransitions);

        PathManager.Reset();

        Assert.False(TraversalTransitionRegistry.IsRegistered("reset-check"));
        Assert.Empty(TraversalTransitionRegistry.AllTransitions);
    }
}
