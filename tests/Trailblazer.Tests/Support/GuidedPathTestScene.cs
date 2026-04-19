using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Tests;

internal static class GuidedPathTestScene
{
    public static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "GuidedPathTestWater");
    }

    public static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "GuidedPathTestOpen");
    }

    public static void AddObstacle(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        GridObstacleManager.TryAddObstacle(
            voxel.GlobalIndex,
            new BoundsKey(position, position)).Should().BeTrue();
    }

    public static void AddObstaclePlaneAtX(int x)
    {
        for (int y = -4; y <= 4; y++)
        {
            for (int z = -4; z <= 4; z++)
                AddObstacle(new Vector3d(x, y, z));
        }
    }

    public static void RegisterTransitionFallbackScene()
    {
        PathTestFactory.RegisterSingleWalkablePoint("GuidedPathTransitionStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("GuidedPathTransitionEnd", new Vector3d(4, 0, 0));

        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guided-path-transition-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guided-path-transition-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterTransitionFallbackClimbScene()
    {
        PathTestFactory.RegisterSingleWalkablePoint("GuidedPathClimbTransitionStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("GuidedPathClimbTransitionEnd", new Vector3d(4, 0, 0));

        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guided-path-climb-transition-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2,
            requestsClimbIntent: true)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guided-path-climb-transition-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterAerialLandingHandoffScene(string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(Vector3d.Zero);

        AddObstaclePlaneAtX(2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    public static void RegisterAerialClimbHandoffScene(string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(Vector3d.Zero);

        AddObstaclePlaneAtX(2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1,
            requestsClimbIntent: true,
            preserveClimbIntentOnFollowup: true)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    public static void RegisterAerialLandingChoiceScene(string sceneKey)
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Target",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterVolumeExitHandoffScene(string chartKey)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1]
        {
            {
                { NavigationChartCell.SolidLiquid },
                { NavigationChartCell.Solid },
                { NavigationChartCell.Solid }
            }
        };

        PathManager.Register(NavigationChart.From3D(chartKey, data, new Vector3d(2, 0, 0), Fixed64.One));

        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(1, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    public static void RegisterChartBackedSwimTargetScene(string chartKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{chartKey}-Target",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Liquid);

        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(1, 0, 0));
    }
}
