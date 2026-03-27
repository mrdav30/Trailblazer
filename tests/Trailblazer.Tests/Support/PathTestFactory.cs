using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Threading;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

public static class PathTestFactory
{
    private static int _generatedChartId;

    public static NavigationChart RegisterSingleWalkablePoint(string mapName, Vector3d pos)
    {
        Vector3d minBounds = pos - new Vector3d(1, 1, 1);
        bool[,,] data = new bool[3, 3, 3];
        data[1, 1, 1] = true;

        var map = NavigationChart.From3D(mapName, data, minBounds, Fixed64.One);
        PathManager.Register(map);
        return map;
    }

    public static NavigationChart RegisterFromData(string name, bool[,,] data, Vector3d minBounds)
    {
        var map = NavigationChart.From3D(name, data, minBounds, GlobalGridManager.VoxelSize);
        PathManager.Register(map);
        return map;
    }

    public static NavigationChart RegisterSingleTraversalPoint(
        string mapName,
        Vector3d pos,
        TraversalMedia traversalKinds)
    {
        Vector3d minBounds = pos - new Vector3d(1, 1, 1);
        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(traversalKinds);

        var map = NavigationChart.From3D(mapName, data, minBounds, Fixed64.One);
        PathManager.Register(map);
        return map;
    }

    public static NavigationChart RegisterGeneratedVolumePoint(
        Vector3d pos,
        TraversalMedium medium,
        string chartNamePrefix = "GeneratedVolume")
    {
        return RegisterSingleTraversalPoint(
            mapName: $"{chartNamePrefix}-{Interlocked.Increment(ref _generatedChartId)}",
            pos: pos,
            traversalKinds: ToTraversalKinds(medium));
    }

    public static NavigationChart BuildSinglePointMap(string name, Vector3d worldPos)
    {
        // Convert a single world point into an aligned map
        Vector3d minBounds = worldPos - new Vector3d(1, 1, 1);
        bool[,,] data = new bool[3, 3, 3];
        data[1, 1, 1] = true;

        return NavigationChart.From3D(name, data, minBounds, Fixed64.One);
    }

    private static TraversalMedia ToTraversalKinds(TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Gas => TraversalMedia.Gas,
            TraversalMedium.Liquid => TraversalMedia.Liquid,
            _ => throw new ArgumentOutOfRangeException(nameof(medium))
        };
    }
}
