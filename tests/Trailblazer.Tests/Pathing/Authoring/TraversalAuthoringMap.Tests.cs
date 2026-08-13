using System;
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class TraversalAuthoringMapTests : IDisposable
{
    public TraversalAuthoringMapTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Build_ShouldCreateChartAndSwimTransitionsFromBuiltInTokens()
    {
        string[,,] map =
        {
            {
                { "S" },
                { "S!" },
                { "L!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "BuiltInLegend",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.True(result.Chart.TryGetCell(new Vector3d(0, 0, 0), out NavigationChartCell plainChartCell));
        Assert.True(plainChartCell.HasTraversalData);
        Assert.True(plainChartCell.HasSolid);
        Assert.False(plainChartCell.HasVolume);
        Assert.Equal(NavigationChartCellFlags.None, plainChartCell.Flags);

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell markedChartCell));
        Assert.True(markedChartCell.HasTraversalData);
        Assert.True(markedChartCell.HasSolid);
        Assert.False(markedChartCell.HasVolume);
        Assert.Equal(
            NavigationChartCellFlags.TransitionSourceHint | NavigationChartCellFlags.TransitionDestinationHint,
            markedChartCell.Flags);
        Assert.Equal(TraversalMedia.Solid, markedChartCell.GeneratedTransitionMedia);

        Assert.True(result.Chart.TryGetCell(new Vector3d(2, 0, 0), out NavigationChartCell waterChartCell));
        Assert.True(waterChartCell.HasTraversalData);
        Assert.False(waterChartCell.HasSolid);
        Assert.True(waterChartCell.HasVolume);
        Assert.True(waterChartCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(waterChartCell.SupportsMedium(TraversalMedium.Gas));
        Assert.Equal(TraversalMedia.Liquid, waterChartCell.GeneratedTransitionMedia);

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimEntry
            && t.Source.Medium == TraversalMedium.Solid
            && t.Destination.Medium == TraversalMedium.Liquid);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimExit
            && t.Source.Medium == TraversalMedium.Liquid
            && t.Destination.Medium == TraversalMedium.Solid);
    }

    [Fact]
    public void Build_ShouldOnlyGenerateTransitionsForPerpendicularNeighbors()
    {
        string[,,] map =
        {
            {
                { "S!", "." },
                { ".", "L!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "PerpendicularOnly",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.Empty(result.GeneratedTransitions);
    }

    [Fact]
    public void Build_ShouldTreatBareVolumeTokensAsAuthoredVolumeCells()
    {
        string[,,] map =
        {
            {
                { "G" },
                { "L" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "BareVolumeTokens",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell openChartCell));
        Assert.True(openChartCell.HasTraversalData);
        Assert.False(openChartCell.HasSolid);
        Assert.True(openChartCell.HasVolume);
        Assert.True(openChartCell.SupportsMedium(TraversalMedium.Gas));
        Assert.False(openChartCell.SupportsMedium(TraversalMedium.Liquid));

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell waterChartCell));
        Assert.True(waterChartCell.HasTraversalData);
        Assert.False(waterChartCell.HasSolid);
        Assert.True(waterChartCell.HasVolume);
        Assert.True(waterChartCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(waterChartCell.SupportsMedium(TraversalMedium.Gas));

        Assert.Empty(result.GeneratedTransitions);
    }

    [Fact]
    public void Build_ShouldTreatBuiltInMixedTokensAsExplicitMixedMediaCells()
    {
        string[,,] map =
        {
            {
                { "SG" },
                { "SL" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "MixedTokens",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell gasBoundaryCell));
        Assert.True(gasBoundaryCell.HasSolid);
        Assert.True(gasBoundaryCell.HasVolume);
        Assert.True(gasBoundaryCell.SupportsMedium(TraversalMedium.Gas));
        Assert.False(gasBoundaryCell.SupportsMedium(TraversalMedium.Liquid));

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell liquidBoundaryCell));
        Assert.True(liquidBoundaryCell.HasSolid);
        Assert.True(liquidBoundaryCell.HasVolume);
        Assert.True(liquidBoundaryCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(liquidBoundaryCell.SupportsMedium(TraversalMedium.Gas));

        Assert.Empty(result.GeneratedTransitions);
    }

    [Fact]
    public void Build_ShouldGenerateSwimTransitionsFromMarkedMixedBoundaryTokens()
    {
        string[,,] map =
        {
            {
                { "S!" },
                { "SL!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "MixedSwimBoundary",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell markedMixedCell));
        Assert.True(markedMixedCell.HasSolid);
        Assert.True(markedMixedCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.Equal(
            NavigationChartCellFlags.TransitionSourceHint | NavigationChartCellFlags.TransitionDestinationHint,
            markedMixedCell.Flags);
        Assert.Equal(TraversalMedia.Solid | TraversalMedia.Liquid, markedMixedCell.GeneratedTransitionMedia);

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimEntry
            && t.Source.Position == Vector3d.Zero
            && t.Source.Medium == TraversalMedium.Solid
            && t.Destination.Position == new Vector3d(1, 0, 0)
            && t.Destination.Medium == TraversalMedium.Liquid);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimExit
            && t.Source.Position == new Vector3d(1, 0, 0)
            && t.Source.Medium == TraversalMedium.Liquid
            && t.Destination.Position == Vector3d.Zero
            && t.Destination.Medium == TraversalMedium.Solid);
    }

    [Fact]
    public void Build_ShouldGenerateAerialTransitionsFromMarkedMixedBoundaryTokens()
    {
        string[,,] map =
        {
            {
                { "SG!" },
                { "G!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "MixedGasBoundary",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.Takeoff
            && t.Source.Position == Vector3d.Zero
            && t.Source.Medium == TraversalMedium.Solid
            && t.Destination.Position == new Vector3d(1, 0, 0)
            && t.Destination.Medium == TraversalMedium.Gas);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.Landing
            && t.Source.Position == new Vector3d(1, 0, 0)
            && t.Source.Medium == TraversalMedium.Gas
            && t.Destination.Position == Vector3d.Zero
            && t.Destination.Medium == TraversalMedium.Solid);
    }

    [Fact]
    public void Build_ShouldNotGenerateTransitionsForAmbiguousMixedToMixedBoundaries()
    {
        string[,,] map =
        {
            {
                { "SG!" },
                { "SL!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "AmbiguousMixedBoundary",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.Empty(result.GeneratedTransitions);
    }

    [Fact]
    public void Build_ShouldMarkClimbSurfaceTokensAndGenerateClimbSeamTransitions()
    {
        string[,,] map =
        {
            {
                { "S" },
                { "SC!" }
            }
        };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "ClimbSeamBoundary",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell climbCell));
        Assert.True(climbCell.HasSolid);
        Assert.Equal(
            NavigationChartCellFlags.ClimbSurfaceHint | NavigationChartCellFlags.ClimbTransitionHint,
            climbCell.Flags);

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.Climb
            && t.Source.Position == Vector3d.Zero
            && t.Destination.Position == new Vector3d(1, 0, 0)
            && t.RequestsClimbIntent);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.Climb
            && t.Source.Position == new Vector3d(1, 0, 0)
            && t.Destination.Position == Vector3d.Zero
            && !t.RequestsClimbIntent);
    }

    [Fact]
    public void Build_ShouldGenerateBidirectionalClimbTransitionsBetweenAdjacentClimbSurfaces()
    {
        string[,,] map =
        {
            {
                { "SC" },
                { "SC" }
            }
        };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "ClimbSurfacePair",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.All(result.GeneratedTransitions, transition =>
        {
            Assert.Equal(TraversalTransitionType.Climb, transition.Type);
            Assert.True(transition.RequestsClimbIntent);
            Assert.Equal(TraversalMedium.Solid, transition.Source.Medium);
            Assert.Equal(TraversalMedium.Solid, transition.Destination.Medium);
        });
    }

    [Fact]
    public void Build_ShouldGenerateLiquidExitThatRequestsClimb_WhenShorelineUsesMarkedLcToken()
    {
        string[,,] map =
        {
            {
                { "L!" },
                { "LC!" }
            }
        };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "LiquidClimbBoundary",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell shorelineCell));
        Assert.True(shorelineCell.HasSolid);
        Assert.True(shorelineCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.Equal(
            NavigationChartCellFlags.ClimbSurfaceHint
                | NavigationChartCellFlags.ClimbTransitionHint
                | NavigationChartCellFlags.TransitionSourceHint
                | NavigationChartCellFlags.TransitionDestinationHint,
            shorelineCell.Flags);

        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimExit
            && t.Source.Medium == TraversalMedium.Liquid
            && t.Destination.Medium == TraversalMedium.Solid
            && t.RequestsClimbIntent
            && t.PreserveClimbIntentOnFollowup);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimEntry
            && t.Source.Medium == TraversalMedium.Solid
            && t.Destination.Medium == TraversalMedium.Liquid
            && !t.RequestsClimbIntent
            && !t.PreserveClimbIntentOnFollowup);
    }

    [Fact]
    public void Build_ShouldRejectInvalidMarkerUsage()
    {
        string[,,] map =
        {
            {
                { "X!" }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "InvalidMarker",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        Assert.Throws<ArgumentException>(() => authoringMap.Build());
    }

    [Fact]
    public void Build_ShouldTreatNullTokensAsSkippedCells()
    {
        string[,,] map =
        {
            {
                { null! }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "NullToken",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        TraversalBuildResult result = authoringMap.Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.False(cell.HasTraversalData);
        Assert.Empty(result.GeneratedTransitions);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("S!!")]
    [InlineData("S!G")]
    public void Build_ShouldRejectMalformedTransitionMarkers(string token)
    {
        string[,,] map =
        {
            {
                { token }
            }
        };

        var authoringMap = new TraversalAuthoringMap(
            chartName: "MalformedMarker",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One);

        Assert.Throws<ArgumentException>(() => authoringMap.Build());
    }

    [Fact]
    public void LegendEntry_ShouldRejectTransitionMediaOutsideAuthoredTraversalKinds()
    {
        Assert.Throws<ArgumentException>(() =>
            new TraversalLegendEntry(NavigationChartCell.Empty, TraversalMedia.Solid));
    }

    [Theory]
    [InlineData("S_60", 60)]
    [InlineData("S_1", 1)]
    [InlineData("S_0", 0)]
    [InlineData("S", 0)]
    public void Build_ShouldApplyInlineCostModifierToSolidCell(string token, int expectedCost)
    {
        string[,,] map = { { { token } } };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "CostModSolid",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.True(cell.HasSolid);
        Assert.Equal(expectedCost, cell.PathCostModifier);
    }

    [Theory]
    [InlineData("SL_45", 45)]
    [InlineData("SG_10", 10)]
    [InlineData("L_99", 99)]
    [InlineData("G_5", 5)]
    public void Build_ShouldApplyInlineCostModifierToVolumeCells(string token, int expectedCost)
    {
        string[,,] map = { { { token } } };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "CostModVolume",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.True(cell.HasTraversalData);
        Assert.Equal(expectedCost, cell.PathCostModifier);
    }

    [Fact]
    public void Build_ShouldApplyCostModifierWhenTransitionMarkerAlsoPresentSuffix()
    {
        // "S_60!" is the supported form: base token, then optional cost, then marker.
        string[,,] map =
        {
            {
                { "S_60!" },
                { "L!" }
            }
        };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "CostAndMarker",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell solidCell));
        Assert.True(solidCell.HasSolid);
        Assert.Equal(60, solidCell.PathCostModifier);
        Assert.Equal(
            NavigationChartCellFlags.TransitionSourceHint | NavigationChartCellFlags.TransitionDestinationHint,
            solidCell.Flags);
    }

    [Theory]
    [InlineData("X_10")]
    [InlineData("._20")]
    public void Build_ShouldIgnoreCostModifierOnSkipCells(string token)
    {
        string[,,] map = { { { token } } };

        TraversalBuildResult result = new TraversalAuthoringMap(
            chartName: "SkipCellCost",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(result.Chart.TryGetCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.False(cell.HasTraversalData);
        Assert.Equal(0, cell.PathCostModifier);
    }
}
