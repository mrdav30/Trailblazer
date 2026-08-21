using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Grids;

namespace Trailblazer.Tests;

internal static class GuidedPathTestScene
{
    public static void AddWater(
        TrailblazerWorldContext context,
        Vector3d position,
        string chartNamePrefix = "GuidedPathTestWater")
    {
        PathTestFactory.RegisterGeneratedVolumePoint(context, position, TraversalMedium.Liquid, chartNamePrefix);
    }

    public static void AddOpen(
        TrailblazerWorldContext context,
        Vector3d position,
        string chartNamePrefix = "GuidedPathTestOpen")
    {
        PathTestFactory.RegisterGeneratedVolumePoint(context, position, TraversalMedium.Gas, chartNamePrefix);
    }

    public static void AddObstacle(TrailblazerWorldContext context, Vector3d position)
    {
        AddObstacle(context, position, context.World.AllocateObstacleToken());
    }

    private static void AddObstacle(
        TrailblazerWorldContext context,
        Vector3d position,
        ObstacleToken obstacleToken)
    {
        context.World.TryGetGridAndVoxel(position, out VoxelGrid? grid, out Voxel? voxel).Should().BeTrue();
        grid!.TryAddObstacle(voxel!, obstacleToken).Should().BeTrue();
    }

    public static void AddObstaclePlaneAtX(TrailblazerWorldContext context, int x)
    {
        ObstacleToken obstacleToken = context.World.AllocateObstacleToken();
        for (int y = -4; y <= 4; y++)
        {
            for (int z = -4; z <= 4; z++)
                AddObstacle(context, new Vector3d(x, y, z), obstacleToken);
        }
    }

}
