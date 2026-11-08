using FixedMathSharp;
using FluentAssertions;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasOffAxisCorridorScenarioFixture
    : GravitasNavigatorScenarioFixture
{
    internal GravitasOffAxisCorridorScenarioFixture(
        Fixed64 radius,
        Fixed64 arrivalRadius)
        : base(addWall: false, addNavigation: false)
    {
        ConfigureOffAxisCorridor(radius, arrivalRadius);
    }

    private void ConfigureOffAxisCorridor(
        Fixed64 radius,
        Fixed64 arrivalRadius)
    {
        const string mapId = "gravitas-off-axis";
        VoxelIndex source = default;
        var middle = new VoxelIndex(1, 0, 0);
        var destination = new VoxelIndex(2, 0, 0);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Right * (Fixed64)2,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();

        Vector3d sourceFoot = Anchor(binding, source);
        Vector3d destinationFoot = Anchor(binding, destination);
        Fixed64 offAxisDistance = Fixed64.FromFraction(1, 16);
        Vector3d entryOffset = new(
            Fixed64.Quarter,
            Fixed64.Zero,
            -offAxisDistance);
        Vector3d exitOffset = new(
            -Fixed64.Quarter,
            Fixed64.Zero,
            -offAxisDistance);
        var connection = new NavigationConnection(
            "off-axis",
            source,
            new NavigationCellAddress(mapId, destination),
            sourceFoot + entryOffset,
            destinationFoot + exitOffset,
            Fixed64.FromFraction(3, 8),
            Fixed64.One,
            new[] { new NavigationCellAddress(mapId, middle) });
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Half,
            Fixed64.One);
        var corridorWitness = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            (Fixed64)4,
            Fixed64.Half,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder(mapId, binding)
            .AddCell(source, cell)
            .AddCell(middle, corridorWitness)
            .AddCell(destination, cell)
            .AddConnection(connection)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            3,
            Trailblazer.FrameCount + 1);
        Trailblazer.Pathing.Admit(operation).Should().BeTrue();
        SimulateUntilApplied(new[] { operation });

        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(radius, Fixed64.One, Fixed64.Half),
            Fixed64.Zero,
            Fixed64.Zero,
            arrivalRadius,
            TraversalMedia.Solid,
            TraversalCapability.None);
        ConfigureScenario(
            profile,
            sourceFoot,
            destinationFoot,
            mapId,
            mapId,
            PhysicsLayerMask.FromLayer(1),
            PhysicsLayerMask.None);
        AddSupportPlatform(new Vector3d(
            Fixed64.One,
            sourceFoot.Y - Fixed64.Quarter,
            Fixed64.Zero));
    }
}
