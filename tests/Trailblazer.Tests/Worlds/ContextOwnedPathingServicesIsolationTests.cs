using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class ContextOwnedPathingServicesIsolationTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ContextTransitions_ShouldAllowSameTransitionIdInSeparateWorlds()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        RegisterSolidLine(contextA, "SharedTransitionChart", Vector3d.Zero, 2);
        RegisterSolidLine(contextB, "SharedTransitionChart", Vector3d.Zero, 2);

        TraversalTransition transitionA = CreateJumpTransition(contextA, "shared-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        TraversalTransition transitionB = CreateJumpTransition(contextB, "shared-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        int contextBVersionBefore = contextB.Transitions.RegistryVersion;

        contextA.Transitions.Register(transitionA).Should().BeTrue();
        contextB.Transitions.RegistryVersion.Should().Be(contextBVersionBefore);
        contextB.Transitions.Register(transitionB).Should().BeTrue();

        contextA.Transitions.IsRegistered("shared-transition").Should().BeTrue();
        contextB.Transitions.IsRegistered("shared-transition").Should().BeTrue();

        contextA.Transitions.Unregister("shared-transition").Should().BeTrue();

        contextA.Transitions.IsRegistered("shared-transition").Should().BeFalse();
        contextB.Transitions.IsRegistered("shared-transition").Should().BeTrue();
        contextB.Transitions.IsActive("shared-transition").Should().BeTrue();
    }

    [Fact]
    public void ContextTransitionQueryCaches_ShouldStayWorldLocalForEquivalentGridIndices()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        RegisterSolidLine(contextA, "WorldATransitionQueryChart", Vector3d.Zero, 2);
        RegisterSolidLine(contextB, "WorldBTransitionQueryChart", Vector3d.Zero, 2);
        TraversalTransition transitionA = CreateJumpTransition(contextA, "world-a-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        Voxel contextAVoxel = RequireVoxel(contextA, Vector3d.Zero);
        Voxel contextBVoxel = RequireVoxel(contextB, Vector3d.Zero);
        contextAVoxel.GridIndex.Should().Be(contextBVoxel.GridIndex);

        contextA.Transitions.Register(transitionA).Should().BeTrue();

        contextA.Transitions.GetDirectedTransitionsFromSourceGrid(contextAVoxel.GridIndex)
            .Should()
            .ContainSingle(transition => transition.Id == "world-a-transition");
        contextB.Transitions.GetDirectedTransitionsFromSourceGrid(contextBVoxel.GridIndex)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ContextVolumeRules_ShouldInvalidateOnlyOwningVolumeGuideCache()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Guides.TrySeedVolumeCacheForBenchmark(1234, new[] { "SharedVolumeChart" }, checkout: false)
            .Should()
            .BeTrue();
        contextB.Guides.TrySeedVolumeCacheForBenchmark(1234, new[] { "SharedVolumeChart" }, checkout: false)
            .Should()
            .BeTrue();

        contextA.VolumeRules.SetGasVoxelRule(static _ => true);

        contextA.VolumeRules.HasGasVoxelRule.Should().BeTrue();
        contextB.VolumeRules.HasGasVoxelRule.Should().BeFalse();
        contextA.Guides.TotalVolumeGuideCount.Should().Be(0);
        contextB.Guides.TotalVolumeGuideCount.Should().Be(1);
    }

    [Fact]
    public void ContextGuides_ShouldInvalidateOnlyOwningGuideCaches()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Guides.TrySeedAStarCacheForBenchmark(2222, new[] { "SharedGuideChart" }, checkout: false)
            .Should()
            .BeTrue();
        contextB.Guides.TrySeedAStarCacheForBenchmark(2222, new[] { "SharedGuideChart" }, checkout: false)
            .Should()
            .BeTrue();

        contextA.Guides.InvalidateCacheFor("SharedGuideChart");

        contextA.Guides.TotalAStarGuideCount.Should().Be(0);
        contextB.Guides.TotalAStarGuideCount.Should().Be(1);
    }

    [Fact]
    public void ContextReachabilitySnapshots_ShouldBuildOnlyForOwningContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        RegisterSolidPoint(contextA, "WorldAReachabilityStart", Vector3d.Zero);
        RegisterSolidPoint(contextA, "WorldAReachabilityEnd", new Vector3d(3, 0, 0));
        RegisterSolidPoint(contextB, "WorldBReachabilityStart", Vector3d.Zero);
        RegisterSolidPoint(contextB, "WorldBReachabilityEnd", new Vector3d(3, 0, 0));
        AStarPathRequest requestA = CreateAStarRequest(contextA, Vector3d.Zero, new Vector3d(3, 0, 0));

        contextA.Guides.RequestGuide(requestA, out AStarGuide? guide).Should().BeFalse();
        guide.Should().BeNull();

        contextA.Guides.CaptureReachabilityStats().SnapshotBuildCount.Should().Be(1);
        contextB.Guides.CaptureReachabilityStats().SnapshotBuildCount.Should().Be(0);
    }

    private static TrailblazerWorldContext CreateContextWithGrid()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        return context;
    }

    private static void RegisterSolidLine(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        context.Pathing.Register(NavigationChart.From3D(chartName, data, minBounds, Fixed64.One))
            .Should()
            .BeTrue();
    }

    private static void RegisterSolidPoint(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d position)
    {
        var data = new bool[1, 1, 1]
        {
            {
                { true }
            }
        };

        context.Pathing.Register(NavigationChart.From3D(chartName, data, position, Fixed64.One))
            .Should()
            .BeTrue();
    }

    private static TraversalTransition CreateJumpTransition(
        TrailblazerWorldContext context,
        string id,
        Vector3d source,
        Vector3d destination)
    {
        return new TraversalTransition(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(RequireVoxel(context, source).WorldIndex),
            TraversalTransitionAnchor.Solid(RequireVoxel(context, destination).WorldIndex),
            pathCostModifier: 1);
    }

    private static AStarPathRequest CreateAStarRequest(
        TrailblazerWorldContext context,
        Vector3d source,
        Vector3d destination)
    {
        return TestRequire.NotNull(AStarPathRequest.Create(context, source, destination, Fixed64.One));
    }

    private static Voxel RequireVoxel(TrailblazerWorldContext context, Vector3d position)
    {
        context.World.TryGetVoxel(position, out Voxel? voxel).Should().BeTrue();
        return TestRequire.NotNull(voxel);
    }
}
