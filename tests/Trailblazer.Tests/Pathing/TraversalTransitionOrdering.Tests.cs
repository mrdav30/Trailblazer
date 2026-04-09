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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
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
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel voxel).Should().BeTrue();

        var solid = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.GlobalIndex),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var gas = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Gas(voxel.GlobalIndex),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var overrideAnchor = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.GlobalIndex, new Vector3d(0.25, 0, 0)),
            TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));
        var bidirectional = new TraversalTransition(
            "same",
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(voxel.GlobalIndex),
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
