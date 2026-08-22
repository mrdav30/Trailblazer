using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

public static class PathTestFactory
{
    public static NavigationAgentProfile DefaultNavigationProfile { get; } = new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Quarter),
        Fixed64.One,
        Fixed64.One,
        Fixed64.Half,
        TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
        TraversalCapability.Jump
            | TraversalCapability.Climb
            | TraversalCapability.Swim
            | TraversalCapability.Fly);

    public static TrailblazerWorldContext CreateContextWithGrid()
    {
        return CreateContextWithGrid(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
    }

    public static TrailblazerWorldContext CreateContextWithGrid(Vector3d boundsMin, Vector3d boundsSize)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(
            new GridConfiguration(boundsMin, boundsSize),
            out _).Should().BeTrue();
        return context;
    }

    public static Voxel RequireVoxel(TrailblazerWorldContext context, Vector3d position)
    {
        context.World.TryGetVoxel(position, out Voxel? voxel).Should().BeTrue();
        return TestRequire.NotNull(voxel);
    }
}
