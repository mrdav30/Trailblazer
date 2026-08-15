using System;
using FixedMathSharp;
using FluentAssertions;
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
    public void FlowFieldCreate_WithContext_ShouldResolveAgainstExplicitContext()
    {
        using TrailblazerWorldContext requestContext = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(requestContext, "ExplicitFlowContextChart", Vector3d.Zero, 3);

        FlowFieldPathRequest request = TestRequire.NotNull(
            FlowFieldPathRequest.Create(requestContext, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        request.Context.Should().BeSameAs(requestContext);
        request.StartNode.Should().BeSameAs(PathTestFactory.RequireVoxel(requestContext, Vector3d.Zero));
        request.EndNode.Should().BeSameAs(PathTestFactory.RequireVoxel(requestContext, new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void VolumeCreate_WithContext_ShouldResolveAgainstExplicitContext()
    {
        using TrailblazerWorldContext requestContext = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterTraversalLine(requestContext, "ExplicitVolumeContextChart", Vector3d.Zero, 3, TraversalMedia.Gas);

        VolumePathRequest request = TestRequire.NotNull(
            VolumePathRequest.Create(
                requestContext,
                Vector3d.Zero,
                new Vector3d(2, 0, 0),
                Fixed64.One,
                medium: TraversalMedium.Gas));

        request.Context.Should().BeSameAs(requestContext);
        request.StartNode.Should().BeSameAs(PathTestFactory.RequireVoxel(requestContext, Vector3d.Zero));
        request.EndNode.Should().BeSameAs(PathTestFactory.RequireVoxel(requestContext, new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void UpdateRequest_ShouldKeepResolvingAgainstOriginalContext()
    {
        using TrailblazerWorldContext requestContext = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(requestContext, "UpdateOriginalContextChart", Vector3d.Zero, 4);

        FlowFieldPathRequest request = TestRequire.NotNull(
            FlowFieldPathRequest.Create(requestContext, Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One));

        request.TrySetDestination(new Vector3d(3, 0, 0), resetSearchRange: true).Should().BeTrue();

        request.Context.Should().BeSameAs(requestContext);
        request.EndNode.Should().BeSameAs(PathTestFactory.RequireVoxel(requestContext, new Vector3d(3, 0, 0)));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GuideService_ShouldRejectRequestBoundToDifferentContext()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "WorldARequestChart", Vector3d.Zero, 3);
        PathTestFactory.RegisterSolidLine(contextB, "WorldBRequestChart", Vector3d.Zero, 3);
        FlowFieldPathRequest requestB = TestRequire.NotNull(
            FlowFieldPathRequest.Create(contextB, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        contextA.Guides.RequestGuide(requestB, out IGuide? guide).Should().BeFalse();

        guide.Should().BeNull();
        contextA.Guides.TotalFlowGuideCount.Should().Be(0);
    }

    [Fact]
    public void GuideService_ShouldRejectGuideReturnedThroughDifferentContext()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "WorldAGuideReturnChart", Vector3d.Zero, 3);
        FlowFieldPathRequest requestA = TestRequire.NotNull(
            FlowFieldPathRequest.Create(contextA, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        contextA.Guides.RequestGuide(requestA, out FlowFieldGuide? guide).Should().BeTrue();
        guide.Should().NotBeNull();
        contextA.Guides.InUseFlowGuideCount.Should().Be(1);

        Exception? exception = Record.Exception(() => contextB.Guides.ReturnGuide(guide));
        if (exception is InvalidOperationException)
            contextA.Guides.ReturnGuide(guide);
        else
            contextA.Guides.FlushCache(force: true);

        exception.Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("owning TrailblazerWorldContext");
        contextA.Guides.InUseFlowGuideCount.Should().Be(0);
        contextB.Guides.InUseFlowGuideCount.Should().Be(0);
    }

    [Fact]
    public void RequestCacheKeys_ShouldNotAllocateSteadyState_WhenRequestsCarryContext()
    {
        using TrailblazerWorldContext context = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(context, "ContextKeySolidChart", Vector3d.Zero, 4);
        PathTestFactory.RegisterTraversalLine(context, "ContextKeyVolumeChart", new Vector3d(0, 0, 4), 4, TraversalMedia.Gas);

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
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                aggregate += flowFieldRequest.RequestCacheKey.GetHashCode();
                aggregate += volumeRequest.RequestCacheKey.GetHashCode();
            }
        });

        aggregate.Should().NotBe(0);
        allocated.Should().BeLessThan(128);
    }

}
