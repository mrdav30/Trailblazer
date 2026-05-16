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
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_ShouldStoreAndQueryChartToChartTransition()
    {
        Vector3d source = Vector3d.Zero;
        Vector3d destination = new(1, 0, 0);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryChartSource", source);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryChartDestination", destination);

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
            out WorldVoxelIndex sourceVoxelIndex,
            out WorldVoxelIndex destinationVoxelIndex));

        Voxel sourceVoxel = TestRequire.VoxelAt(TestWorld.Context, source);
        Voxel destinationVoxel = TestRequire.VoxelAt(TestWorld.Context, destination);
        Assert.Equal(sourceVoxel.WorldIndex, sourceVoxelIndex);
        Assert.Equal(destinationVoxel.WorldIndex, destinationVoxelIndex);
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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryPointOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryPointOverrideDestination", new Vector3d(1, 0, 0));

        Voxel sourceVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel destinationVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        Vector3d pointOverride = sourceVoxel.WorldPosition + new Vector3d(
            TestWorld.Context.VoxelSize / 4,
            Fixed64.Zero,
            Fixed64.Zero);
        Voxel overrideVoxel = TestRequire.VoxelAt(TestWorld.Context, pointOverride);
        Assert.Equal(sourceVoxel.WorldIndex, overrideVoxel.WorldIndex);

        var transition = new TraversalTransition(
            id: "voxel-override",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(sourceVoxel.WorldPosition, pointOverride),
            destination: TraversalTransitionAnchor.Solid(destinationVoxel.WorldPosition));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Assert.True(TraversalTransitionRegistry.TryGet("voxel-override", out TraversalTransition storedTransition));
        Assert.True(storedTransition.Source.HasPointOverride);
        Assert.Equal(sourceVoxel.WorldIndex, storedTransition.Source.VoxelIndex);
        Assert.Equal(pointOverride, storedTransition.Source.Position);

        Vector3d alternateQueryPoint = sourceVoxel.WorldPosition + new Vector3d(
            Fixed64.Zero,
            TestWorld.Context.VoxelSize / 4,
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
    public void LookupAndMutationApis_ShouldGracefullyIgnoreMissingTransitionsAndNoOpRequests()
    {
        int versionBefore = TraversalTransitionRegistry.RegistryVersion;

        Assert.False(TraversalTransitionRegistry.TryGet("missing-transition", out _));
        Assert.False(TraversalTransitionRegistry.TryGetResolvedEndpoints(
            "missing-transition",
            out _,
            out _));
        Assert.False(TraversalTransitionRegistry.Unregister("missing-transition"));
        Assert.Empty(TraversalTransitionRegistry.GetIncomingTransitions(new Vector3d(64, 0, 0)));

        TraversalTransitionRegistry.UnregisterRange(new[] { "missing-transition" }, count: 0);
        TraversalTransitionRegistry.UnregisterRange(new[] { "missing-transition" }, count: 4);
        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(Array.Empty<string>(), suppressed: true);
        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { "missing-transition" },
            suppressed: true,
            count: 0);

        Assert.Equal(versionBefore, TraversalTransitionRegistry.RegistryVersion);
    }

    [Fact]
    public void Register_ShouldRejectDuplicateManualTransitions_WhenEffectiveSemanticsMatch()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryDuplicateSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryDuplicateDestination", new Vector3d(1, 0, 0));

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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryDistinctOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryDistinctOverrideDestination", new Vector3d(1, 0, 0));

        Voxel sourceVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        Vector3d pointOverride = sourceVoxel.WorldPosition + new Vector3d(
            TestWorld.Context.VoxelSize / 4,
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
    public void Register_ShouldRejectTransition_WhenDestinationGridIsRemovedAfterAnchorCreation()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryRemovedGridSource", Vector3d.Zero);
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(14, 4, 4)),
            out ushort removedGridIndex));
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryRemovedGridDestination", new Vector3d(10, 0, 0));

        var transition = new TraversalTransition(
            id: "removed-grid-destination",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(10, 0, 0)));

        Assert.True(TestWorld.World.TryRemoveGrid(removedGridIndex));
        Assert.False(TraversalTransitionRegistry.Register(transition));
        Assert.False(TraversalTransitionRegistry.IsRegistered("removed-grid-destination"));
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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryOverrideSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryOverrideDestination", new Vector3d(1, 0, 0));

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
        Assert.True(TraversalTransitionRegistry.IsSuppressed("generated-suppressed"));
        Assert.False(TraversalTransitionRegistry.IsActive("generated-suppressed"));
        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));

        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { "generated-suppressed" },
            suppressed: false);

        Assert.False(TraversalTransitionRegistry.IsSuppressed("generated-suppressed"));
        Assert.True(TraversalTransitionRegistry.IsActive("generated-suppressed"));
        Assert.Single(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
    }

    [Fact]
    public void RegisteredTraversalTransition_EqualsObject_ShouldCompareTransitionIdentity()
    {
        var transition = new TraversalTransition(
            id: "registered-equals",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 3);
        var registered = new RegisteredTraversalTransition(
            transition,
            TraversalTransitionOwnershipKind.ManagedManual,
            priority: 1,
            registrationOrder: 2);

        Assert.True(registered.Equals((object)registered));
        Assert.False(registered.Equals("registered-equals"));
    }

    [Fact]
    public void SetManagedTransitionsSuppressed_ShouldIgnoreInvalidIdsAndCountOverflow()
    {
        var generated = new TraversalTransition(
            id: "generated-noop-suppression",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(generated));
        int versionBeforeSuppression = TraversalTransitionRegistry.RegistryVersion;

        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { string.Empty, "missing-transition", generated.Id },
            suppressed: true,
            count: 6);

        Assert.True(TraversalTransitionRegistry.IsRegistered(generated.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(generated.Id));
        Assert.True(TraversalTransitionRegistry.RegistryVersion > versionBeforeSuppression);

        int versionBeforeNoOp = TraversalTransitionRegistry.RegistryVersion;
        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { generated.Id },
            suppressed: true,
            count: 4);
        Assert.Equal(versionBeforeNoOp, TraversalTransitionRegistry.RegistryVersion);

        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            new[] { string.Empty, "missing-transition", generated.Id },
            suppressed: false,
            count: 6);

        Assert.True(TraversalTransitionRegistry.IsActive(generated.Id));
    }

    [Fact]
    public void PathManagerReset_ShouldClearTransitionRegistry()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryResetSource", Vector3d.Zero);
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(0, 1, 0), TraversalMedium.Gas, "RegistryResetGas");

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

    [Fact]
    public void RegisterGeneratedRange_ShouldValidateInputAndRollbackOnFailure()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TraversalTransitionRegistry.RegisterGeneratedRange(null!, priority: 0));

        Assert.True(TraversalTransitionRegistry.RegisterGeneratedRange(Array.Empty<TraversalTransition>(), priority: 0));

        var unresolved = new TraversalTransition(
            id: "generated-invalid",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(default(WorldVoxelIndex)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.False(TraversalTransitionRegistry.RegisterGeneratedRange(new[] { unresolved }, priority: 0));
        Assert.False(TraversalTransitionRegistry.IsRegistered("generated-invalid"));

        var preexisting = new TraversalTransition(
            id: "generated-existing",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var rolledBack = new TraversalTransition(
            id: "generated-rolled-back",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)));

        Assert.True(TraversalTransitionRegistry.RegisterGenerated(preexisting));
        Assert.False(TraversalTransitionRegistry.RegisterGeneratedRange(
            new[] { rolledBack, preexisting },
            priority: 0));
        Assert.True(TraversalTransitionRegistry.IsRegistered("generated-existing"));
        Assert.False(TraversalTransitionRegistry.IsRegistered("generated-rolled-back"));
    }

    [Fact]
    public void RegisterAndUnregister_ShouldSupportSameVoxelManualTransitions()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryLoopPoint", Vector3d.Zero);

        var transition = new TraversalTransition(
            id: "loop-link",
            type: TraversalTransitionType.Custom,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(Vector3d.Zero));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        TraversalTransition[] incoming = TraversalTransitionRegistry.GetIncomingTransitions(Vector3d.Zero);
        TraversalTransition[] touching = TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(voxel.GridIndex);

        Assert.Single(outgoing);
        Assert.Single(incoming);
        Assert.Single(touching);
        Assert.Equal("loop-link", outgoing[0].Id);
        Assert.Equal("loop-link", incoming[0].Id);
        Assert.Equal("loop-link", touching[0].Id);

        Assert.True(TraversalTransitionRegistry.Unregister("loop-link"));
        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
        Assert.Empty(TraversalTransitionRegistry.GetIncomingTransitions(Vector3d.Zero));
    }

    [Fact]
    public void GetActiveTransitionsTouchingGrid_ShouldDeduplicateTransitionsReferencedByBothEndpoints()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryGridSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "RegistryGridDestination", new Vector3d(1, 0, 0));

        var transition = new TraversalTransition(
            id: "same-grid",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(transition));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        TraversalTransition[] touching = TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(voxel.GridIndex);

        Assert.Single(touching);
        Assert.Equal("same-grid", touching[0].Id);
        Assert.Empty(TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(99));
    }
}
