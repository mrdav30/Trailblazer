using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using Trailblazer.Pathing;
using Trailblazer.Tests;
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
        PathTestFactory.RegisterSingleWalkablePoint("RegistryChartSource", source);
        PathTestFactory.RegisterSingleWalkablePoint("RegistryChartDestination", destination);

        var transition = new TraversalTransition(
            id: "jump-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(source),
            destination: TraversalTransitionAnchor.Solid(destination),
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
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(0, 0, 1)),
            pathCostModifier: 2);

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.TryGet("shoreline-entry", out TraversalTransition storedTransition));
        Assert.Equal(TraversalMedium.Solid, storedTransition.Source.Medium);
        Assert.Equal(TraversalMedium.Liquid, storedTransition.Destination.Medium);
        Assert.True(storedTransition.Destination.TryGetVolumeMedium(out TraversalMedium volumeMode));
        Assert.Equal(TraversalMedium.Liquid, volumeMode);
    }

    [Fact]
    public void Register_ShouldSupportVoxelScopedAnchorsWithPointOverrides()
    {
        PathTestFactory.RegisterSingleWalkablePoint("RegistryPointOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("RegistryPointOverrideDestination", new Vector3d(1, 0, 0));

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
            source: TraversalTransitionAnchor.Solid(sourceVoxel.WorldPosition, pointOverride),
            destination: TraversalTransitionAnchor.Solid(destinationVoxel.WorldPosition));

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
            TraversalTransitionAnchor.Solid(Vector3d.Zero, new Vector3d(1, 0, 0)));
    }

    [Fact]
    public void ChartAnchor_ShouldRejectMissingVoxelPositions()
    {
        Assert.Throws<ArgumentException>(() => TraversalTransitionAnchor.Solid(new Vector3d(64, 0, 0)));
    }

    [Fact]
    public void Register_ShouldRejectDuplicateIds()
    {
        var valid = new TraversalTransition(
            id: "duplicate-check",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(valid));
        Assert.False(TraversalTransitionRegistry.Register(valid));
    }

    [Fact]
    public void Register_ShouldRejectDuplicateManualTransitions_WhenEffectiveSemanticsMatch()
    {
        PathTestFactory.RegisterSingleWalkablePoint("RegistryDuplicateSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("RegistryDuplicateDestination", new Vector3d(1, 0, 0));

        var first = new TraversalTransition(
            id: "manual-duplicate-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 3);

        var duplicate = new TraversalTransition(
            id: "manual-duplicate-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero, Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
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
        PathTestFactory.RegisterSingleWalkablePoint("RegistryDistinctOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("RegistryDistinctOverrideDestination", new Vector3d(1, 0, 0));

        Assert.True(GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel sourceVoxel));

        Vector3d pointOverride = sourceVoxel.WorldPosition + new Vector3d(
            GlobalGridManager.VoxelSize / 4,
            Fixed64.Zero,
            Fixed64.Zero);

        var defaultTransition = new TraversalTransition(
            id: "point-default",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        var offsetTransition = new TraversalTransition(
            id: "point-offset",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero, pointOverride),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

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
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2);

        var second = new TraversalTransition(
            id: "generated-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
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
        PathTestFactory.RegisterSingleWalkablePoint("RegistryOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("RegistryOverrideDestination", new Vector3d(1, 0, 0));

        var generated = new TraversalTransition(
            id: "generated-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1);

        var manual = new TraversalTransition(
            id: "manual-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
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
    public void Register_ShouldPreferHigherPriorityTransition_RegardlessOfOwnershipKind()
    {
        var generated = new TraversalTransition(
            id: "generated-priority",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        var manual = new TraversalTransition(
            id: "manual-priority",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(manual, priority: 1));
        Assert.True(TraversalTransitionRegistry.RegisterGenerated(generated, priority: 3));

        Assert.True(TraversalTransitionRegistry.IsActive("generated-priority"));
        Assert.False(TraversalTransitionRegistry.IsActive("manual-priority"));
        Assert.Single(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
        Assert.Equal("generated-priority", TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero)[0].Id);
    }

    [Fact]
    public void ManagedSuppression_ShouldKeepTransitionRegistered_WhileRemovingItFromActiveQueries()
    {
        var generated = new TraversalTransition(
            id: "generated-suppressed",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(generated, startSuppressed: true));
        Assert.True(TraversalTransitionRegistry.IsRegistered("generated-suppressed"));
        Assert.False(TraversalTransitionRegistry.IsActive("generated-suppressed"));
        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));

        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { "generated-suppressed" },
            suppressed: false);

        Assert.True(TraversalTransitionRegistry.IsActive("generated-suppressed"));
        Assert.Single(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
    }

    [Fact]
    public void PathManagerReset_ShouldClearTransitionRegistry()
    {
        PathTestFactory.RegisterSingleWalkablePoint("RegistryResetSource", Vector3d.Zero);
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(0, 1, 0), TraversalMedium.Gas, "RegistryResetGas");

        var transition = new TraversalTransition(
            id: "reset-check",
            type: TraversalTransitionType.Takeoff,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Gas(new Vector3d(0, 1, 0)));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.NotEmpty(TraversalTransitionRegistry.AllTransitions);

        PathManager.Reset();

        Assert.False(TraversalTransitionRegistry.IsRegistered("reset-check"));
        Assert.Empty(TraversalTransitionRegistry.AllTransitions);
    }
}
