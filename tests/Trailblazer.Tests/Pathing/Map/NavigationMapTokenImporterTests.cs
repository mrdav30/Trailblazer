using System;
using System.Globalization;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationMapTokenImporterTests
{
    [Fact]
    public void ImportRectangular_EmitsBuiltInCellsInlineCostAndAddressedTransitions()
    {
        string[,,] source = new string[3, 1, 1];
        source[0, 0, 0] = "S_2.5!";
        source[1, 0, 0] = "L!";
        source[2, 0, 0] = ".";

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(3, 1, 1),
            source);

        map.Cells.Should().HaveCount(2);
        map.Cells[0].Cell.Media.Should().Be(TraversalMedia.Solid);
        map.Cells[0].Cell.EnterCost.Should().Be(Fixed64.Parse("2.5"));
        map.Cells[0].Cell.Flags.Should().HaveFlag(NavigationCellFlags.TransitionSourceHint);
        map.Cells[1].Cell.Media.Should().Be(TraversalMedia.Liquid);
        map.Transitions.Should().HaveCount(2);
        map.Transitions.Should().Contain(transition =>
            transition.Type == TraversalTransitionType.SwimEntry
            && transition.SourceIndex.x == 0
            && transition.Destination.MapId == "world"
            && transition.Destination.Index.x == 1
            && transition.RequiredCapabilities == TraversalCapability.Swim);
        map.Transitions.Should().Contain(transition =>
            transition.Type == TraversalTransitionType.SwimExit
            && transition.SourceIndex.x == 1
            && transition.Destination.Index.x == 0);
    }

    [Fact]
    public void ImportRectangular_UsesCompleteCustomLegendPayload()
    {
        var expected = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.Fly,
            default,
            Fixed64.Parse("3.25"),
            Fixed64.Parse("0.25"),
            Fixed64.One,
            NavigationCellFlags.TransitionDestinationHint);
        var legend = new NavigationTokenLegend();
        legend.Register("AIR", new NavigationTokenLegendEntry(expected, TraversalMedia.Gas))
            .Should().BeTrue();

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(1, 1, 1),
            new string[,,] { { { " AIR " } } },
            legend);

        map.Cells.Should().ContainSingle();
        map.Cells[0].Cell.Should().Be(expected);
    }

    [Theory]
    [InlineData("S_-1")]
    [InlineData("S_bad")]
    [InlineData("S!!")]
    [InlineData("!")]
    [InlineData("UNKNOWN")]
    public void ImportRectangular_RejectsMalformedTokens(string token)
    {
        Action import = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(1, 1, 1),
            new string[,,] { { { token } } });

        import.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportRectangular_ParsesInlineCostUsingInvariantCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
                "world",
                CreateRectangularConfiguration(1, 1, 1),
                new string[,,] { { { "S_1.5" } } });

            map.Cells[0].Cell.EnterCost.Should().Be(Fixed64.Parse("1.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ImportRectangular_ProducesCanonicalOutputAcrossLegendRegistrationOrder()
    {
        NavigationCell solid = NavigationTokenLegend.CreateBuiltIn()
            .TryGetEntry("S", out NavigationTokenLegendEntry builtIn)
            ? builtIn.Cell
            : default;
        var forwardLegend = new NavigationTokenLegend();
        forwardLegend.Register("A", new NavigationTokenLegendEntry(solid));
        forwardLegend.Register("B", new NavigationTokenLegendEntry(solid));
        var reverseLegend = new NavigationTokenLegend();
        reverseLegend.Register("B", new NavigationTokenLegendEntry(solid));
        reverseLegend.Register("A", new NavigationTokenLegendEntry(solid));
        string[,,] source = new string[2, 1, 1];
        source[0, 0, 0] = "B";
        source[1, 0, 0] = "A";

        NavigationMap forward = NavigationMapTokenImporter.ImportRectangular(
            "world", CreateRectangularConfiguration(2, 1, 1), source, forwardLegend);
        NavigationMap reverse = NavigationMapTokenImporter.ImportRectangular(
            "world", CreateRectangularConfiguration(2, 1, 1), source, reverseLegend);

        reverse.Should().Be(forward);
        reverse.GetHashCode().Should().Be(forward.GetHashCode());
    }

    private static GridConfiguration CreateRectangularConfiguration(
        int width,
        int height,
        int length) => new(
            Vector3d.Zero,
            new Vector3d(width - 1, height - 1, length - 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
}
