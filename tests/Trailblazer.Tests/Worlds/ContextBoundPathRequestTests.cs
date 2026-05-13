using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class ContextBoundPathRequestTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AStarCreate_WithContext_ShouldResolveAgainstExplicitContext()
    {
        using TrailblazerWorldContext requestContext = CreateContextWithGrid();
        RegisterSolidLine(requestContext, "ExplicitAStarContextChart", Vector3d.Zero, 3);

        AStarPathRequest request = TestRequire.NotNull(
            AStarPathRequest.Create(requestContext, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        request.Context.Should().BeSameAs(requestContext);
        request.StartNode.Should().BeSameAs(RequireVoxel(requestContext, Vector3d.Zero));
        request.EndNode.Should().BeSameAs(RequireVoxel(requestContext, new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void FlowFieldCreate_WithContext_ShouldResolveAgainstExplicitContext()
    {
        using TrailblazerWorldContext requestContext = CreateContextWithGrid();
        RegisterSolidLine(requestContext, "ExplicitFlowContextChart", Vector3d.Zero, 3);

        FlowFieldPathRequest request = TestRequire.NotNull(
            FlowFieldPathRequest.Create(requestContext, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        request.Context.Should().BeSameAs(requestContext);
        request.StartNode.Should().BeSameAs(RequireVoxel(requestContext, Vector3d.Zero));
        request.EndNode.Should().BeSameAs(RequireVoxel(requestContext, new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void VolumeCreate_WithContext_ShouldResolveAgainstExplicitContext()
    {
        using TrailblazerWorldContext requestContext = CreateContextWithGrid();
        RegisterVolumeLine(requestContext, "ExplicitVolumeContextChart", Vector3d.Zero, 3, TraversalMedia.Gas);

        VolumePathRequest request = TestRequire.NotNull(
            VolumePathRequest.Create(
                requestContext,
                Vector3d.Zero,
                new Vector3d(2, 0, 0),
                Fixed64.One,
                medium: TraversalMedium.Gas));

        request.Context.Should().BeSameAs(requestContext);
        request.StartNode.Should().BeSameAs(RequireVoxel(requestContext, Vector3d.Zero));
        request.EndNode.Should().BeSameAs(RequireVoxel(requestContext, new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void UpdateRequest_ShouldKeepResolvingAgainstOriginalContext()
    {
        using TrailblazerWorldContext requestContext = CreateContextWithGrid();
        RegisterSolidLine(requestContext, "UpdateOriginalContextChart", Vector3d.Zero, 4);

        AStarPathRequest request = TestRequire.NotNull(
            AStarPathRequest.Create(requestContext, Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One));

        request.TrySetDestination(new Vector3d(3, 0, 0), resetSearchRange: true).Should().BeTrue();

        request.Context.Should().BeSameAs(requestContext);
        request.EndNode.Should().BeSameAs(RequireVoxel(requestContext, new Vector3d(3, 0, 0)));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GuideService_ShouldRejectRequestBoundToDifferentContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        RegisterSolidLine(contextA, "WorldARequestChart", Vector3d.Zero, 3);
        RegisterSolidLine(contextB, "WorldBRequestChart", Vector3d.Zero, 3);
        AStarPathRequest requestB = TestRequire.NotNull(
            AStarPathRequest.Create(contextB, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        contextA.Guides.RequestGuide(requestB, out IGuide? guide).Should().BeFalse();

        guide.Should().BeNull();
        contextA.Guides.TotalAStarGuideCount.Should().Be(0);
    }

    [Fact]
    public void RequestCacheKeys_ShouldNotAllocateSteadyState_WhenRequestsCarryContext()
    {
        using TrailblazerWorldContext context = CreateContextWithGrid();
        RegisterSolidLine(context, "ContextKeySolidChart", Vector3d.Zero, 4);
        RegisterVolumeLine(context, "ContextKeyVolumeChart", new Vector3d(0, 0, 4), 4, TraversalMedia.Gas);

        AStarPathRequest aStarRequest = TestRequire.NotNull(
            AStarPathRequest.Create(context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(
            FlowFieldPathRequest.Create(context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        VolumePathRequest volumeRequest = TestRequire.NotNull(
            VolumePathRequest.Create(
                context,
                new Vector3d(0, 0, 4),
                new Vector3d(3, 0, 4),
                Fixed64.One,
                medium: TraversalMedium.Gas));

        const int iterations = 1_024;
        long aggregate = 0;
        long allocated = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                aggregate += aStarRequest.RequestCacheKey;
                aggregate += flowFieldRequest.RequestCacheKey;
                aggregate += volumeRequest.RequestCacheKey;
            }
        });

        aggregate.Should().NotBe(0);
        allocated.Should().BeLessThan(128);
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

    private static void RegisterVolumeLine(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length,
        TraversalMedia media)
    {
        var data = new NavigationChartCell[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = new NavigationChartCell(media);

        context.Pathing.Register(NavigationChart.From3D(chartName, data, minBounds, Fixed64.One))
            .Should()
            .BeTrue();
    }

    private static Voxel RequireVoxel(TrailblazerWorldContext context, Vector3d position)
    {
        context.World.TryGetVoxel(position, out Voxel? voxel).Should().BeTrue();
        return TestRequire.NotNull(voxel);
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
