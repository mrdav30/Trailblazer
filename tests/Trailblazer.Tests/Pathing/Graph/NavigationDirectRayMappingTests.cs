using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationDirectRayTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MappingReuse_ShouldPreserveEveryDebitAtExactAndOneBelowBudgets(int category)
    {
        using var scene = new Scene(endZ: 5);
        var warm = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        NavigationRayRequest request = scene.Request(reverse: true);
        CompareMappingAttempt(warm, request, Scene.Budget).Should().Be(NavigationRayStatus.Success);
        int[] costs = { warm.WorkMeter.LookupProbes, warm.WorkMeter.CoveredVoxelIntervals,
            warm.WorkMeter.TraceIntervals, warm.WorkMeter.EvaluatedEdges };
        costs[category].Should().BeGreaterThan(0);

        for (int difference = 0; difference <= 1; difference++)
        {
            int limit = costs[category] - difference;
            var budget = new NavigationWorkBudget(
                category == 0 ? limit : 65536, 0, 0,
                category == 3 ? limit : 65536, 4096,
                0, 0, 0, category == 2 ? limit : 256, category == 1 ? limit : 256, 0);
            CompareMappingAttempt(warm, request, budget).Should().Be(difference == 0
                ? NavigationRayStatus.Success : NavigationRayStatus.BudgetExceeded);
            CompareMappingAttempt(warm, request, Scene.Budget).Should().Be(NavigationRayStatus.Success);
        }
    }

    [Theory]
    [InlineData(0, 32, 256, 256, false)]
    [InlineData(32, 0, 256, 256, false)]
    [InlineData(1, 1, 256, 256, false)]
    [InlineData(2, 1, 256, 256, true)]
    [InlineData(3, 1, 256, 256, true)]
    [InlineData(32, 32, 83, 16, false)]
    [InlineData(32, 32, 84, 16, true)]
    [InlineData(32, 32, 256, 15, false)]
    [InlineData(32, 32, 256, 16, true)]
    public void MappingReuse_ShouldPreservePartialDependenciesAtScratchBoundaries(
        int pages, int components, int covered, int traces, bool succeeds)
    {
        using var scene = new Scene(endZ: 5);
        var warm = new NavigationImmediateRayWorkspace(1, pages, components, covered, traces);
        NavigationRayStatus expected = succeeds ? NavigationRayStatus.Success : NavigationRayStatus.CapacityExceeded;
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(expected);
        CompareMappingAttempt(warm, scene.Request(reverse: true), Scene.Budget).Should().Be(expected);
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(expected);
    }

    [Fact]
    public void MappingReuse_ShouldDistinguishCollidingVoxelKeysWithDifferentAdmission()
    {
        using var scene = new Scene(wallKind: 2, wallX: 7, maxX: 24);
        var blockedIndex = new VoxelIndex(7, 0, 3);
        var clearIndex = new VoxelIndex(24, 0, 0);
        // Both hashes select slot 376 with 1,024 entries (and also collide at 256/512).
        (blockedIndex.GetHashCode() & (NavigationRayMappingMemo.Capacity - 1)).Should()
            .Be(clearIndex.GetHashCode() & (NavigationRayMappingMemo.Capacity - 1));
        var warm = scene.Context.Pathing.ImmediateRayWorkspace;
        var binding = scene.Graph.GetInstance(0).Map.GridBinding;
        Vector3d blocked = GuidedPathTestScene.Anchor(binding, blockedIndex);
        Vector3d clear = GuidedPathTestScene.Anchor(binding, clearIndex);
        var offset = new Vector3d(Fixed64.One / 4, Fixed64.Zero, Fixed64.One / 4);
        NavigationRayRequest blockedRay = Copy(scene.Request(), start: blocked, end: blocked + offset);
        NavigationRayRequest clearRay = Copy(scene.Request(), start: clear, end: clear + offset);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            CompareMappingAttempt(warm, blockedRay, Scene.Budget).Should().Be(NavigationRayStatus.Blocked);
            CompareMappingAttempt(warm, clearRay, Scene.Budget).Should().Be(NavigationRayStatus.Success);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappingReuse_ShouldReevaluateProfilePolicyAndMedium(bool sparse)
    {
        using var scene = new Scene(sparse, wallKind: 2, endZ: 5);
        var warm = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        NavigationRayRequest request = scene.Request();
        CompareMappingAttempt(warm, request, Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
        var jumping = new NavigationAgentProfile(Scene.Profile.Shape, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.Jump);
        NavigationRayRequest allowed = WithAdmission(request, jumping, Scene.Policy);
        CompareMappingAttempt(warm, allowed, Scene.Budget).Should().Be(NavigationRayStatus.Success);

        var denied = new NavigationAreaPolicy(new NavigationAreaPolicyKey("deny", 1),
            new[] { new NavigationAreaRule(false, Fixed64.Zero) });
        CompareMappingAttempt(warm, WithAdmission(request, jumping, denied), Scene.Budget)
            .Should().NotBe(NavigationRayStatus.Success);
        var tall = new NavigationAgentProfile(new KinematicBodyShape(Fixed64.Zero, (Fixed64)2, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.Jump);
        CompareMappingAttempt(warm, WithAdmission(request, tall, Scene.Policy), Scene.Budget)
            .Should().NotBe(NavigationRayStatus.Success);
        CompareMappingAttempt(warm, Copy(allowed, medium: TraversalMedium.Gas), Scene.Budget)
            .Should().Be(NavigationRayStatus.Blocked);
        CompareMappingAttempt(warm, allowed, Scene.Budget).Should().Be(NavigationRayStatus.Success);
        CompareMappingAttempt(warm, request, Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
    }

    [Fact]
    public void MappingReuse_ShouldObserveChangedPublishedCellsAndClosedComponents()
    {
        using var scene = new Scene(endZ: 5);
        var warm = scene.Context.Pathing.ImmediateRayWorkspace;
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
        long previousVersion = scene.Graph.GraphVersion;
        scene.PublishWall();
        scene.Graph.GraphVersion.Should().BeGreaterThan(previousVersion);
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
        NavigationWorldGraph closed = scene.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty, closeAllStructuralComponents: true, scene.Graph.GraphVersion + 1);
        scene.Context.Pathing.NavigationGraphStore.TryPublish(closed).Should().Be(NavigationCandidatePublication.Published);
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Stale);
    }

    [Fact]
    public void MappingReuse_ShouldRecoverAfterContextResetAndRepublishing()
    {
        using var scene = new Scene(endZ: 5);
        NavigationMap clearMap = scene.Graph.GetInstance(0).Map;
        var warm = scene.Context.Pathing.ImmediateRayWorkspace;
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
        scene.PublishWall();
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
        long version = scene.Graph.GraphVersion;
        scene.Context.Reset();
        scene.Graph.MapCount.Should().Be(0);
        scene.Graph.GraphVersion.Should().BeGreaterThan(version);
        GuidedPathTestScene.PublishMapAndPolicy(scene.Context, clearMap, 3,
            OverlayReplacementPolicy.Clear, 1, Scene.Policy, 2);
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
    }

    [Fact]
    public void MappingReuse_ShouldNotConfuseEqualVersionsFromDifferentWorlds()
    {
        using var clear = new Scene(endZ: 5);
        using var blocked = new Scene(wallKind: 1, endZ: 5);
        // Equal numeric epochs must not reuse another store's published obstacle bytes.
        long sharedVersion = Math.Max(clear.Graph.GraphVersion, blocked.Graph.GraphVersion) + 1;
        clear.Context.Pathing.NavigationGraphStore.TryPublish(clear.Graph.WithGraphVersion(sharedVersion))
            .Should().Be(NavigationCandidatePublication.Published);
        blocked.Context.Pathing.NavigationGraphStore.TryPublish(blocked.Graph.WithGraphVersion(sharedVersion))
            .Should().Be(NavigationCandidatePublication.Published);
        clear.Graph.GraphVersion.Should().Be(blocked.Graph.GraphVersion);
        var warm = clear.Context.Pathing.ImmediateRayWorkspace;
        CompareMappingAttempt(warm, clear.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
        CompareMappingAttempt(warm, blocked.Request(), Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
        CompareMappingAttempt(warm, clear.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
    }

    [Fact]
    public void MappingReuse_ShouldRejectReplacedPhysicalGridBeforePublication()
    {
        using var scene = new Scene(endZ: 5);
        var warm = scene.Context.Pathing.ImmediateRayWorkspace;
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Success);
        GridConfiguration configuration = scene.Grid.Configuration;
        ushort index = scene.Grid.GridIndex;
        scene.Context.World.TryRemoveGrid(index).Should().BeTrue();
        scene.Context.World.TryAddGrid(configuration, out ushort replacement).Should().BeTrue();
        replacement.Should().Be(index);
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.Stale);
        scene.PublishWall();
        CompareMappingAttempt(warm, scene.Request(), Scene.Budget).Should().Be(NavigationRayStatus.CertifiedObstruction);
    }

    [Fact]
    public void MappingReuse_ShouldAllocateNothingAcrossWarmDirectAttempts()
    {
        using var scene = new Scene(endZ: 5);
        var warm = scene.Context.Pathing.ImmediateRayWorkspace;
        NavigationRayRequest request = scene.Request();
        for (int i = 0; i < 16; i++)
            RunMappingAttempt(warm, request, Scene.Budget);
        NavigationRayStatus status = default;
        long bytes = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < 16; i++)
                status = RunMappingAttempt(warm, request, Scene.Budget);
        });
        status.Should().Be(NavigationRayStatus.Success);
        bytes.Should().Be(0);
    }

    [Fact]
    public void MappingReuse_ShouldHaveExactBoundedConstructionStorage()
    {
        Unsafe.SizeOf<NavigationRayMappingSnapshot>().Should().Be(64);
        GC.KeepAlive(new NavigationRayMappingMemo());
        long bytes = AllocationTestUtility.MeasureAllocatedBytes(
            () => GC.KeepAlive(new NavigationRayMappingMemo()));
        bytes.Should().Be(40 + 24 + 64L * NavigationRayMappingMemo.Capacity,
            "one memo object and one fixed snapshot array are construction storage, not leased payload bytes");
    }

    private static NavigationRayRequest WithAdmission(NavigationRayRequest request,
        NavigationAgentProfile profile, NavigationAreaPolicy policy) => new(
        request.World, request.Store, request.ExpectedGraph, profile, policy, request.Medium,
        request.Start, request.End, request.EndpointAllowance, request.ChainConstraint);

    private static NavigationRayStatus RunMappingAttempt(NavigationImmediateRayWorkspace workspace,
        NavigationRayRequest request, NavigationWorkBudget budget)
    {
        workspace.WorkMeter.Reset(budget);
        workspace.RayWork.Begin(request);
        return workspace.RayWork.AdvanceDirect(workspace);
    }

    private static NavigationRayStatus CompareMappingAttempt(NavigationImmediateRayWorkspace warm,
        NavigationRayRequest request, NavigationWorkBudget budget)
    {
        NavigationRayWorkspace actual = Workspace(warm);
        var cold = new NavigationImmediateRayWorkspace(actual.MapCapacity, actual.Dependencies.Pages.Length,
            actual.Dependencies.Components.Length, actual.CoveredAddressCapacity, actual.TraceIntervalCapacity);
        NavigationRayStatus expected = RunMappingAttempt(cold, request, budget);
        RunMappingAttempt(warm, request, budget).Should().Be(expected);
        warm.RayWork.Result.Should().BeEquivalentTo(cold.RayWork.Result);
        warm.WorkMeter.Should().BeEquivalentTo(cold.WorkMeter, options => options.IncludingInternalProperties());
        NavigationRayWorkspace oracle = Workspace(cold);
        actual.TraceIntervals.AsReadOnlySpan().ToArray().Should().Equal(oracle.TraceIntervals.AsReadOnlySpan().ToArray());
        actual.Dependencies.Pages.AsSpan(0, actual.Dependencies.PageCount).ToArray().Should()
            .Equal(oracle.Dependencies.Pages.AsSpan(0, oracle.Dependencies.PageCount).ToArray());
        actual.Dependencies.Components.AsSpan(0, actual.Dependencies.ComponentCount).ToArray().Should()
            .Equal(oracle.Dependencies.Components.AsSpan(0, oracle.Dependencies.ComponentCount).ToArray());
        actual.Dependencies.HasTransitionDependency.Should().Be(oracle.Dependencies.HasTransitionDependency);
        warm.SourceOrdinals.Should().OnlyContain(value => value == 0);
        return expected;
    }
}
