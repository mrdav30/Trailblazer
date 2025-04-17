using Xunit;
using FixedMathSharp;
using SwiftCollections;
using Trailblazer.Pathing;
using GridForge.Configuration;
using GridForge.Grids;
using System.Linq;

namespace Trailblazer.Tests.Pathing
{
    [Collection("TraversableNavMapCollection")]
    public class AStarPathFinderTests
    {
        public AStarPathFinderTests()
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

            var map = PathTestFactory.RegisterFromData("Line", data, origin);

            SwiftList<Vector3d>? resultPath = null;
            var request = PathTestFactory.CreateRequest(origin, target, 1, (success, path) =>
            {
                Assert.True(success);
                resultPath = path;
            });

            PathingManager.RequestPath(request);

            Assert.NotNull(resultPath);
            Assert.Equal(3, resultPath!.Count); // start, middle, end
            Assert.Equal(origin, resultPath[0]);
            Assert.Equal(target, resultPath.Last());

            PathingManager.Unload("Line");
        }

        [Fact]
        public void AStar_ShouldFailIfTargetUnreachable()
        {
            // Only one walkable tile
            PathTestFactory.RegisterSingleWalkablePoint("Isolated", new Vector3d(0, 0, 0));

            var unreachableTarget = new Vector3d(4, 0, 4);
            bool wasSuccess = false;

            var request = PathTestFactory.CreateRequest(new Vector3d(0, 0, 0), unreachableTarget, 1, (success, path) =>
            {
                wasSuccess = success;
            });

            PathingManager.RequestPath(request);

            Assert.False(wasSuccess);

            PathingManager.Unload("Isolated");
        }

        [Fact]
        public void AStar_ShouldReportHeightLimitViolation()
        {
            bool[,,] data = new bool[6, 6, 1];
            for (int i = 0; i < 6; i++)
                data[i, i, 0] = true;

            var map = PathNavigationMap.From3D("HeightSpy", data, new Vector3d(0, 0, 0), Fixed64.One);
            PathingManager.Register(map);
            PathingManager.InitializeMap("HeightSpy");

            Fixed64 maxHeightDifference = Fixed64.Half;
            bool heightViolationTriggered = false;

#if DEBUG
            AStarPathFinder.OnHeightLimitViolated = (from, to, delta) =>
            {
                if (delta > maxHeightDifference)
                    heightViolationTriggered = true;
            };
#endif

            var request = PathTestFactory.CreateRequest(
                new Vector3d(0, 0, 0),
                new Vector3d(5, 5, 0),
                1,
                (success, _) => Assert.False(success)
            );

            request.MaxClimbHeight = maxHeightDifference;
            PathingManager.RequestPath(request);

            Assert.True(heightViolationTriggered);

            PathingManager.Unload("HeightSpy");
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
            var map = PathNavigationMap.From3D("Diag", new bool[1, 3, 3]
            {
        {
            { true, true, true },
            { false, true, false },
            { true, true, true }
        }
            }, start, Fixed64.One);

            PathingManager.Register(map);
            PathingManager.InitializeMap("Diag");

            SwiftList<Vector3d>? result = null;
            var request = PathTestFactory.CreateRequest(start, target, 1, (success, path) =>
            {
                Assert.True(success);
                result = path;
            });

            request.Heuristic = method;
            PathingManager.RequestPath(request);

            Assert.NotNull(result);
            Assert.True(result!.Count > 1);

            PathingManager.Unload("Diag");
        }

        [Fact]
        public void AStar_ShouldReturnImmediateSuccessOnSameStartAndEnd()
        {
            var pos = new Vector3d(1, 0, 1);
            PathTestFactory.RegisterSingleWalkablePoint("SameSpot", pos);

            SwiftList<Vector3d>? path = null;
            var request = PathTestFactory.CreateRequest(pos, pos, 1, (success, result) =>
            {
                Assert.True(success);
                path = result;
            });

            PathingManager.RequestPath(request);

            Assert.NotNull(path);
            Assert.Single(path);
            Assert.Equal(pos, path[0]);

            PathingManager.Unload("SameSpot");
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

            var map = PathTestFactory.RegisterFromData("Detour", data, new Vector3d(0, 0, 0));

            var start = new Vector3d(0, 0, 0);
            var target = new Vector3d(4, 0, 0);
            SwiftList<Vector3d>? resultPath = null;

            var request = PathTestFactory.CreateRequest(start, target, 1, (success, path) =>
            {
                resultPath = path;
            });

            PathingManager.RequestPath(request);

            Assert.NotNull(resultPath);
            Assert.Contains(new Vector3d(2, 0, 1), resultPath!); // Must have detoured around

            PathingManager.Unload("Detour");
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

            var map = PathTestFactory.RegisterFromData("Choke", data, new Vector3d(0, 0, 0));

            var pathFound = false;
            var request = PathTestFactory.CreateRequest(new Vector3d(0, 0, 1), new Vector3d(4, 0, 1), roverSize: 2, (success, path) => pathFound = success);

            PathingManager.RequestPath(request);

            Assert.False(pathFound);

            PathingManager.Unload("Choke");
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

            var map = PathTestFactory.RegisterFromData("ResetTest", data, new Vector3d(0, 0, 0));

            SwiftList<Vector3d>? result = null;
            var pathFound = false;
            var req = PathTestFactory.CreateRequest(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), 1, (success, path) =>
            {
                pathFound = success;
                result = path;
            });

