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

}
