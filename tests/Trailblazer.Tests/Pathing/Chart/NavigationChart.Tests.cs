using FixedMathSharp;
using FluentAssertions;
using System;
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

    [Fact]
    public void TrySetCell_ShouldReturnFalse_WhenIndexIsOutOfBounds()
    {
        NavigationChart chart = NavigationChart.From3D(
            "OOBChart",
            new bool[1, 2, 2],
            Vector3d.Zero,
            Fixed64.One);

        // Negative index — out of bounds.
        chart.TrySetCell(-1, 0, 0, NavigationChartCell.Solid, out _).Should().BeFalse();
        // Index equal to size — out of bounds.
        chart.TrySetCell(2, 0, 0, NavigationChartCell.Solid, out _).Should().BeFalse();
        chart.TrySetCell(0, 1, 0, NavigationChartCell.Solid, out _).Should().BeFalse();
    }

    [Fact]
    public void TrySetCell_ShouldReturnFalse_WhenNewCellEqualsCurrentCell()
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 2, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        NavigationChart chart = NavigationChart.From3D("SameCellChart", data, Vector3d.Zero, Fixed64.One);

        // Setting the same cell value again should report no change.
        chart.TrySetCell(0, 0, 0, NavigationChartCell.Solid, out NavigationChartCell previous).Should().BeFalse();
        previous.Should().Be(NavigationChartCell.Solid);
    }

    [Fact]
    public void IsWalkable_ShouldReturnFalse_WhenPositionIsOutsideChartBounds()
    {
        NavigationChart chart = NavigationChart.From3D(
            "BoundsCheck",
            new bool[1, 1, 1] { { { true } } },
            Vector3d.Zero,
            Fixed64.One);

        chart.IsWalkable(new Vector3d(10, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void IsWalkable_ShouldReturnFalse_WhenCellExistsButHasNoSolid()
    {
        // Gas cell exists at position but HasSolid = false.
        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Gas;

        NavigationChart chart = NavigationChart.From3D("GasCellIsWalkable", data, Vector3d.Zero, Fixed64.One);

        chart.TryGetCell(Vector3d.Zero, out NavigationChartCell cell).Should().BeTrue();
        cell.HasSolid.Should().BeFalse();

        chart.IsWalkable(Vector3d.Zero).Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCellArrayLengthMismatchesDimensions()
    {
        // Providing fewer cells than sizeX * sizeY * sizeZ should throw ArgumentException.
        Action act = () => new NavigationChart(
            "Mismatch",
            cells: new NavigationChartCell[1], // too small for 2x2x2
            sizeX: 2,
            sizeY: 2,
            sizeZ: 2,
            minBounds: Vector3d.Zero,
            maxBounds: new Vector3d(2, 2, 2),
            interval: Fixed64.One);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From3D_Bool_ShouldThrow_WhenUnsupportedMediumIsUsed()
    {
        var map = new bool[1, 1, 1] { { { true } } };
        const TraversalMedium unsupported = (TraversalMedium)(-1);

        Action act = () => NavigationChart.From3D("BadMedium", map, Vector3d.Zero, Fixed64.One, unsupported);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryWorldToIndex_ShouldReturnFalse_AndNegativeIndices_WhenOutOfBounds()
    {
        NavigationChart chart = NavigationChart.From3D(
            "WorldIndexOOB",
            new bool[1, 2, 2],
            Vector3d.Zero,
            Fixed64.One);

        bool valid = chart.TryWorldToIndex(new Vector3d(-5, 0, 0), out int x, out int y, out int z);

        valid.Should().BeFalse();
        x.Should().Be(-1);
        y.Should().Be(-1);
        z.Should().Be(-1);
    }
}
