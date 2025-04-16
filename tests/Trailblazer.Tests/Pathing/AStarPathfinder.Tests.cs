using Xunit;
using FixedMathSharp;
using SwiftCollections;
using Trailblazer.Pathing;
using GridForge.Configuration;
using GridForge.Grids;

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
        public void RemoveFirst_WhenCountIsOne_ShouldClearRootSafely()
        {
            PathPartitionHeap.FastClear();
            var node = new PathPartition();
            node.HeapCost = 1;

            PathPartitionHeap.Add(node);
            Assert.Equal(1u, PathPartitionHeap.Count);

            PathPartitionHeap.RemoveFirst(out PathPartition removed);
            Assert.Equal(node, removed);
            Assert.Equal(0u, PathPartitionHeap.Count);

            // Should not leave stale data
            Assert.Null(PathPartitionHeap.PeekAt(0));
        }

        [Fact]
        public void AStar_ShouldReturnDirectPathBetweenTwoPoints()
        {
            // Build 3 walkable points in a line
            var origin = new Vector3d(0, 0, 0);
            var target = new Vector3d(2, 0, 0);

            var map = PathNavigationMap.From3D("Line", new bool[1, 3, 1]
            {
                { { true }, { true }, { true } }
            }, origin, Fixed64.One);

            PathingManager.Register(map);
            PathingManager.InitializeMap("Line");

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

            // TODO: this is adding (1, 0, 2) twice!
            Assert.NotNull(resultPath);
            Assert.True(resultPath!.Count > 3); // should have inserted curve points

            PathingManager.Unload("LSpline");
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
    }
}
