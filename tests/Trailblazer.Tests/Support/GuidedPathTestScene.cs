using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Grids;
using Trailblazer.Pathing;

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

    public static void RegisterTransitionFallbackScene(TrailblazerWorldContext context)
    {
        PathTestFactory.RegisterSingleWalkablePoint(context, "GuidedPathTransitionStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(context, "GuidedPathTransitionEnd", new Vector3d(4, 0, 0));

        AddWater(context, new Vector3d(1, 0, 0));
        AddWater(context, new Vector3d(2, 0, 0));
        AddWater(context, new Vector3d(3, 0, 0));

        context.Transitions.Register(new TraversalTransition(
            id: "guided-path-transition-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        context.Transitions.Register(new TraversalTransition(
            id: "guided-path-transition-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterTransitionFallbackClimbScene(TrailblazerWorldContext context)
    {
        PathTestFactory.RegisterSingleWalkablePoint(context, "GuidedPathClimbTransitionStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(context, "GuidedPathClimbTransitionEnd", new Vector3d(4, 0, 0));

        AddWater(context, new Vector3d(1, 0, 0));
        AddWater(context, new Vector3d(2, 0, 0));
        AddWater(context, new Vector3d(3, 0, 0));

        context.Transitions.Register(new TraversalTransition(
            id: "guided-path-climb-transition-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2,
            requestsClimbIntent: true)).Should().BeTrue();

        context.Transitions.Register(new TraversalTransition(
            id: "guided-path-climb-transition-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterLiquidClimbExitScene(TrailblazerWorldContext context, string chartKey)
    {
        string[,,] map = new string[1, 6, 1]
        {
            {
                { "S!" },
                { "L" },
                { "L" },
                { "L!" },
                { "LC!" },
                { "S" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartKey,
            map,
            Vector3d.Zero,
            Fixed64.One).Build();
        context.Pathing.Register(buildResult).Should().BeTrue();
    }

    public static void RegisterAerialLandingHandoffScene(TrailblazerWorldContext context, string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            context,
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint(context, $"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(context, Vector3d.Zero);

        AddObstaclePlaneAtX(context, 2);

        context.Transitions.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        context.Transitions.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    public static void RegisterAerialClimbHandoffScene(TrailblazerWorldContext context, string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            context,
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint(context, $"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(context, Vector3d.Zero);

        AddObstaclePlaneAtX(context, 2);

        context.Transitions.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1,
            requestsClimbIntent: true,
            preserveClimbIntentOnFollowup: true)).Should().BeTrue();

        context.Transitions.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    public static void RegisterAerialLandingChoiceScene(TrailblazerWorldContext context, string sceneKey)
    {
        AddOpen(context, Vector3d.Zero);
        AddOpen(context, new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleTraversalPoint(
            context,
            $"{sceneKey}-Target",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);

        context.Transitions.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterVolumeExitHandoffScene(TrailblazerWorldContext context, string chartKey)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1]
        {
            {
                { NavigationChartCell.SolidLiquid },
                { NavigationChartCell.Solid },
                { NavigationChartCell.Solid }
            }
        };

        context.Pathing.Register(NavigationChart.From3D(chartKey, data, new Vector3d(2, 0, 0), Fixed64.One)).Should().BeTrue();

        AddWater(context, Vector3d.Zero);
        AddWater(context, new Vector3d(1, 0, 0));

        context.Transitions.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterVolumeExitFollowupClimbScene(TrailblazerWorldContext context, string chartKey)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1]
        {
            {
                { NavigationChartCell.SolidLiquid },
                { default },
                { NavigationChartCell.Solid }
            }
        };

        context.Pathing.Register(NavigationChart.From3D(chartKey, data, new Vector3d(2, 0, 0), Fixed64.One)).Should().BeTrue();

        AddWater(context, Vector3d.Zero);
        AddWater(context, new Vector3d(1, 0, 0));

        context.Transitions.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        context.Transitions.Register(new TraversalTransition(
            id: $"{chartKey}-climb",
            type: TraversalTransitionType.Climb,
            source: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1,
            requestsClimbIntent: true)).Should().BeTrue();
    }

    public static void RegisterChartBackedSwimTargetScene(TrailblazerWorldContext context, string chartKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            context,
            $"{chartKey}-Target",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Liquid);

        AddWater(context, Vector3d.Zero);
        AddWater(context, new Vector3d(1, 0, 0));
    }
}
