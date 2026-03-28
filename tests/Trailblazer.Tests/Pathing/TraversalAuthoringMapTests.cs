using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class TraversalAuthoringMapTests : IDisposable
{
    public TraversalAuthoringMapTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
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

        Assert.True(result.Chart.TryGetCell(new Vector3d(2, 0, 0), out NavigationChartCell waterChartCell));
        Assert.True(waterChartCell.HasTraversalData);
        Assert.False(waterChartCell.HasSolid);
        Assert.True(waterChartCell.HasVolume);
        Assert.True(waterChartCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(waterChartCell.SupportsMedium(TraversalMedium.Gas));

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
}
