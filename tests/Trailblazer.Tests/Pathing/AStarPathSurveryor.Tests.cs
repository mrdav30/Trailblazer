using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AStarSurveryorTests : IDisposable
{
    public AStarSurveryorTests()
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
    public void AStar_ShouldReturnDirectPathBetweenTwoPoints()
    {
        // Build 3 walkable points in a line
        var origin = new Vector3d(0, 0, 0);
        var target = new Vector3d(2, 0, 0);

        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("Line", data, origin);

        AStarPathRequest.TryCreate(origin, target, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(success);
        Assert.NotNull(guide);
        Assert.Equal(3, guide.ActiveWaypoints!.Length); // start, middle, end
        Assert.Equal(origin, guide.ActiveWaypoints[0].Position);
        Assert.Equal(target, guide.ActiveWaypoints.Last().Position);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Line");
    }

    [Fact]
    public void AStar_ShouldFailIfTargetUnreachable()
    {
        // Only one walkable tile
        PathTestFactory.RegisterSingleWalkablePoint("Isolated", new Vector3d(0, 0, 0));

        var unreachableTarget = new Vector3d(4, 0, 4);

        AStarPathRequest.TryCreate(Vector3d.Zero, unreachableTarget, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Isolated");
    }

    [Fact]
    public void AStar_ShouldReportHeightLimitViolation()
    {
        bool[,,] data = new bool[6, 6, 6];
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                data[y, x, 0] = true;

        var map = NavigationChart.From3D("HeightSpy", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(map);
        PathManager.InitializeChart("HeightSpy");

        Fixed64 maxHeightDifference = Fixed64.Half;
        bool heightViolationTriggered = false;

        AStarSurveyor.OnHeightLimitViolated = (from, to, delta) =>
        {
            if (delta > maxHeightDifference)
                heightViolationTriggered = true;
        };

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(5, 5, 0), out AStarPathRequest request);
        request.MaxClimbHeight = maxHeightDifference;

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(heightViolationTriggered);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("HeightSpy");
    }

    [Theory]
    [InlineData(HeuristicMethod.Manhattan)]
    [InlineData(HeuristicMethod.Euclidean)]
    [InlineData(HeuristicMethod.Octile)]
    public void AStar_ShouldSupportAllHeuristics(HeuristicMethod method)
    {
        var start = new Vector3d(0, 0, 0);
        var target = new Vector3d(2, 0, 2);

        // Simple diagonal reachable path
        var map = NavigationChart.From3D("Diag", new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { false, true, false },
                { true, true, true }
            }
        }, start, Fixed64.One);

        PathManager.Register(map);
        PathManager.InitializeChart("Diag");

        AStarPathRequest.TryCreate(start, target, out AStarPathRequest request);
        request.Heuristic = method;

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(success);
        Assert.NotNull(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Diag");
    }

    [Fact]
    public void AStar_ShouldNotReturnImmediateSuccessOnSameStartAndEnd()
    {
        var pos = new Vector3d(1, 0, 1);
        PathTestFactory.RegisterSingleWalkablePoint("SameSpot", pos);

        AStarPathRequest.TryCreate(pos, pos, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("SameSpot");
    }

    [Fact]
    public void AStar_ShouldPreferLongerButLowerCostPath()
    {
        // Block direct route, allow a longer walkable detour
        var data = new bool[1, 5, 3]
        {
            {
                { true,  true,  true },
                { false, false, true },
                { true,  true,  true },
                { true,  true,  true },
                { true,  true,  true }
            }
        };

        PathTestFactory.RegisterFromData("Detour", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 0);
        var target = new Vector3d(4, 0, 0);

        AStarPathRequest.TryCreate(start, target, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(success);
        Assert.NotNull(guide);
        // Must have detoured around
        List<Vector3d> waypoints = guide.ActiveWaypoints.Select(p => p.Position).ToList();
        Assert.Contains(new Vector3d(2, 0, 2), waypoints);
        Assert.Contains(new Vector3d(3, 0, 1), waypoints);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Detour");
    }

    [Fact]
    public void AStar_ShouldFailWhenClearanceTooLow()
    {
        // Build 4-wide corridor with a 3-block choke
        var data = new bool[1, 7, 4]
        {
            {
                { true, true, true, true },
                { true, true, true, true },
                { true, true, true, true },
                { false, true, false, false },
                { true, true, true, true },
                { true, true, true, true },
                { true, true, true, true },
            }
        };

        PathTestFactory.RegisterFromData("Choke", data, Vector3d.Zero);

        var request = AStarPathRequest.Create(new Vector3d(1, 0, 1), new Vector3d(4, 0, 1), Fixed64.Two);

        request.IsValid.Should().BeTrue();

        bool success =
            PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Choke");
    }

    [Fact]
    public void AStar_ShouldNotReuseStalePathData()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("ResetTest", data, Vector3d.Zero);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest request);

        bool success1 =
            PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(success1);
        Assert.NotNull(guide);

        //  PathGuideFactory.ReturnGuide(guide);

        // Second request with blocked path
        PathManager.UnloadChart("ResetTest");

        var badData = new bool[1, 3, 1]
        {
            {
                { true },
                { false },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("ResetTestBlocked", badData, Vector3d.Zero);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest failedRequest);

        bool success2 =
            PathGuideFactory.RequestGuide(failedRequest, out AStarGuide failedGuide);

        Assert.False(success2);
        Assert.Null(failedGuide);

        PathGuideFactory.ReturnGuide(failedGuide);

        PathManager.UnloadChart("ResetTestBlocked");
    }

    [Fact]
    public void AStar_ShouldFailWhenSearchLimitExceeded()
    {
        var data = new bool[1, 50, 1];
        for (int i = 0; i < 50; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData("SearchCap", data, Vector3d.Zero);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(8, 0, 0), out AStarPathRequest request);
        request.MaxPathSearchRange = 4; // Set a low search limit to force failure

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("SearchCap");
    }

    [Fact]
    public void AStar_SplineSmoothProducesMorePoints()
    {
        // L-shaped path
        var map = NavigationChart.From3D("LSpline", new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { false, false, true },
                { false, false, true }
            }
        }, new Vector3d(0, 0, 0), Fixed64.One);

        PathManager.Register(map);
        PathManager.InitializeChart("LSpline");

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 2), out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);
        guide.UseSplineSmoothing = true;

        Assert.True(success);
        Assert.NotNull(guide);
        Assert.True(guide.ActiveWaypoints!.Length > 4); // should have inserted curve points

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("LSpline");
    }

    [Fact]
    public void AStarSpline_ShouldSkipShortPaths()
    {
        PathTestFactory.RegisterFromData("ShortSpline", new bool[1, 2, 1]
        {
            {
                { true },
                { true }
            }
        }, new Vector3d(0, 0, 0));

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(1, 0, 0);

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);
        guide.UseSplineSmoothing = true;

        Assert.True(success);
        Assert.NotNull(guide);
        Assert.Equal(2, guide.ActiveWaypoints!.Length); // No smoothing applied

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("ShortSpline");
    }

    [Fact]
    public void AStarSpline_ShouldIncludeOriginalEndpoints()
    {
        var data = new bool[1, 4, 4]
        {
            {
                { true, true, true, true },
                { false, false, false, true },
                { false, false, false, true },
                { false, false, false, true }
            }
        };

        PathTestFactory.RegisterFromData("SplineEnds", data, new Vector3d(0, 0, 0));

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(3, 0, 3);

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);
        guide.UseSplineSmoothing = true;

        Assert.True(success);
        Assert.NotNull(guide);
        Assert.Equal(start, guide.ActiveWaypoints!.First().Position);
        Assert.Equal(end, guide.ActiveWaypoints.Last().Position);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("SplineEnds");
    }

    [Fact]
    public void AStar_ShouldNotCutDiagonallyThroughCorner()
    {
        var data = new bool[1, 2, 2]
        {
            {
                { true, false },
                { false, true }
            }
        };

        PathTestFactory.RegisterFromData("CornerCut", data, Vector3d.Zero);

        AStarPathRequest.TryCreate(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1), out AStarPathRequest request);
        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("CornerCut");
    }

    [Fact]
    public void AStar_ShouldChooseShortestPath_WhenAllCostsEqual()
    {
        var data = new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { true, true, true },
                { true, true, true }
            }
        };

        PathTestFactory.RegisterFromData("ShortestPath", data, new Vector3d(0, 0, 0));

        AStarPathRequest.TryCreate(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), out AStarPathRequest request);
        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.True(success);
        Assert.NotNull(guide);

        // Ensure path goes through (1,0,1) as optimal diagonal
        Assert.Contains(new Vector3d(1, 0, 1), guide.ActiveWaypoints.Select(w => w.Position));

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("ShortestPath");
    }

    [Fact]
    public void AStar_ShouldFailOnFullyBlockedMap()
    {
        var data = new bool[1, 3, 3]
        {
            {
                { false, false, false },
                { false, false, false },
                { false, false, false }
            }
        };

        PathTestFactory.RegisterFromData("BlockedMap", data, new Vector3d(0, 0, 0));

        AStarPathRequest.TryCreate(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), out AStarPathRequest request);
        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("BlockedMap");
    }

    [Fact]
    public void AStar_ShouldReturnConsistentPath_ForSameRequest()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("Consistent", data, Vector3d.Zero);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest request);
        bool success1 = PathGuideFactory.RequestGuide(request, out AStarGuide guide1);
        bool success2 = PathGuideFactory.RequestGuide(request, out AStarGuide guide2);

        Assert.True(success1);
        Assert.True(success2);
        Assert.Equal(guide1.ActiveWaypoints.Length, guide2.ActiveWaypoints.Length);

        PathGuideFactory.ReturnGuide(guide1);
        PathGuideFactory.ReturnGuide(guide2);

        PathManager.UnloadChart("Consistent");
    }

    [Fact]
    public void AStar_HeuristicChoice_ShouldAffectPathPathing()
    {
        var data = new bool[1, 5, 5];

        // Create a map with a cross-pattern obstacle to force different choices
        for (int z = 0; z < 5; z++)
        {
            for (int x = 0; x < 5; x++)
                data[0, z, x] = true;
        }

        // Create a wall along center row (except at edge)
        data[0, 2, 1] = false;
        data[0, 2, 2] = false;
        data[0, 2, 3] = false;

        PathTestFactory.RegisterFromData("HeuristicImpact", data, Vector3d.Zero);

        var start = new Vector3d(1, 0, 2);
        var end = new Vector3d(4, 0, 2);

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest manhattanRequest);
        manhattanRequest.Heuristic = HeuristicMethod.Manhattan;

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest euclideanRequest);
        euclideanRequest.Heuristic = HeuristicMethod.Euclidean;

        PathGuideFactory.RequestGuide(manhattanRequest, out AStarGuide manhattan);
        PathGuideFactory.RequestGuide(euclideanRequest, out AStarGuide euclidean);

        // Both should succeed
        manhattan.Should().NotBeNull();
        euclidean.Should().NotBeNull();

        // But they should not take the exact same paths (due to tie-breaking preferences)
        var manhattanPath = manhattan.ActiveWaypoints
            .Select(wp => wp.Position)
            .Skip(1)
            .SkipFromEnd(1)
            .ToArray();

        var euclideanPath = euclidean.ActiveWaypoints
            .Select(wp => wp.Position)
            .Skip(1)
            .SkipFromEnd(1)
            .ToArray();

        manhattanPath.Should().NotEqual(euclideanPath);

        manhattan.ActiveWaypoints.First().Position.Should().Be(start);
        manhattan.ActiveWaypoints.Last().Position.Should().Be(end);

        euclidean.ActiveWaypoints.First().Position.Should().Be(start);
        euclidean.ActiveWaypoints.Last().Position.Should().Be(end);

        PathGuideFactory.ReturnGuide(manhattan);
        PathGuideFactory.ReturnGuide(euclidean);

        PathManager.UnloadChart("HeuristicImpact");
    }

    [Fact]
    public void AStar_ShouldAvoidHighCostPartitions_WhenModifiersAreApplied()
    {
        // Build a 3x3 walkable grid
        var data = new bool[1, 3, 3]
        {
    {
        { true, true, true },
        { true, true, true },
        { true, true, true }
    }
        };

        PathTestFactory.RegisterFromData("ModifierBias", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 2);

        // Apply a high PathCostModifier to the direct diagonal path (1,0,1)
        GlobalGridManager.TryGetGridAndVoxel(new Vector3d(1, 0, 1), out _, out Voxel diagonalVoxel);
        diagonalVoxel.Should().NotBeNull("Expected midpoint voxel to exist");

        var diagonalPartition = diagonalVoxel.GetPartitionOrDefault<PathPartition>();
        diagonalPartition.Should().NotBeNull("Expected midpoint partition to exist");

        diagonalPartition.PathCostModifier = 1000;

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide guide);

        success.Should().BeTrue("Pathfinding should succeed even if one path is expensive");

        var middlePositions = guide.ActiveWaypoints
            .Select(p => p.Position)
            .Skip(1)
            .SkipFromEnd(1)
            .ToArray();

        middlePositions.Should().NotContain(new Vector3d(1, 0, 1),
            "The path should avoid the heavily penalized partition");

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("ModifierBias");
    }
}
