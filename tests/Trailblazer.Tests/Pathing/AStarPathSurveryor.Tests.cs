using Xunit;
using FixedMathSharp;
using Trailblazer.Pathing;
using GridForge.Configuration;
using GridForge.Grids;
using System.Linq;

namespace Trailblazer.Tests.Pathing
{
    [Collection("PathingCollection")]
    public class AStarPathSurveryorTests
    {
        public AStarPathSurveryorTests()
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

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(origin, target, request);

            Assert.True(guide.IsValid);
            Assert.NotNull(guide.Path);
            Assert.Equal(3, guide.Path!.Count); // start, middle, end
            Assert.Equal(origin, guide.Path[0]);
            Assert.Equal(target, guide.Path.Last());

            PathManager.Unload("Line");
        }

        [Fact]
        public void AStar_ShouldFailIfTargetUnreachable()
        {
            // Only one walkable tile
            PathTestFactory.RegisterSingleWalkablePoint("Isolated", new Vector3d(0, 0, 0));

            var unreachableTarget = new Vector3d(4, 0, 4);

            var request = AStarPathRequest.CreateEmpty();

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, unreachableTarget, request);

            Assert.Null(guide);

            PathManager.Unload("Isolated");
        }

        [Fact]
        public void AStar_ShouldReportHeightLimitViolation()
        {
            bool[,,] data = new bool[6, 6, 1];
            for (int i = 0; i < 6; i++)
                data[i, i, 0] = true;

            var map = NavigationChart.From3D("HeightSpy", data, new Vector3d(0, 0, 0), Fixed64.One);
            PathManager.Register(map);
            PathManager.InitializeMap("HeightSpy");

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

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, new Vector3d(5, 5, 0), request);

            Assert.True(heightViolationTriggered);

            PathManager.Unload("HeightSpy");
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
            PathManager.InitializeMap("Diag");

            var request = AStarPathRequest.CreateEmpty();
            request.Heuristic = method;

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(start, target, request);

            Assert.NotNull(guide);
            Assert.True(guide.IsValid);

            PathManager.Unload("Diag");
        }

        [Fact]
        public void AStar_ShouldNotReturnImmediateSuccessOnSameStartAndEnd()
        {
            var pos = new Vector3d(1, 0, 1);
            PathTestFactory.RegisterSingleWalkablePoint("SameSpot", pos);

            var request = AStarPathRequest.CreateEmpty();

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(pos, pos, request);

            Assert.Null(guide);

            PathManager.Unload("SameSpot");
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

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(start, target, request);

            Assert.NotNull(guide);
            Assert.Contains(new Vector3d(2, 0, 1), guide.Path!); // Must have detoured around

            PathManager.Unload("Detour");
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

            AStarGuide guide =
                PathGuideFactory.RequestGuide<AStarGuide>(new Vector3d(0, 0, 1), new Vector3d(4, 0, 1), request);

            Assert.Null(guide);

            PathManager.Unload("Choke");
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

            AStarGuide guide =
                PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, new Vector3d(2, 0, 0), request);

            Assert.NotNull(guide);
            Assert.True(guide.IsValid);

            // Second request with blocked path
            PathManager.Unload("ResetTest");

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

            AStarGuide failedGuide =
                PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, new Vector3d(2, 0, 0), failedRequest);

            Assert.Null(failedGuide);

            PathManager.Unload("ResetTestBlocked");
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

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, new Vector3d(49, 0, 0), request);

            Assert.Null(guide);

            PathManager.Unload("SearchCap");
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
            PathManager.InitializeMap("LSpline");

            var request = AStarPathRequest.CreateEmpty();
            request.UseSplineSmoothing = true;

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(Vector3d.Zero, new Vector3d(2, 0, 2), request);

            Assert.NotNull(guide);
            Assert.True(guide.Path!.Count > 3); // should have inserted curve points

            PathManager.Unload("LSpline");
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
            request.UseSplineSmoothing = true;

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(start, end, request);

            Assert.NotNull(guide);
            Assert.Equal(2, guide.Path!.Count); // No smoothing applied

            PathManager.Unload("ShortSpline");
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
            request.UseSplineSmoothing = true;

            AStarGuide guide = PathGuideFactory.RequestGuide<AStarGuide>(start, end, request);

            Assert.NotNull(guide);
            Assert.Equal(start, guide.Path!.First());
            Assert.Equal(end, guide.Path.Last());

            PathManager.Unload("SplineEnds");
        }

    }
}
