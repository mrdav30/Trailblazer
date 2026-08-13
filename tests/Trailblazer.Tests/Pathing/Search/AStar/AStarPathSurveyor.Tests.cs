using System;
using System.Collections.Generic;
using System.Linq;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AStarSurveyorTests : IDisposable
{
    public AStarSurveyorTests()
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

        PathTestFactory.RegisterFromData(TestWorld.Context, "Line", data, origin);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, origin, target, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        Assert.Equal(3, guide.ActiveWaypoints.Length); // start, middle, end
        Assert.Equal(origin, guide.ActiveWaypoints[0].Position);
        Assert.Equal(target, guide.ActiveWaypoints.Last().Position);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Line");
    }

    [Fact]
    public void AStar_ShouldFailIfTargetUnreachable()
    {
        // Only one walkable tile
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "Isolated", new Vector3d(0, 0, 0));

        var unreachableTarget = new Vector3d(4, 0, 4);

        bool created = AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, unreachableTarget, Fixed64.One, out AStarPathRequest? request);

        Assert.False(created);
        Assert.Null(request);

        PathManager.UnloadChart("Isolated");
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

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, target, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        request.Heuristic = method;

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Diag");
    }

    [Fact]
    public void AStar_ShouldNotReturnImmediateSuccessOnSameStartAndEnd()
    {
        var pos = new Vector3d(1, 0, 1);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "SameSpot", pos);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, pos, pos, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide? guide);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "Detour", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 0);
        var target = new Vector3d(4, 0, 0);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, target, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
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

        PathTestFactory.RegisterFromData(TestWorld.Context, "Choke", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(1, 0, 1), new Vector3d(4, 0, 1), Fixed64.Two));

        request.IsValid.Should().BeTrue();

        bool success =
            PathGuideFactory.RequestGuide(request, out AStarGuide? guide);

        Assert.False(success);
        Assert.Null(guide);

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("Choke");
    }

    [Fact]
    public void AStar_ShouldFastFailRepeatedClearanceDisconnectedRequest()
    {
        bool[,,] data = PathTestFactory.BuildSingleVoxelChoke();
        PathTestFactory.RegisterFromData(TestWorld.Context, "ChokeFastFail", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(6, 0, 2),
            Fixed64.Two));

        SolidPartitionReachability.IsProvablyUnreachable(request).Should().BeTrue();

        PathGuideFactory.RequestGuide(request, out AStarGuide? firstGuide).Should().BeFalse();
        firstGuide.Should().BeNull();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide? secondGuide);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        success.Should().BeFalse();
        secondGuide.Should().BeNull();
        allocated.Should().BeLessThan(768);

        PathManager.UnloadChart("ChokeFastFail");
    }

    [Fact]
    public void AStar_ShouldRebuildReachabilitySnapshot_WhenClearanceKeyChanges()
    {
        bool[,,] data = PathTestFactory.BuildSingleVoxelChoke();
        PathTestFactory.RegisterFromData(TestWorld.Context, "ChokeKeySwitch", data, Vector3d.Zero);

        AStarPathRequest lowClimbRequest = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(6, 0, 2),
            Fixed64.Two));
        lowClimbRequest.MaxClimbHeight = Fixed64.Zero;

        AStarPathRequest highClimbRequest = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(6, 0, 2),
            Fixed64.Two));
        highClimbRequest.MaxClimbHeight = Fixed64.One;

        SolidPartitionReachability.IsProvablyUnreachable(lowClimbRequest).Should().BeTrue();
        SolidPartitionReachability.IsProvablyUnreachable(highClimbRequest).Should().BeTrue();
        SolidPartitionReachability.IsProvablyUnreachable(lowClimbRequest).Should().BeTrue();

        PathManager.UnloadChart("ChokeKeySwitch");
    }

    [Fact]
    public void AStarSurveyor_FindPath_ShouldKeepOpenPlane16ColdAllocationsUnderBudget()
    {
        TestWorld.Reset();
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(20, 4, 20)), out _);

        bool[,,] data = new bool[1, 16, 16];
        for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData(TestWorld.Context, "AStarOpenPlane16Alloc", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(
            AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(15, 0, 15), Fixed64.One, out AStarPathRequest? createdRequest),
            createdRequest);

        AStarSurveyor.Shared.FindPath(request).HasPath.Should().BeTrue();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        AStarSurveyResult result = AStarSurveyor.Shared.FindPath(request);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.HasPath.Should().BeTrue();
        TestRequire.NotNull(result.Waypoints).Should().NotBeEmpty();
        allocated.Should().BeLessThan(120_000);

        PathManager.UnloadChart("AStarOpenPlane16Alloc");
    }

    [Fact]
    public void AStar_ShouldReevaluateFastFail_WhenChartChangesMakeRouteReachable()
    {
        bool[,,] blocked = PathTestFactory.BuildSingleVoxelChoke();
        PathTestFactory.RegisterFromData(TestWorld.Context, "ChokeReevalBlocked", blocked, Vector3d.Zero);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(6, 0, 2),
            Fixed64.Two));

        PathGuideFactory.RequestGuide(request, out AStarGuide? blockedGuide).Should().BeFalse();
        blockedGuide.Should().BeNull();

        PathManager.UnloadChart("ChokeReevalBlocked");

        bool[,,] open = new bool[1, 7, 5];
        for (int x = 0; x < 7; x++)
        {
            for (int z = 0; z < 5; z++)
                open[0, x, z] = true;
        }

        PathTestFactory.RegisterFromData(TestWorld.Context, "ChokeReevalOpen", open, Vector3d.Zero);

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        guide.ActiveWaypoints.Last().IsGoal.Should().BeTrue();

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("ChokeReevalOpen");
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

        PathTestFactory.RegisterFromData(TestWorld.Context, "ResetTest", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        bool success1 =
            PathGuideFactory.RequestGuide(request, out AStarGuide? guide);

        TestRequire.Created(success1, guide);

        PathGuideFactory.ReturnGuide(guide);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "ResetTestBlocked", badData, Vector3d.Zero);

        AStarPathRequest failedRequest = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdfailedRequest), createdfailedRequest);

        bool success2 =
            PathGuideFactory.RequestGuide(failedRequest, out AStarGuide? failedGuide);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "SearchCap", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(8, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        request.MaxPathSearchRange = 4; // Set a low search limit to force failure

        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide? guide);

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

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 2), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.UseSplineSmoothing = true;
        Assert.True(guide.ActiveWaypoints.Length > 4); // should have inserted curve points

        PathGuideFactory.ReturnGuide(guide);

        PathManager.UnloadChart("LSpline");
    }

    [Fact]
    public void AStarSpline_ShouldSkipShortPaths()
    {
        PathTestFactory.RegisterFromData(TestWorld.Context, "ShortSpline", new bool[1, 2, 1]
        {
            {
                { true },
                { true }
            }
        }, new Vector3d(0, 0, 0));

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(1, 0, 0);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, end, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.UseSplineSmoothing = true;
        Assert.Equal(2, guide.ActiveWaypoints.Length); // No smoothing applied

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "SplineEnds", data, new Vector3d(0, 0, 0));

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(3, 0, 3);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, end, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.UseSplineSmoothing = true;
        Assert.Equal(start, guide.ActiveWaypoints.First().Position);
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

        PathTestFactory.RegisterFromData(TestWorld.Context, "CornerCut", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, new Vector3d(0, 0, 0), new Vector3d(1, 0, 1), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        bool success = PathGuideFactory.RequestGuide(request, out AStarGuide? guide);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "ShortestPath", data, new Vector3d(0, 0, 0));

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "BlockedMap", data, new Vector3d(0, 0, 0));

        bool created = AStarPathRequest.TryCreate(TestWorld.Context, new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), Fixed64.One, out AStarPathRequest? request);

        Assert.False(created);
        Assert.Null(request);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "Consistent", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide1 = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide1), createdGuide1);
        AStarGuide guide2 = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide2), createdGuide2);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "HeuristicImpact", data, Vector3d.Zero);

        var start = new Vector3d(1, 0, 2);
        var end = new Vector3d(4, 0, 2);

        AStarPathRequest manhattanRequest = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, end, Fixed64.One, out AStarPathRequest? createdmanhattanRequest), createdmanhattanRequest);
        manhattanRequest.Heuristic = HeuristicMethod.Manhattan;

        AStarPathRequest euclideanRequest = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, end, Fixed64.One, out AStarPathRequest? createdeuclideanRequest), createdeuclideanRequest);
        euclideanRequest.Heuristic = HeuristicMethod.Euclidean;

        AStarGuide manhattan = TestRequire.Created(PathGuideFactory.RequestGuide(manhattanRequest, out AStarGuide? createdManhattan), createdManhattan);
        AStarGuide euclidean = TestRequire.Created(PathGuideFactory.RequestGuide(euclideanRequest, out AStarGuide? createdEuclidean), createdEuclidean);

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

        PathTestFactory.RegisterFromData(TestWorld.Context, "ModifierBias", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 2);

        // Apply a high PathCostModifier to the direct diagonal path (1,0,1)
        Voxel diagonalVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 1));
        SolidChartPartition diagonalPartition = TestRequire.NotNull(diagonalVoxel.GetPartitionOrDefault<SolidChartPartition>());

        diagonalPartition.PathCostModifier = 1000;

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, start, end, Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

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

    [Fact]
    public void AStar_ProcessNeighbors_ShouldReturnFalse_WhenCurrentPartitionHasNoRecordedMeta()
    {
        bool[,,] data = new bool[1, 2, 1]
        {
            {
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "AStarMissingMeta", data, Vector3d.Zero);

        Voxel currentVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        SolidChartPartition current = TestRequire.Partition<SolidChartPartition>(currentVoxel);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarSurveyor surveyor = new();
        ReflectionUtility.SetPrivateField(surveyor, "_request", request);

        ReflectionUtility.InvokePrivate<bool>(surveyor, "ProcessNeighbors", current).Should().BeFalse();

        PathManager.UnloadChart("AStarMissingMeta");
    }

    [Fact]
    public void AStar_ProcessNeighbor_ShouldUpdateOpenNeighbor_WhenLowerMovementCostIsProvided()
    {
        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "AStarHelperUpdate", data, Vector3d.Zero);

        Voxel currentVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel neighborVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        SolidChartPartition current = TestRequire.Partition<SolidChartPartition>(currentVoxel);
        SolidChartPartition neighbor = TestRequire.Partition<SolidChartPartition>(neighborVoxel);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);

        AStarSurveyor surveyor = new();
        ReflectionUtility.SetPrivateField(surveyor, "_request", request);

        PathHeap<SolidChartPartition> heap = ReflectionUtility.GetPrivateField<PathHeap<SolidChartPartition>>(surveyor, "_heap");
        SwiftDictionary<WorldVoxelIndex, AStarVoxelMeta> meta = ReflectionUtility.GetPrivateField<SwiftDictionary<WorldVoxelIndex, AStarVoxelMeta>>(surveyor, "_meta");

        meta[neighbor.WorldIndex] = new AStarVoxelMeta
        {
            MovementCost = 300,
            NextTrailIndex = current.WorldIndex,
            PathCost = 999
        };
        heap.Add(neighbor, 999);

        ReflectionUtility.InvokePrivate<bool>(surveyor, "ProcessNeighbor", current, neighbor, 200).Should().BeFalse();

        meta[neighbor.WorldIndex].MovementCost.Should().Be(200);
        meta[neighbor.WorldIndex].NextTrailIndex.Should().Be(current.WorldIndex);
        heap.TryGetPathCost(neighbor, out int updatedPathCost).Should().BeTrue();
        updatedPathCost.Should().Be(
            neighbor.PathCostModifier
            + 200
            + AStarSurveyor.CalculateHeuristic(
                neighbor.VoxelPosition,
                TestRequire.NotNull(request.EndNode).WorldPosition,
                request.Heuristic));

        PathManager.UnloadChart("AStarHelperUpdate");
    }

    // -----------------------------------------------------------------
    // AStarSurveyor static helpers
    // -----------------------------------------------------------------

    [Fact]
    public void CalculateHeuristic_ShouldReturnMaxValue_ForUndefinedHeuristicMethod()
    {
        const HeuristicMethod undefinedHeuristic = (HeuristicMethod)(-1);

        int result = AStarSurveyor.CalculateHeuristic(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            undefinedHeuristic);

        result.Should().Be(Fixed64.MaxValue.CeilToInt());
    }

    [Fact]
    public void CalculateHeuristic_ShouldUseOctileCost_ForUnevenPlanarDeltas()
    {
        int result = AStarSurveyor.CalculateHeuristic(
            Vector3d.Zero,
            new Vector3d(3, 0, 1),
            HeuristicMethod.Octile);

        result.Should().Be((AStarSurveyor.DiagonalCost * 1) + (AStarSurveyor.StraightCost * 2));
    }

    /// <summary>
    /// Covers the <c>chartsUtilized ?? Array.Empty&lt;string&gt;()</c> null-coalescing branch
    /// in <c>AStarSurveyResult.Create</c> when the caller passes <c>null</c>.
    /// </summary>
    [Fact]
    public void AStarSurveyResult_Create_ShouldUseFallbackEmptyArray_WhenChartsUtilizedIsNull()
    {
        var waypoints = new[] { new AStarWaypoint { Position = Vector3d.Zero, IsGoal = true } };
        AStarSurveyResult result = AStarSurveyResult.Create(
            TestWorld.Context,
            waypoints,
            null!,
            TestPathRequest.CreateCacheKey(1));

        string[] chartsUtilized = TestRequire.NotNull(result.ChartsUtilized);
        chartsUtilized.Should().BeEmpty();
    }

    /// <summary>
    /// Covers the <c>return true</c> branch in <c>TryProcessDirection(DiagonalDirections)</c>
    /// (AStarSurveyor line 162) by placing the end node diagonally adjacent to the start, so the
    /// surveyor reaches the end node on the first diagonal sweep of the start voxel.
    /// </summary>
    [Fact]
    public void AStar_ShouldFindDirectDiagonalPath_WhenEndNodeIsDiagonallyAdjacentToStart()
    {
        // 2×2 XZ grid — all four corners walkable so the diagonal legs are clear.
        var data = new bool[1, 2, 2]
        {
            {
                { true, true },
                { true, true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "DiagonalEndNode", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(1, 0, 1), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.ActiveWaypoints.First().Position.Should().Be(Vector3d.Zero);
        guide.ActiveWaypoints.Last().Position.Should().Be(new Vector3d(1, 0, 1));

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("DiagonalEndNode");
    }

    [Fact]
    public void CatmullSmooth_ShouldReturnInputUnchanged_WhenFewerThanFourWaypoints()
    {
        AStarWaypoint[] three = new AStarWaypoint[]
        {
            new() { Position = Vector3d.Zero },
            new() { Position = new Vector3d(1, 0, 0) },
            new() { Position = new Vector3d(2, 0, 0) },
        };

        AStarWaypoint[] result = AStarSurveyor.CatmullSmooth(three);

        result.Should().BeSameAs(three);
    }

    [Fact]
    public void CatmullSmooth_ShouldSampleIntermediatePointsWithFixedPointFractions()
    {
        AStarWaypoint[] input =
        {
            new() { Position = Vector3d.Zero },
            new() { Position = new Vector3d(3, 0, 0) },
            new() { Position = new Vector3d(6, 0, 0) },
            new() { Position = new Vector3d(9, 0, 0) }
        };

        AStarWaypoint[] result = AStarSurveyor.CatmullSmooth(input, resolutionPerSegment: 3);

        result.Should().HaveCount(5);
        result[0].Position.Should().Be(input[0].Position);
        result[1].Position.X.Should().BeGreaterThan((Fixed64)3);
        result[2].Position.X.Should().BeGreaterThan(result[1].Position.X);
        result[2].Position.X.Should().BeLessThan((Fixed64)6);
        result[3].Position.Should().Be(input[2].Position);
        result[4].Position.Should().Be(input[3].Position);
    }

    // -----------------------------------------------------------------
    // AStarGuide runtime behaviour
    // -----------------------------------------------------------------

    [Fact]
    public void AStarGuide_GetCurrentWaypointDirection_ShouldReturnZero_WhenFirstWaypointIsAtOrigin()
    {
        // A path starting at (0,0,0) places the first waypoint at the world origin.
        // GetCurrentWaypointDirection returns zero when the waypoint position equals Vector3d.Zero.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideOriginWaypoint", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        // Index 0 is at (0,0,0) = Vector3d.Zero → movementDirection == Zero → return Zero.
        Vector3d dir = guide.GetCurrentWaypointDirection(new Vector3d(1, 0, 0));
        dir.Should().Be(Vector3d.Zero);

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideOriginWaypoint");
    }

    [Fact]
    public void AStarGuide_GetCurrentWaypointDirection_ShouldReturnDirection_WhenWaypointIsOffOrigin()
    {
        // After advancing the waypoint index past the origin, the direction should be non-zero.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideOffOriginDir", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        // Advance to a waypoint that is not at the origin so the branch is taken.
        guide.AdvanceWaypoint();

        Vector3d dir = guide.GetCurrentWaypointDirection(Vector3d.Zero);
        dir.Should().NotBe(Vector3d.Zero);

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideOffOriginDir");
    }

    [Fact]
    public void AStarGuide_TryGetWaypointAt_ShouldReturnFalse_WhenIndexIsOutOfRange()
    {
        var data = new bool[1, 2, 1]
        {
            { { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideOutOfRange", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        guide.TryGetWaypointAt(-1, out _).Should().BeFalse();
        guide.TryGetWaypointAt(999, out _).Should().BeFalse();
        guide.TryGetWaypointAt(0, out AStarWaypoint first).Should().BeTrue();
        first.Position.Should().Be(Vector3d.Zero);

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideOutOfRange");
    }

    [Fact]
    public void AStarGuide_TryGetFallbackDirection_ShouldReturnTrue_AndAdvanceSearchForward()
    {
        // Exercises the forward-search loop in TryGetFallbackDirection.
        // Sample from a position between waypoints so the nearest waypoint is ahead of
        // from, producing a non-zero direction.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideWaypointFallback", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        // Sample from halfway between the first and second waypoints so the nearest
        // ahead waypoint is (1,0,0) and the resulting direction is non-zero.
        bool ok = guide.TryGetFallbackDirection(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Zero), out Vector3d fallback);
        ok.Should().BeTrue();
        fallback.Should().NotBe(Vector3d.Zero);

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideWaypointFallback");
    }

    [Fact]
    public void AStarGuide_HasArrived_ShouldReturnTrue_WhenAtLastWaypoint()
    {
        // Exercises the true branch of HasArrived: CurrentWaypointIndex == Length - 1.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideHasArrived", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.HasArrived().Should().BeFalse("guide starts at index 0, not the last waypoint");

        // Advance to the last waypoint.
        for (int i = 0; i < guide.ActiveWaypoints.Length - 1; i++)
            guide.AdvanceWaypoint();

        guide.HasArrived().Should().BeTrue("index is now at the last waypoint");

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideHasArrived");
    }

    [Fact]
    public void AStarGuide_ShouldReturnSafeDefaults_WhenTrailMapHasNoPath()
    {
        AStarGuide guide = new();
        ReflectionUtility.SetPrivateField(
            guide,
            "<TrailMap>k__BackingField",
            AStarSurveyResult.Create(
                TestWorld.Context,
                Array.Empty<AStarWaypoint>(),
                Array.Empty<string>(),
                TestPathRequest.CreateCacheKey(0)));

        guide.HasArrived().Should().BeFalse();
        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d movementDirection).Should().BeFalse();
        movementDirection.Should().Be(Vector3d.Zero);
        guide.TryGetFallbackDirection(Vector3d.Zero, out Vector3d fallbackDirection).Should().BeFalse();
        fallbackDirection.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void AStarGuide_GetCurrentWaypointDirection_ShouldReturnZero_WhenIndexOutOfRange()
    {
        // Exercises the early-return guard when CurrentWaypointIndex >= ActiveWaypoints.Length.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideOutOfRangeDir", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        // Advance past the end so CurrentWaypointIndex >= Length.
        for (int i = 0; i < guide.ActiveWaypoints.Length; i++)
            guide.AdvanceWaypoint();

        guide.GetCurrentWaypointDirection(new Vector3d(1, 0, 0)).Should().Be(Vector3d.Zero,
            "index out of range triggers the early-return guard");

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideOutOfRangeDir");
    }

    [Fact]
    public void AStarGuide_GetCurrentWaypointDirection_ShouldReturnZero_WhenWaypointPositionIsOrigin()
    {
        // Exercises the movementDirection == Vector3d.Zero guard.
        // When CurrentWaypointIndex == 0 and the first waypoint is at world origin (0,0,0),
        // the position-as-direction check returns Zero early.
        var data = new bool[1, 3, 1]
        {
            { { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideZeroPos", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);

        // Index 0 waypoint position is (0,0,0) — the zero-position guard fires.
        guide.GetCurrentWaypointDirection(new Vector3d(1, 0, 0)).Should().Be(Vector3d.Zero,
            "first waypoint is at world origin so the position-as-direction check yields Zero");

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideZeroPos");
    }

    [Fact]
    public void AStarGuide_UseSplineSmoothing_ShouldReturnSmoothedWaypoints_WhenPathIsLongEnough()
    {
        // Exercises UseSplineSmoothing = true with at least 4 waypoints (CatmullSmooth path).
        // Creates a path long enough (4+ waypoints) so the smoothed waypoints cache is populated.
        var data = new bool[1, 5, 1]
        {
            { { true }, { true }, { true }, { true }, { true } }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideSpline", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.Created(AStarPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero, new Vector3d(4, 0, 0), Fixed64.One, out AStarPathRequest? createdrequest), createdrequest);
        AStarGuide guide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide), createdGuide);
        guide.UseSplineSmoothing = true;

        // First access builds the smoothed cache when Length >= 4.
        AStarWaypoint[] smoothed = guide.ActiveWaypoints;
        Assert.NotNull(smoothed);

        // Second access uses the cached smoothed waypoints.
        guide.ActiveWaypoints.Should().BeSameAs(smoothed, "cache is not rebuilt on second access");

        PathGuideFactory.ReturnGuide(guide);
        PathManager.UnloadChart("GuideSpline");
    }

}
