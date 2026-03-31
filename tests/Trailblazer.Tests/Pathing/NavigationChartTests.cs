using FixedMathSharp;
using System.Linq;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public class NavigationChartTests
{
    [Fact]
    public void Enumerations_ShouldTrackSparseAuthoredAndGeneratedCellsAcrossLiveUpdates()
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 4, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;
        data[0, 2, 0] = new NavigationChartCell(
            TraversalMedia.Liquid,
            generatedTransitionMedia: TraversalMedia.Liquid);

        NavigationChart chart = NavigationChart.From3D("IndexedChart", data, Vector3d.Zero, Fixed64.One);

        Assert.Equal(
            new[] { Vector3d.Zero, new Vector3d(2, 0, 0) },
            chart.GetAuthoredCells().Select(entry => entry.Position).ToArray());
        Assert.Equal(
            new[] { Vector3d.Zero },
            chart.GetSurfaceCells().Select(entry => entry.Position).ToArray());
        Assert.Equal(new[] { 2 }, chart.GetGeneratedTransitionIndices());

        Assert.True(chart.TrySetCell(1, 0, 0, NavigationChartCell.Gas, out _));
        Assert.True(chart.TrySetCell(2, 0, 0, NavigationChartCell.Empty, out _));
        Assert.True(chart.TrySetCell(
            3,
            0,
            0,
            new NavigationChartCell(
                TraversalMedia.Solid,
                generatedTransitionMedia: TraversalMedia.Solid),
            out _));

        Assert.Equal(
            new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(3, 0, 0) },
            chart.GetAuthoredCells().Select(entry => entry.Position).ToArray());
        Assert.Equal(
            new[] { Vector3d.Zero, new Vector3d(3, 0, 0) },
            chart.GetSurfaceCells().Select(entry => entry.Position).ToArray());
        Assert.Equal(new[] { 3 }, chart.GetGeneratedTransitionIndices());
    }
}
