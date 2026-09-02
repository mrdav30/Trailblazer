using System;
using System.Globalization;
using System.Linq;
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
        map.DefaultCell.Should().BeNull("token import emits explicit entries, not a coverage-history default");
        map.TransitionRuleSpan.Length.Should().Be(0);
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

    [Fact]
    public void ImportRectangular_ShouldRejectEveryTransposedDimensionWithoutPartialImport()
    {
        GridConfiguration single = CreateRectangularConfiguration(1, 1, 1);
        Action missingSource = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            single,
            null!);
        Action wrongX = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            single,
            new string[2, 1, 1]);
        Action wrongY = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            single,
            new string[1, 2, 1]);
        Action wrongZ = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            single,
            new string[1, 1, 2]);

        missingSource.Should().Throw<ArgumentNullException>();
        wrongX.Should().Throw<ArgumentException>();
        wrongY.Should().Throw<ArgumentException>();
        wrongZ.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportRectangular_ShouldUseCustomPrefixAndExactGasBoundarySemantics()
    {
        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "G!" } }, { { "S!" } } },
            transitionIdPrefix: "airlock");

        map.Transitions.Should().HaveCount(2);
        map.Transitions.Should().OnlyContain(transition =>
            transition.Id.StartsWith("airlock:", StringComparison.Ordinal)
            && transition.RequiredCapabilities == TraversalCapability.Fly);
        map.Transitions.Should().ContainSingle(transition =>
            transition.Type == TraversalTransitionType.Takeoff
            && transition.SourceIndex.x == 1
            && transition.Destination.Index.x == 0);
        map.Transitions.Should().ContainSingle(transition =>
            transition.Type == TraversalTransitionType.Landing
            && transition.SourceIndex.x == 0
            && transition.Destination.Index.x == 1);

        NavigationMap inertFirst = NavigationMapTokenImporter.ImportRectangular(
            "inert",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "." } }, { { "S!" } } });
        inertFirst.Cells.Should().ContainSingle();
        inertFirst.Transitions.Should().BeEmpty();
    }

    [Fact]
    public void TokenLegend_ShouldRejectReservedAndDuplicateNormalizedTokensWithoutReplacement()
    {
        var first = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var replacement = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.Fly,
            default,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        var legend = new NavigationTokenLegend();

        legend.Register(" TILE ", new NavigationTokenLegendEntry(first)).Should().BeTrue();
        legend.Register("TILE", new NavigationTokenLegendEntry(replacement)).Should().BeFalse();
        Action marker = () => legend.Register("BAD!", new NavigationTokenLegendEntry(first));
        Action cost = () => legend.Register("BAD_COST", new NavigationTokenLegendEntry(first));

        marker.Should().Throw<ArgumentException>();
        cost.Should().Throw<ArgumentException>();
        legend.TryGetEntry(" TILE ", out NavigationTokenLegendEntry retained).Should().BeTrue();
        retained.Cell.Should().Be(first);
    }

    [Fact]
    public void TokenLegend_ShouldRejectNullWithoutAliasingTheEmptyToken()
    {
        NavigationTokenLegend legend = NavigationTokenLegend.CreateBuiltIn();

        Action register = () => legend.Register(null!, NavigationTokenLegendEntry.SkipCell());
        Action lookup = () => legend.TryGetEntry(null!, out _);

        register.Should().Throw<ArgumentNullException>();
        lookup.Should().Throw<ArgumentNullException>();
        legend.TryGetEntry("   ", out NavigationTokenLegendEntry empty).Should().BeTrue();
        empty.EmitsCell.Should().BeFalse();
    }

    [Fact]
    public void TokenLegendEntry_ShouldRejectUnknownOrUnauthoredTransitionMedia()
    {
        var solid = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);

        Action unknown = () => _ = new NavigationTokenLegendEntry(
            solid,
            (TraversalMedia)(1 << 12));
        Action unauthored = () => _ = new NavigationTokenLegendEntry(
            solid,
            TraversalMedia.Gas);
        Action emptyCell = () => _ = new NavigationTokenLegendEntry(default);

        unknown.Should().Throw<ArgumentException>();
        unauthored.Should().Throw<ArgumentException>();
        emptyCell.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportRectangular_AuthorsExactClimbAndShoreHints()
    {
        string[,,] source = new string[4, 1, 1];
        source[0, 0, 0] = "S";
        source[1, 0, 0] = "SC!";
        source[2, 0, 0] = "SC!";
        source[3, 0, 0] = "S";

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "ladder",
            CreateRectangularConfiguration(4, 1, 1),
            source);

        TraversalTransitionDefinition enter = map.Transitions.Single(
            transition => transition.SourceIndex.x == 0
                && transition.Destination.Index.x == 1);
        TraversalTransitionDefinition stay = map.Transitions.Single(
            transition => transition.SourceIndex.x == 1
                && transition.Destination.Index.x == 2);
        TraversalTransitionDefinition exit = map.Transitions.Single(
            transition => transition.SourceIndex.x == 2
                && transition.Destination.Index.x == 3);

        enter.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb
            | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        stay.LocomotionHints.Should().Be(enter.LocomotionHints);
        exit.LocomotionHints.Should().Be(TraversalTransitionLocomotionHints.RequestClimb);

        NavigationMap climbShore = NavigationMapTokenImporter.ImportRectangular(
            "climb-shore",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "L!" } }, { { "LC!" } } });
        TraversalTransitionDefinition swimEntry = climbShore.Transitions.Single(
            transition => transition.Type == TraversalTransitionType.SwimEntry);
        TraversalTransitionDefinition climbSwimExit = climbShore.Transitions.Single(
            transition => transition.Type == TraversalTransitionType.SwimExit);
        swimEntry.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.None);
        climbSwimExit.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb
            | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);

        NavigationMap normalShore = NavigationMapTokenImporter.ImportRectangular(
            "normal-shore",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "L!" } }, { { "S!" } } });
        normalShore.Transitions.Single(
                transition => transition.Type == TraversalTransitionType.SwimExit)
            .LocomotionHints.Should().Be(
                TraversalTransitionLocomotionHints.None);

        NavigationMap mixedShore = NavigationMapTokenImporter.ImportRectangular(
            "mixed-shore",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "L!" } }, { { "SL!" } } });
        mixedShore.Transitions.Single(
                transition => transition.Type == TraversalTransitionType.SwimExit)
            .LocomotionHints.Should().Be(
                TraversalTransitionLocomotionHints.None);

        var solidOnlyClimbLegend = NavigationTokenLegend.CreateBuiltIn();
        solidOnlyClimbLegend.Register(
                "CUSTOMCLIMB",
                new NavigationTokenLegendEntry(
                    new NavigationCell(
                        TraversalMedia.Solid,
                        TraversalCapability.None,
                        default,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        NavigationCellFlags.ClimbSurfaceHint),
                    TraversalMedia.Solid))
            .Should().BeTrue();
        NavigationMap solidOnlyClimbShore =
            NavigationMapTokenImporter.ImportRectangular(
                "solid-only-climb-shore",
                CreateRectangularConfiguration(2, 1, 1),
                new string[,,] { { { "L!" } }, { { "CUSTOMCLIMB!" } } },
                solidOnlyClimbLegend);
        solidOnlyClimbShore.Transitions.Single(
                transition => transition.Type == TraversalTransitionType.SwimExit)
            .LocomotionHints.Should().Be(
                TraversalTransitionLocomotionHints.None);
    }

    [Theory]
    [InlineData("S_-1")]
    [InlineData("S_bad")]
    [InlineData("S!!")]
    [InlineData("!")]
    [InlineData("_1")]
    [InlineData(".!")]
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
    public void ImportRectangular_ShouldTreatNullTokenAsAnEmptyCell()
    {
        var source = new string[1, 1, 1];

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(1, 1, 1),
            source);

        map.Cells.Should().BeEmpty();
        map.Transitions.Should().BeEmpty();
    }

    [Fact]
    public void ImportRectangular_ShouldNotGenerateClimbTransitionsWithoutAMarker()
    {
        var legend = NavigationTokenLegend.CreateBuiltIn();
        legend.Register(
                "CUSTOMCLIMB",
                new NavigationTokenLegendEntry(
                    new NavigationCell(
                        TraversalMedia.Solid,
                        TraversalCapability.None,
                        default,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        Fixed64.One,
                        NavigationCellFlags.ClimbSurfaceHint),
                    TraversalMedia.Solid))
            .Should().BeTrue();

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(3, 1, 1),
            new string[,,] { { { "CUSTOMCLIMB" } }, { { "S" } }, { { "CUSTOMCLIMB" } } },
            legend);

        map.Cells.Should().HaveCount(3);
        map.Transitions.Should().BeEmpty();

        NavigationMap climbThenGas = NavigationMapTokenImporter.ImportRectangular(
            "climb-then-gas",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "CUSTOMCLIMB!" } }, { { "G!" } } },
            legend);
        NavigationMap gasThenClimb = NavigationMapTokenImporter.ImportRectangular(
            "gas-then-climb",
            CreateRectangularConfiguration(2, 1, 1),
            new string[,,] { { { "G!" } }, { { "CUSTOMCLIMB!" } } },
            legend);

        climbThenGas.Transitions.Should().HaveCount(2);
        climbThenGas.Transitions.Should().NotContain(
            transition => transition.Type == TraversalTransitionType.Climb);
        gasThenClimb.Transitions.Should().HaveCount(2);
        gasThenClimb.Transitions.Should().NotContain(
            transition => transition.Type == TraversalTransitionType.Climb);
    }

    [Fact]
    public void ImportRectangular_ShouldRejectTransitionMarkerOnInertCustomCell()
    {
        var legend = new NavigationTokenLegend();
        legend.Register(
            "PLAIN",
            new NavigationTokenLegendEntry(new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One))).Should().BeTrue();

        Action import = () => NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(1, 1, 1),
            new string[,,] { { { "PLAIN!" } } },
            legend);

        import.Should().Throw<ArgumentException>()
            .WithMessage("*cannot generate transitions*");
    }

    [Fact]
    public void ImportRectangular_ShouldGenerateMarkedTransitionsAcrossVerticalAndDepthAxes()
    {
        var source = new string[1, 2, 2];
        source[0, 0, 0] = "S!";
        source[0, 1, 0] = "L!";
        source[0, 0, 1] = "L!";
        source[0, 1, 1] = ".";

        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "world",
            CreateRectangularConfiguration(1, 2, 2),
            source);

        map.Transitions.Should().HaveCount(4);
        map.Transitions.Should().Contain(transition =>
            transition.SourceIndex == default
            && transition.Destination.Index == new GridForge.Spatial.VoxelIndex(0, 1, 0));
        map.Transitions.Should().Contain(transition =>
            transition.SourceIndex == default
            && transition.Destination.Index == new GridForge.Spatial.VoxelIndex(0, 0, 1));
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
