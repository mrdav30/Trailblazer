using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Threading;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

public static class PathTestFactory
{
    private static int _generatedChartId;

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

    public static NavigationChart RegisterSingleWalkablePoint(string mapName, Vector3d pos)
    {
        Vector3d minBounds = pos - new Vector3d(1, 1, 1);
        bool[,,] data = new bool[3, 3, 3];
        data[1, 1, 1] = true;

        var map = NavigationChart.From3D(mapName, data, minBounds, Fixed64.One);
        TestWorld.Context.Pathing.Register(map);
        return map;
    }

    public static NavigationChart RegisterSolidPoint(
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

        var chart = NavigationChart.From3D(chartName, data, position, Fixed64.One);
        context.Pathing.Register(chart).Should().BeTrue();
        return chart;
    }

    public static NavigationChart RegisterSolidPoint(string chartName, Vector3d position)
    {
        var data = new bool[1, 1, 1]
        {
            {
                { true }
            }
        };

        var chart = NavigationChart.From3D(chartName, data, position, Fixed64.One);
        TestWorld.Context.Pathing.Register(chart).Should().BeTrue();
        return chart;
    }

    public static NavigationChart RegisterFromData(string name, bool[,,] data, Vector3d minBounds)
    {
        var map = NavigationChart.From3D(name, data, minBounds, TestWorld.Context.VoxelSize);
        TestWorld.Context.Pathing.Register(map);
        return map;
    }

    public static NavigationChart RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        var data = BuildSolidLineData(length);
        return RegisterFromData(chartName, data, minBounds);
    }

    public static NavigationChart RegisterSolidLine(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length)
    {
        var data = BuildSolidLineData(length);
        var chart = NavigationChart.From3D(chartName, data, minBounds, Fixed64.One);
        context.Pathing.Register(chart).Should().BeTrue();
        return chart;
    }

    public static NavigationChart RegisterSolidLine(string chartName, Vector3d minBounds, int length)
    {
        var data = BuildSolidLineData(length);
        return RegisterFromData(chartName, data, minBounds);
    }

    public static void RegisterVolumeLine(
        Vector3d start,
        TraversalMedium medium,
        int length,
        string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            RegisterGeneratedVolumePoint(
                new Vector3d(start.x + i, start.y, start.z),
                medium,
            chartNamePrefix);
        }
    }

    public static TraversalBuildResult RegisterAuthoredClimbRoute(string chartName)
    {
        string[,,] map = new string[2, 4, 2];
        map[0, 0, 0] = "S";
        map[0, 1, 0] = "SC!";
        map[1, 1, 0] = "SC";
        map[1, 1, 1] = "SC";
        map[1, 2, 1] = "SC!";
        map[0, 2, 1] = "S";
        map[0, 3, 1] = "S";

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName,
            map,
            Vector3d.Zero,
            Fixed64.One).Build();

        buildResult.GeneratedTransitions.Should().NotBeEmpty();
        PathManager.Register(buildResult).Should().BeTrue();
        return buildResult;
    }

    public static NavigationChart RegisterTraversalLine(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length,
        TraversalMedia media)
    {
        var data = new NavigationChartCell[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = new NavigationChartCell(media);

        var chart = NavigationChart.From3D(chartName, data, minBounds, Fixed64.One);
        context.Pathing.Register(chart).Should().BeTrue();
        return chart;
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
        TestWorld.Context.Pathing.Register(map);
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

    public static TraversalTransition CreateJumpTransition(
        TrailblazerWorldContext context,
        string id,
        Vector3d source,
        Vector3d destination,
        int pathCostModifier = 1)
    {
        return new TraversalTransition(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(RequireVoxel(context, source).WorldIndex),
            TraversalTransitionAnchor.Solid(RequireVoxel(context, destination).WorldIndex),
            pathCostModifier);
    }

    public static AStarPathRequest CreateAStarRequest(
        TrailblazerWorldContext context,
        Vector3d source,
        Vector3d destination)
    {
        return TestRequire.NotNull(AStarPathRequest.Create(context, source, destination, Fixed64.One));
    }

    public static Voxel RequireVoxel(TrailblazerWorldContext context, Vector3d position)
    {
        context.World.TryGetVoxel(position, out Voxel? voxel).Should().BeTrue();
        return TestRequire.NotNull(voxel);
    }

    private static bool[,,] BuildSolidLineData(int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        return data;
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
