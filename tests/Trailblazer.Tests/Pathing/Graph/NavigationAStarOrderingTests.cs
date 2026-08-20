//=======================================================================
// NavigationAStarOrderingTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Collections.Generic;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationAStarOrderingTests
{
    private static readonly NavigationCell WeightedCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        enterCost: (Fixed64)100,
        radiusClearance: (Fixed64)4,
        heightClearance: (Fixed64)4);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualCostDiamond_ShouldPreferLowerEstimatedTotalBeforeAddress(
        bool reverseDefinitionOrder)
    {
        VoxelIndex start = default;
        var preferred = new VoxelIndex(2, 0, 0);
        var addressFirst = new VoxelIndex(0, 0, 2);
        var end = new VoxelIndex(2, 0, 2);
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges =
        {
            Edge("start-preferred", start, preferred, 2),
            Edge("start-address", start, addressFirst, 4),
            Edge("preferred-end", preferred, end, 4),
            Edge("address-end", addressFirst, end, 2)
        };

        AssertCanonicalDiamond(
            start,
            preferred,
            end,
            (Fixed64)206,
            reverseDefinitionOrder ? Reverse(edges) : edges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualEstimatedTotalDiamond_ShouldPreferLowerHeuristicBeforeAddress(
        bool reverseDefinitionOrder)
    {
        VoxelIndex start = default;
        var addressFirst = new VoxelIndex(2, 0, 0);
        var preferred = new VoxelIndex(4, 0, 0);
        var end = new VoxelIndex(6, 0, 0);
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges =
        {
            Edge("start-address", start, addressFirst, 2),
            Edge("start-preferred", start, preferred, 4),
            Edge("address-end", addressFirst, end, 4),
            Edge("preferred-end", preferred, end, 2)
        };

        AssertCanonicalDiamond(
            start,
            preferred,
            end,
            (Fixed64)206,
            reverseDefinitionOrder ? Reverse(edges) : edges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualEstimatedTotalAndHeuristicDiamond_ShouldPreferCanonicalAddress(
        bool reverseDefinitionOrder)
    {
        VoxelIndex start = default;
        var canonical = new VoxelIndex(0, 0, 2);
        var other = new VoxelIndex(2, 0, 0);
        var end = new VoxelIndex(2, 0, 2);
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges =
        {
            Edge("start-other", start, other, 2),
            Edge("start-canonical", start, canonical, 2),
            Edge("other-end", other, end, 2),
            Edge("canonical-end", canonical, end, 2)
        };

        AssertCanonicalDiamond(
            start,
            canonical,
            end,
            (Fixed64)204,
            reverseDefinitionOrder ? Reverse(edges) : edges);
    }

    [Fact]
    public void EstimatedTotalOverflow_ShouldFailInsteadOfWrappingHeapOrder()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var middle = new VoxelIndex(2, 0, 0);
        var end = new VoxelIndex(4, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                new[]
                {
                    start,
                    new VoxelIndex(1, 0, 0),
                    middle,
                    new VoxelIndex(3, 0, 0),
                    end
                },
                "overflow",
                new[]
                {
                    Edge(
                        "near-maximum",
                        start,
                        middle,
                        Fixed64.MaxValue - Fixed64.One),
                    Edge("finish", middle, end, 2)
                },
                WeightedCell);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                fixture.CreateQuery(start, end, fixture.DefaultProfile));

        result.Status.Should().Be(NavigationSurfaceAStarStatus.CostOverflow);
        result.Nodes.Should().BeEmpty();
    }

    private static void AssertCanonicalDiamond(
        VoxelIndex start,
        VoxelIndex expectedMiddle,
        VoxelIndex end,
        Fixed64 expectedCost,
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges)
    {
        using var world = new GridWorld();
        var cells = new List<VoxelIndex>();
        for (int i = 0; i < edges.Length; i++)
        {
            AddUnique(cells, edges[i].Source);
            for (int witness = 0; witness < edges[i].Witnesses.Length; witness++)
                AddUnique(cells, edges[i].Witnesses[witness]);
            AddUnique(cells, edges[i].Destination);
        }
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                cells.ToArray(),
                "diamond",
                edges,
                WeightedCell);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                fixture.CreateQuery(start, end, fixture.DefaultProfile));

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Cost.Should().Be(expectedCost);
        result.Nodes.Should().Equal(
            new NavigationCellAddress("diamond", start),
            new NavigationCellAddress("diamond", expectedMiddle),
            new NavigationCellAddress("diamond", end));
    }

    private static void AddUnique(List<VoxelIndex> values, VoxelIndex value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].Equals(value))
                return;
        }
        values.Add(value);
    }

    private static NavigationAStarExitTestHarness.ExplicitEdgeSpec Edge(
        string id,
        VoxelIndex source,
        VoxelIndex destination,
        Fixed64 cost) => new(
        id,
        source,
        destination,
        cost,
        radiusClearance: (Fixed64)2,
        witnesses: BuildWitnesses(source, destination));

    private static NavigationAStarExitTestHarness.ExplicitEdgeSpec Edge(
        string id,
        VoxelIndex source,
        VoxelIndex destination,
        int cost) => Edge(id, source, destination, (Fixed64)cost);

    private static NavigationAStarExitTestHarness.ExplicitEdgeSpec[] Reverse(
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] source)
    {
        var reversed = (NavigationAStarExitTestHarness.ExplicitEdgeSpec[])source.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    private static VoxelIndex[] BuildWitnesses(
        VoxelIndex source,
        VoxelIndex destination)
    {
        var witnesses = new List<VoxelIndex>();
        VoxelIndex current = source;
        while (!current.Equals(destination))
        {
            current = current.x != destination.x
                ? new VoxelIndex(
                    current.x + (current.x < destination.x ? 1 : -1),
                    current.y,
                    current.z)
                : current.y != destination.y
                    ? new VoxelIndex(
                        current.x,
                        current.y + (current.y < destination.y ? 1 : -1),
                        current.z)
                    : new VoxelIndex(
                        current.x,
                        current.y,
                        current.z + (current.z < destination.z ? 1 : -1));
            if (!current.Equals(destination))
                witnesses.Add(current);
        }
        return witnesses.ToArray();
    }
}