            PathingManager.RequestPath(req);

            Assert.True(pathFound);
            Assert.NotNull(result);

            // Second request with blocked path
            PathingManager.Unload("ResetTest");

            var badData = new bool[1, 3, 1]
            {
                {
                    { true }, 
                    { false }, 
                    { true }
                }
            };

            var brokenMap = PathTestFactory.RegisterFromData("ResetTestBlocked", badData, new Vector3d(0, 0, 0));

            bool failed = false;
            var req2 = PathTestFactory.CreateRequest(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), 1, (success, path) => {
                failed = !success;
            });
            PathingManager.RequestPath(req2);

            Assert.True(failed);

            PathingManager.Unload("ResetTestBlocked");
        }

        [Fact]
        public void AStar_ShouldFailWhenSearchLimitExceeded()
        {
            var data = new bool[1, 50, 1];
            for (int i = 0; i < 50; i++)
                data[0, i, 0] = true;

            var map = PathTestFactory.RegisterFromData("SearchCap", data, new Vector3d(0, 0, 0));

            var req = PathTestFactory.CreateRequest(new Vector3d(0, 0, 0), new Vector3d(49, 0, 0), 1, (success, _) =>
            {
                Assert.False(success); // Won't reach the end
            });

            req.MaxSearchSize = 10;

            PathingManager.RequestPath(req);
            PathingManager.Unload("SearchCap");
        }

        [Fact]
        public void AStar_SplineSmoothProducesMorePoints()
        {
            // L-shaped path
            var map = PathNavigationMap.From3D("LSpline", new bool[1, 3, 3]
            {
                {
                    { true, true, true },
                    { false, false, true },
                    { false, false, true }
                }
            }, new Vector3d(0, 0, 0), Fixed64.One);

            PathingManager.Register(map);
            PathingManager.InitializeMap("LSpline");

            SwiftList<Vector3d>? resultPath = null;
            var request = PathTestFactory.CreateRequest(
                new Vector3d(0, 0, 0),
                new Vector3d(2, 0, 2),
                1,
                (success, path) => resultPath = path
            );
            request.UseSplineSmoothing = true;

            PathingManager.RequestPath(request);

            Assert.NotNull(resultPath);
            Assert.True(resultPath!.Count > 3); // should have inserted curve points

            PathingManager.Unload("LSpline");
        }

        [Fact]
        public void AStarSpline_ShouldSkipShortPaths()
        {
            var map = PathTestFactory.RegisterFromData("ShortSpline", new bool[1, 2, 1]
            {
                {
                    { true },
                    { true }
                }
            }, new Vector3d(0, 0, 0));

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(1, 0, 0);

            SwiftList<Vector3d>? resultPath = null;
            var req = PathTestFactory.CreateRequest(start, end, 1, (success, path) => resultPath = path);
            req.UseSplineSmoothing = true;

            PathingManager.RequestPath(req);

            Assert.NotNull(resultPath);
            Assert.Equal(2, resultPath!.Count); // No smoothing applied

            PathingManager.Unload("ShortSpline");
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

            var map = PathTestFactory.RegisterFromData("SplineEnds", data, new Vector3d(0, 0, 0));

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(3, 0, 3);

            SwiftList<Vector3d>? resultPath = null;
            var req = PathTestFactory.CreateRequest(start, end, 1, (success, path) => resultPath = path);
            req.UseSplineSmoothing = true;

            PathingManager.RequestPath(req);

            Assert.NotNull(resultPath);
            Assert.Equal(start, resultPath!.First());
            Assert.Equal(end, resultPath.Last());

            PathingManager.Unload("SplineEnds");
        }

    }
}
