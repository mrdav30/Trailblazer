using System;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

public static class PathTestFactory
{
    private static int _generatedChartId;

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

    public static NavigationChart RegisterSingleWalkablePoint(
        TrailblazerWorldContext context,
        string mapName,
        Vector3d pos)
    {
        Vector3d minBounds = pos - new Vector3d(1, 1, 1);
        bool[,,] data = new bool[3, 3, 3];
        data[1, 1, 1] = true;

        var map = NavigationChart.From3D(mapName, data, minBounds, Fixed64.One);
        context.Pathing.Register(map).Should().BeTrue();
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


    public static NavigationChart RegisterFromData(
        TrailblazerWorldContext context,
        string name,
        bool[,,] data,
        Vector3d minBounds)
    {
        var map = NavigationChart.From3D(name, data, minBounds, context.VoxelSize);
        context.Pathing.Register(map).Should().BeTrue();
        return map;
    }

    public static bool[,,] BuildSingleVoxelChoke()
    {
        bool[,,] data = new bool[1, 7, 5];
        for (int x = 0; x < 7; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                bool isChokeColumn = x == 3;
                bool isCenterRow = z == 2;
                data[0, x, z] = !isChokeColumn || isCenterRow;
            }
        }

        return data;
    }

    public static NavigationChart RegisterLineChart(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length)
    {
        var data = BuildSolidLineData(length);
        return RegisterFromData(context, chartName, data, minBounds);
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


    public static void RegisterVolumeLine(
        TrailblazerWorldContext context,
        Vector3d start,
        TraversalMedium medium,
        int length,
        string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            RegisterGeneratedVolumePoint(
                context,
                new Vector3d(start.X + i, start.Y, start.Z),
                medium,
                chartNamePrefix);
        }
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
        TrailblazerWorldContext context,
        string mapName,
        Vector3d pos,
        TraversalMedia traversalKinds)
    {
        Vector3d minBounds = pos - new Vector3d(1, 1, 1);
        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(traversalKinds);

        var map = NavigationChart.From3D(mapName, data, minBounds, Fixed64.One);
        context.Pathing.Register(map).Should().BeTrue();
        return map;
    }

    public static NavigationChart RegisterGeneratedVolumePoint(
        TrailblazerWorldContext context,
        Vector3d pos,
        TraversalMedium medium,
        string chartNamePrefix = "GeneratedVolume")
    {
        return RegisterSingleTraversalPoint(
            context,
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
