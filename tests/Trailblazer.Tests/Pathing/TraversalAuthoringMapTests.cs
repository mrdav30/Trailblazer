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
                { "L" },
                { "L!" },
                { "W!" }
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
        Assert.True(plainChartCell.HasSurface);
        Assert.False(plainChartCell.HasVolume);
        Assert.Equal(NavigationChartCellFlags.None, plainChartCell.Flags);

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell markedChartCell));
        Assert.True(markedChartCell.HasTraversalData);
        Assert.True(markedChartCell.HasSurface);
        Assert.False(markedChartCell.HasVolume);
        Assert.Equal(
            NavigationChartCellFlags.TransitionSourceHint | NavigationChartCellFlags.TransitionDestinationHint,
            markedChartCell.Flags);

        Assert.True(result.Chart.TryGetCell(new Vector3d(2, 0, 0), out NavigationChartCell waterChartCell));
        Assert.True(waterChartCell.HasTraversalData);
        Assert.False(waterChartCell.HasSurface);
        Assert.True(waterChartCell.HasVolume);
        Assert.True(waterChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Water));
        Assert.False(waterChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Open));

        Assert.Equal(2, result.GeneratedTransitions.Length);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimEntry
            && t.Source.Space == TraversalTransitionAnchorSpace.Chart
            && t.Destination.Space == TraversalTransitionAnchorSpace.WaterVolume);
        Assert.Contains(result.GeneratedTransitions, t =>
            t.Type == TraversalTransitionType.SwimExit
            && t.Source.Space == TraversalTransitionAnchorSpace.WaterVolume
            && t.Destination.Space == TraversalTransitionAnchorSpace.Chart);
    }

    [Fact]
    public void Build_ShouldOnlyGenerateTransitionsForPerpendicularNeighbors()
    {
        string[,,] map =
        {
            {
                { "L!", "." },
                { ".", "W!" }
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
                { "O" },
                { "W" }
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
        Assert.False(openChartCell.HasSurface);
        Assert.True(openChartCell.HasVolume);
        Assert.True(openChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Open));
        Assert.False(openChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Water));

        Assert.True(result.Chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell waterChartCell));
        Assert.True(waterChartCell.HasTraversalData);
        Assert.False(waterChartCell.HasSurface);
        Assert.True(waterChartCell.HasVolume);
        Assert.True(waterChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Water));
        Assert.False(waterChartCell.SupportsVolumeTraversal(VolumeTraversalMode.Open));

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
