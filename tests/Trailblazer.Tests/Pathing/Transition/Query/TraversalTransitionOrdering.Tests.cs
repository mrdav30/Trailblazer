using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class TraversalTransitionOrderingTests : IDisposable
{
    public TraversalTransitionOrderingTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldSortDeterministically()
    {
        TraversalTransition[] transitions =
        {
            CreateTransition("b", Vector3d.Zero, new Vector3d(2, 0, 0), pathCostModifier: 3, isBidirectional: true),
            CreateTransition("a", new Vector3d(1, 0, 0), new Vector3d(2, 0, 0), pathCostModifier: 2),
            CreateTransition("a", Vector3d.Zero, new Vector3d(2, 0, 0), pathCostModifier: 1)
        };

        TraversalTransitionOrdering.Sort(transitions);

        transitions[0].Id.Should().Be("a");
        transitions[0].PathCostModifier.Should().Be(1);
        transitions[1].Id.Should().Be("a");
        transitions[1].PathCostModifier.Should().Be(2);
        transitions[2].Id.Should().Be("b");
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompareAnchorDetails_WhenIdsMatch()
    {
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        var solid = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.WorldIndex),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var gas = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Gas(voxel.WorldIndex),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var overrideAnchor = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.WorldIndex, new Vector3d(0.25, 0, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var bidirectional = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.WorldIndex),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            isBidirectional: true);

        TraversalTransitionOrdering.Compare(solid, gas).Should().BeLessThan(0);
        TraversalTransitionOrdering.Compare(solid, overrideAnchor).Should().BeLessThan(0);
        TraversalTransitionOrdering.Compare(solid, bidirectional).Should().BeLessThan(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldHandleShortArrays_AndCompareDestinationDetails()
    {
        TraversalTransitionOrdering.Sort(null!);

        TraversalTransition[] single =
        {
            CreateTransition("solo", Vector3d.Zero, new Vector3d(1, 0, 0))
        };

        TraversalTransitionOrdering.Sort(single);
        single.Should().HaveCount(1);

        var left = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1);
        var right = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0), new Vector3d(2.25, 0, 0)),
            pathCostModifier: 2);

        TraversalTransitionOrdering.Compare(left, right).Should().BeLessThan(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompare_ByDestinationVoxelYAndZ()
    {
        // Two transitions with same id, type, source, and dest X — differ on dest Y.
        var leftDestY = new TraversalTransition(
            "cmp",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var rightDestY = new TraversalTransition(
            "cmp",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 1, 0)));

        TraversalTransitionOrdering.Compare(leftDestY, rightDestY).Should().NotBe(0);

        // Differ on dest Z only (same X and Y).
        var leftDestZ = new TraversalTransition(
            "cmp",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var rightDestZ = new TraversalTransition(
            "cmp",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 1)));

        TraversalTransitionOrdering.Compare(leftDestZ, rightDestZ).Should().NotBe(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompare_BySourceVoxelYAndZ()
    {
        // Two transitions with same id and type — differ on source Y.
        var leftSrcY = new TraversalTransition(
            "src",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(new Vector3d(0, 0, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)));
        var rightSrcY = new TraversalTransition(
            "src",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(new Vector3d(0, 1, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)));

        TraversalTransitionOrdering.Compare(leftSrcY, rightSrcY).Should().NotBe(0);

        // Differ on source Z only.
        var leftSrcZ = new TraversalTransition(
            "src",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(new Vector3d(0, 0, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)));
        var rightSrcZ = new TraversalTransition(
            "src",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(new Vector3d(0, 0, 1)),
            TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)));

        TraversalTransitionOrdering.Compare(leftSrcZ, rightSrcZ).Should().NotBe(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldReturnZero_WhenTransitionsAreEqual()
    {
        var a = CreateTransition("eq", Vector3d.Zero, new Vector3d(1, 0, 0), pathCostModifier: 5, isBidirectional: false);
        var b = CreateTransition("eq", Vector3d.Zero, new Vector3d(1, 0, 0), pathCostModifier: 5, isBidirectional: false);

        TraversalTransitionOrdering.Compare(a, b).Should().Be(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompare_ByBidirectionalFlag()
    {
        var nonBidi = CreateTransition("bi", Vector3d.Zero, new Vector3d(1, 0, 0), pathCostModifier: 1, isBidirectional: false);
        var bidi = CreateTransition("bi", Vector3d.Zero, new Vector3d(1, 0, 0), pathCostModifier: 1, isBidirectional: true);

        TraversalTransitionOrdering.Compare(nonBidi, bidi).Should().NotBe(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompare_ByPointOverridePosition()
    {
        Voxel destVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        // Same dest voxel, but one has a point override with different Y.
        var left = new TraversalTransition(
            "po",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destVoxel.WorldIndex, new Vector3d(1.1, 0, 0)));
        var right = new TraversalTransition(
            "po",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destVoxel.WorldIndex, new Vector3d(1.1, 0.1, 0)));

        TraversalTransitionOrdering.Compare(left, right).Should().NotBe(0);

        // Same dest voxel, point override same X and Y but different Z.
        var leftZ = new TraversalTransition(
            "po",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destVoxel.WorldIndex, new Vector3d(1.1, 0, 0)));
        var rightZ = new TraversalTransition(
            "po",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destVoxel.WorldIndex, new Vector3d(1.1, 0, 0.1)));

        TraversalTransitionOrdering.Compare(leftZ, rightZ).Should().NotBe(0);
    }

    [Fact]
    public void TraversalTransitionOrdering_ShouldCompare_ByTypeCostGridAndPointOverrideX()
    {
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(16, -4, -4), new Vector3d(24, 8, 8)),
            out _).Should().BeTrue();

        var jump = new TraversalTransition(
            "cmp-type",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var swim = new TraversalTransition(
            "cmp-type",
            TraversalTransitionType.SwimEntry,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        TraversalTransitionOrdering.Compare(jump, swim).Should().BeLessThan(0);

        var lowCost = new TraversalTransition(
            "cmp-cost",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1);
        var highCost = new TraversalTransition(
            "cmp-cost",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2);

        TraversalTransitionOrdering.Compare(lowCost, highCost).Should().BeLessThan(0);

        var firstGrid = new TraversalTransition(
            "cmp-grid",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var secondGrid = new TraversalTransition(
            "cmp-grid",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(new Vector3d(16, 0, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(17, 0, 0)));

        TraversalTransitionOrdering.Compare(firstGrid, secondGrid).Should().BeLessThan(0);

        Voxel destinationVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        var lowerOverrideX = new TraversalTransition(
            "cmp-point",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destinationVoxel.WorldIndex, new Vector3d(1.1, 0, 0)));
        var higherOverrideX = new TraversalTransition(
            "cmp-point",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(Vector3d.Zero),
            TraversalTransitionAnchor.Solid(destinationVoxel.WorldIndex, new Vector3d(1.2, 0, 0)));

        TraversalTransitionOrdering.Compare(lowerOverrideX, higherOverrideX).Should().BeLessThan(0);
    }

    private static TraversalTransition CreateTransition(
        string id,
        Vector3d source,
        Vector3d destination,
        int pathCostModifier = 0,
        bool isBidirectional = false)
    {
        return new TraversalTransition(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(source),
            TraversalTransitionAnchor.Solid(destination),
            pathCostModifier,
            isBidirectional);
    }
}
