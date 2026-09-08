using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasStackedFloorScenarioFixture
    : GravitasNavigatorScenarioFixture
{
    internal GravitasStackedFloorScenarioFixture()
        : base(addWall: false, addNavigation: false)
    {
        ConfigureStackedFloors();
    }

    private void ConfigureStackedFloors()
    {
        const string lowerMapId = "gravitas-lower";
        const string middleMapId = "gravitas-middle";
        const string upperMapId = "gravitas-upper";
        var start = new VoxelIndex(0, 0, 0);
        var middleIndex = new VoxelIndex(1, 0, 0);
        var end = new VoxelIndex(2, 0, 0);
        NormalizedGridConfiguration lower = AddGrid((Fixed64)(-3));
        NormalizedGridConfiguration middle = AddGrid(Fixed64.Zero);
        NormalizedGridConfiguration upper = AddGrid((Fixed64)3);
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Half,
            Fixed64.One);
        NavigationMap lowerMap = BuildMap(lowerMapId, lower, start, middleIndex, end, cell);
        NavigationMap middleMap = BuildMap(middleMapId, middle, start, middleIndex, end, cell);
        NavigationMap upperMap = BuildMap(upperMapId, upper, start, middleIndex, end, cell);
        NavigationMapCommitOperation[] operations =
        {
            new(new PreparedNavigationMap(lowerMap, 1), OverlayReplacementPolicy.Clear, 3, Trailblazer.FrameCount + 1),
            new(new PreparedNavigationMap(middleMap, 1), OverlayReplacementPolicy.Clear, 4, Trailblazer.FrameCount + 1),
            new(new PreparedNavigationMap(upperMap, 1), OverlayReplacementPolicy.Clear, 5, Trailblazer.FrameCount + 1)
        };
        for (int i = 0; i < operations.Length; i++)
            Trailblazer.Pathing.Admit(operations[i]).Should().BeTrue();
        SimulateUntilApplied(operations);

        Vector3d startFoot = Anchor(upper, start);
        Vector3d endFoot = Anchor(upper, end);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Half),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.FromFraction(1, 16),
            TraversalMedia.Solid,
            TraversalCapability.None);
        ConfigureScenario(
            profile,
            startFoot,
            endFoot,
            upperMapId,
            upperMapId,
            PhysicsLayerMask.FromLayer(1),
            PhysicsLayerMask.FromLayer(0));
        AddSupportPlatform(new Vector3d(
            Fixed64.One,
            (Fixed64)2 + Fixed64.Quarter,
            Fixed64.Zero));
    }

    private NormalizedGridConfiguration AddGrid(Fixed64 centerY)
    {
        var configuration = new GridConfiguration(
            new Vector3d(Fixed64.Zero, centerY, Fixed64.Zero),
            new Vector3d((Fixed64)2, centerY, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        return binding;
    }

    private static NavigationMap BuildMap(
        string mapId,
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        VoxelIndex middle,
        VoxelIndex end,
        NavigationCell cell) => new NavigationMapBuilder(mapId, binding)
        .AddCell(start, cell)
        .AddCell(middle, cell)
        .AddCell(end, cell)
        .Build();
}
