using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
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

    internal static NavigationMapBuilder BuildSideViewValley(GridConfiguration grid, bool crest = false)
    {
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var left = new VoxelIndex(0, 1, 1);
        var right = new VoxelIndex(1, 1, 1);
        var height = crest ? Fixed64.Quarter : -Fixed64.Quarter;
        return new NavigationMapBuilder("side", grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(solid)
            .AddPlanarSupport(left, new(-Fixed64.Half, height), new(Fixed64.Half, height))
            .AddCell(right, new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
                Fixed64.Half, Fixed64.One, Fixed64.Two))
            .AddPlanarSupport(right, new(Fixed64.Half, height),
                new(Fixed64.One + Fixed64.Half, crest ? -Fixed64.Half : Fixed64.Half));
    }
}
