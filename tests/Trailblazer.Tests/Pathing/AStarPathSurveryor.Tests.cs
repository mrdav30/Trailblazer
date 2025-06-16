using Xunit;
using FixedMathSharp;
using Trailblazer.Pathing;
using GridForge.Configuration;
using GridForge.Grids;
using System.Linq;
using FluentAssertions;
using System.Collections.Generic;

namespace Trailblazer.Tests.Pathing
{
    [Collection("PathingCollection")]
    public class AStarSurveryorTests
    {
        public AStarSurveryorTests()
        {
            var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
            GlobalGridManager.TryAddGrid(config, out _);
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

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(origin, target, request, out AStarGuide guide);

            Assert.True(success);
            Assert.NotNull(guide);
            Assert.Equal(3, guide.ActiveWaypoints!.Length); // start, middle, end
            Assert.Equal(origin, guide.ActiveWaypoints[0].Position);
            Assert.Equal(target, guide.ActiveWaypoints.Last().Position);

            PathManager.UnloadChart("Line");
        }

        [Fact]
        public void AStar_ShouldFailIfTargetUnreachable()
        {
            // Only one walkable tile
            PathTestFactory.RegisterSingleWalkablePoint("Isolated", new Vector3d(0, 0, 0));

            var unreachableTarget = new Vector3d(4, 0, 4);

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(Vector3d.Zero, unreachableTarget, request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

#if DEBUG
            AStarSurveyor.OnHeightLimitViolated = (from, to, delta) =>
            {
                if (delta > maxHeightDifference)
                    heightViolationTriggered = true;
            };
#endif

            var request = AStarPathRequest.CreateEmpty();
            request.MaxClimbHeight = maxHeightDifference;

            bool success = PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(5, 5, 0), request, out AStarGuide guide);

            Assert.True(heightViolationTriggered);

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

            var request = AStarPathRequest.CreateEmpty();
            request.Heuristic = method;

            bool success = PathGuideFactory.RequestGuide(start, target, request, out AStarGuide guide);

            Assert.True(success);
            Assert.NotNull(guide);

            PathManager.UnloadChart("Diag");
        }

        [Fact]
        public void AStar_ShouldNotReturnImmediateSuccessOnSameStartAndEnd()
        {
            var pos = new Vector3d(1, 0, 1);
            PathTestFactory.RegisterSingleWalkablePoint("SameSpot", pos);

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(pos, pos, request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(start, target, request, out AStarGuide guide);

            Assert.True(success);
            Assert.NotNull(guide);
            // Must have detoured around
            List<Vector3d> waypoints = guide.ActiveWaypoints.Select(p => p.Position).ToList();
            Assert.Contains(new Vector3d(2, 0, 2), waypoints);
            Assert.Contains(new Vector3d(3, 0, 1), waypoints);

            PathManager.UnloadChart("Detour");
        }

        [Fact]
        public void AStar_ShouldFailWhenClearanceTooLow()
        {
            // Build 3-wide corridor with a 2-block choke
            var data = new bool[1, 5, 3]
            {
                {
                    { true, true, true },
                    { true, true, true },
                    { false, true, false },
                    { true, true, true },
                    { true, true, true }
                }
            };

            PathTestFactory.RegisterFromData("Choke", data, Vector3d.Zero);

            var request = AStarPathRequest.CreateEmpty();
            request.UnitSize = (Fixed64)2;

            bool success =
                PathGuideFactory.RequestGuide(new Vector3d(0, 0, 1), new Vector3d(4, 0, 1), request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

            var request = AStarPathRequest.CreateEmpty();

            bool success1 =
                PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(2, 0, 0), request, out AStarGuide guide);

            Assert.True(success1);
            Assert.NotNull(guide);

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

            var failedRequest = AStarPathRequest.CreateEmpty();

            bool success2 =
                PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(2, 0, 0), failedRequest, out AStarGuide failedGuide);

            Assert.False(success2);
            Assert.Null(failedGuide);

            PathManager.UnloadChart("ResetTestBlocked");
        }

        [Fact]
        public void AStar_ShouldFailWhenSearchLimitExceeded()
        {
            var data = new bool[1, 50, 1];
            for (int i = 0; i < 50; i++)
                data[0, i, 0] = true;

            PathTestFactory.RegisterFromData("SearchCap", data, Vector3d.Zero);

            var request = AStarPathRequest.CreateEmpty();
            request.MaxPathSearchRange = 10;

            bool success = PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(49, 0, 0), request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(2, 0, 2), request, out AStarGuide guide);
            guide.UseSplineSmoothing = true;

            Assert.True(success);
            Assert.NotNull(guide);
            Assert.True(guide.ActiveWaypoints!.Length > 4); // should have inserted curve points

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

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(start, end, request, out AStarGuide guide);
            guide.UseSplineSmoothing = true;

            Assert.True(success);
            Assert.NotNull(guide);
            Assert.Equal(2, guide.ActiveWaypoints!.Length); // No smoothing applied

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

            var request = AStarPathRequest.CreateEmpty();

            bool success = PathGuideFactory.RequestGuide(start, end, request, out AStarGuide guide);
            guide.UseSplineSmoothing = true;

            Assert.True(success);
            Assert.NotNull(guide);
            Assert.Equal(start, guide.ActiveWaypoints!.First().Position);
            Assert.Equal(end, guide.ActiveWaypoints.Last().Position);

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

            var request = AStarPathRequest.CreateEmpty();
            request.UnitSize = (Fixed64)1;
            bool success = PathGuideFactory.RequestGuide(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1), request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

            var request = AStarPathRequest.CreateEmpty();
            bool success = PathGuideFactory.RequestGuide(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), request, out AStarGuide guide);

            Assert.True(success);
            Assert.NotNull(guide);

            // Ensure path goes through (1,0,1) as optimal diagonal
            Assert.Contains(new Vector3d(1, 0, 1), guide.ActiveWaypoints.Select(w => w.Position));

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

            var request = AStarPathRequest.CreateEmpty();
            bool success = PathGuideFactory.RequestGuide(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), request, out AStarGuide guide);

            Assert.False(success);
            Assert.Null(guide);

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

            var request = AStarPathRequest.CreateEmpty();

            bool success1 = PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(2, 0, 0), request, out AStarGuide guide1);
            bool success2 = PathGuideFactory.RequestGuide(Vector3d.Zero, new Vector3d(2, 0, 0), request, out AStarGuide guide2);

            Assert.True(success1);
            Assert.True(success2);
            Assert.Equal(guide1.ActiveWaypoints.Length, guide2.ActiveWaypoints.Length);

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

            var manhattanRequest = AStarPathRequest.CreateEmpty();
            manhattanRequest.Heuristic = HeuristicMethod.Manhattan;

            var euclideanRequest = AStarPathRequest.CreateEmpty();
            euclideanRequest.Heuristic = HeuristicMethod.Euclidean;

            PathGuideFactory.RequestGuide(start, end, manhattanRequest, out AStarGuide manhattan);
            PathGuideFactory.RequestGuide(start, end, euclideanRequest, out AStarGuide euclidean);

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

            PathManager.UnloadChart("HeuristicImpact");
        }
    }
}
