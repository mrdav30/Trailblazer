using System;
using System.Linq;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationPlanarMapTests
{
    private static readonly GridConfiguration Grid = new(
        new Vector3d(-1, 0, -1),
        new Vector3d(5, 1, 2));

    private static readonly NavigationCell Open = new(
        TraversalMedia.Solid, TraversalCapability.None, default,
        Fixed64.Zero, Fixed64.One, Fixed64.Two);

    [Fact]
    public void Build_TopDownObstacleAndPortalUseOneExplicitLayer()
    {
        NavigationMapBuilder obstacle = TopDown("obstacle", Fixed64.One);
        NavigationMapBuilder portal = TopDown("portal", Fixed64.One);
        for (int x = 5; x >= 1; x--)
        {
            var detour = new VoxelIndex(x, 1, 2);
            obstacle.AddCell(detour, Open);
            portal.AddCell(detour, Open);
            if (x != 3)
                obstacle.AddCell(new VoxelIndex(x, 1, 1), Open);
        }

        NavigationMap detourMap = obstacle.Build();
        NavigationMap portalMap = portal.Build();
        NavigationPlanarBinding binding = detourMap.PlanarBinding!.Value;
        binding.Mode.Should().Be(NavigationMode2D.TopDown);
        binding.LayerIndex.Should().Be(1);
        binding.EmbeddingY.Should().Be(Fixed64.One);
        detourMap.Cells.Select(cell => cell.Index).Should().BeInAscendingOrder();
        detourMap.Cells.Should().HaveCount(9);
        detourMap.Cells.Should().NotContain(cell => cell.Index.Equals(new VoxelIndex(3, 1, 1)));
        portalMap.Cells.Should().HaveCount(5);
        portalMap.Cells.Should().OnlyContain(cell => cell.Index.y == 1 && cell.Index.z == 2);
    }

    [Fact]
    public void Build_PlanarBindingIsImmutableBakeIdentity()
    {
        NavigationMapBuilder builder = TopDown("map", Fixed64.One);
        NavigationMap original = builder.Build();
        NavigationMap same = TopDown("map", Fixed64.One).Build();
        NavigationMap moved = builder.SetPlanarLayer(
            NavigationMode2D.TopDown, 1, Fixed64.One + Fixed64.Quarter).Build();
        NavigationMap spatial = new NavigationMapBuilder("map", Grid).Build();

        original.Should().Be(same);
        original.GetHashCode().Should().Be(same.GetHashCode());
        moved.Should().NotBe(original);
        spatial.PlanarBinding.Should().BeNull();
        spatial.Should().NotBe(original);
        original.PlanarBinding!.Value.EmbeddingY.Should().Be(Fixed64.One);
        ((object)original.PlanarBinding.Value).Equals(same.PlanarBinding!.Value).Should().BeTrue();
        original.PlanarBinding.Value.Equals(new object()).Should().BeFalse();
        original.PlanarBinding.Value.Equals(default(NavigationPlanarBinding)).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void SetPlanarLayer_RejectsInvalidLayerOrEmbedding(int layer, int y)
    {
        Action configure = () => new NavigationMapBuilder("map", Grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, layer, (Fixed64)y);
        configure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsExplicitCellOnAnotherLayer()
    {
        Action build = () => new NavigationMapBuilder("map", Grid)
            .AddCell(new VoxelIndex(1, 0, 1), Open)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            .Build();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_EmbeddingOnSharedBoundaryRetainsTheSelectedLayer()
    {
        NavigationMap upper = TopDown("map", Fixed64.Half).Build();
        NavigationMap lower = new NavigationMapBuilder("map", Grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Half)
            .Build();

        upper.PlanarBinding!.Value.LayerIndex.Should().Be(1);
        lower.PlanarBinding!.Value.LayerIndex.Should().Be(0);
        upper.Should().NotBe(lower);
    }

    [Theory]
    [InlineData(NavigationMode2D.SideView)]
    [InlineData((NavigationMode2D)0)]
    public void SetPlanarLayer_RejectsUnsupportedModes(NavigationMode2D mode)
    {
        Action configure = () => new NavigationMapBuilder("map", Grid)
            .SetPlanarLayer(mode, 1, Fixed64.One);
        configure.Should().Throw<ArgumentException>().WithParameterName("mode");
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("transition")]
    [InlineData("rule")]
    public void Build_RejectsSpatialEdgeAuthoringOnAPlanarMap(string kind)
    {
        var source = new VoxelIndex(1, 1, 1);
        var destination = new NavigationCellAddress("map", new VoxelIndex(2, 1, 1));
        NavigationMapBuilder builder = TopDown("map", Fixed64.One);
        switch (kind)
        {
            case "connection":
                builder.AddConnection(new NavigationConnection(
                    "step", source, destination, Vector3d.Zero, Vector3d.Right,
                    Fixed64.Zero, Fixed64.Half));
                break;
            case "transition":
                builder.AddTransition(new TraversalTransitionDefinition(
                    "jump", TraversalTransitionType.Jump, source, TraversalMedium.Solid,
                    destination, TraversalMedium.Solid, TraversalCapability.Jump));
                break;
            default:
                builder.AddTransitionRule(new TraversalTransitionRule(
                    "climb", TraversalTransitionType.Climb, TraversalMedium.Solid,
                    TraversalMedium.Solid, TraversalTransitionRuleScope.SameCell,
                    TraversalCapability.Climb, Fixed64.One, TraversalTransitionLocomotionHints.RequestClimb));
                break;
        }

        Action build = () => builder.Build();
        build.Should().Throw<ArgumentException>().WithParameterName(nameof(NavigationMap.PlanarBinding));
    }

    [Fact]
    public void Prepare_RejectsPlanarBakeBeforeSpatialPublication()
    {
        NavigationMap map = TopDown("map", Fixed64.One)
            .AddCell(new VoxelIndex(1, 1, 1), Open)
            .Build();
        Action prepare = () => new PreparedNavigationMap(map, bakeVersion: 1);
        prepare.Should().Throw<ArgumentException>();
    }

    private static NavigationMapBuilder TopDown(string mapId, Fixed64 embeddingY) =>
        new NavigationMapBuilder(mapId, Grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 1, embeddingY);
}
