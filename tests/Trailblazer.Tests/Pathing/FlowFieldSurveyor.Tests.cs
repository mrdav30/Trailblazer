using Xunit;
using FixedMathSharp;
using Trailblazer.Pathing;
using GridForge.Grids;
using SwiftCollections;
using System.Linq;
using GridForge.Configuration;
using FluentAssertions;

namespace Trailblazer.Tests.Pathing
{
    [Collection("PathingCollection")]
    public class FlowFieldSurveyorTests
    {
        public FlowFieldSurveyorTests()
        {
            var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
            GlobalGridManager.TryAddGrid(config, out _);
        }

        [Fact]
        public void FlowField_ShouldFloodFromGoalOutward()
        {
            // Create a 1x5 corridor
            bool[,,] data = new bool[1, 5, 1];
            for (int y = 0; y < 5; y++)
                data[0, y, 0] = true;

            PathTestFactory.RegisterFromData("FloodTest", data, new Vector3d(0, 0, 0));

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(4, 0, 0);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Fields);
            Assert.Equal(5, result.Fields.Count);

            var sorted = result.Fields.Values.OrderBy(f => f.DistanceToTarget).ToList();
            for (int i = 1; i < sorted.Count; i++)
                Assert.True(sorted[i].DistanceToTarget > sorted[i - 1].DistanceToTarget);

            PathManager.Unload("FloodTest");
        }

        [Fact]
        public void FlowField_ShouldRespectUnitSizeBlockers()
        {
            bool[,,] data = new bool[1, 5, 3]
            {
                {
                    { true, true, true },
                    { true, true, true },
                    { false, true, false },
                    { true, true, true },
                    { true, true, true }
                }
            };

            PathTestFactory.RegisterFromData("BlockedChoke", data, Vector3d.Zero);

            var start = new Vector3d(1, 0, 0);
            var end = new Vector3d(1, 0, 4);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.UnitSize = (Fixed64)2;
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            Assert.False(result.IsValid);
            Assert.Null(result.Fields);

            PathManager.Unload("BlockedChoke");
        }

        [Fact]
        public void FlowField_ShouldRespectSearchRange()
        {
            bool[,,] data = new bool[1, 8, 8];
            for (int x = 0; x < 8; x++)
                for (int z = 0; z < 8; z++)
                    data[0, x, z] = true;

            PathTestFactory.RegisterFromData("ShortRange", data, new Vector3d(-4, 0, -4));

            var start = new Vector3d(-2, 0, 0);
            var end = new Vector3d(4, 0, 4);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.ExtraFloodRange = 5;
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            Assert.True(result.IsValid);
             
            var distanceToTarget = Vector3d.Distance(start, end).CeilToInt();
            foreach (FlowField flow in result.Fields.Values)
                Assert.True(flow.DistanceToTarget <= distanceToTarget + request.ExtraFloodRange);

            PathManager.Unload("ShortRange");
        }

        [Fact]
        public void FlowField_ShouldPointToGoal_WhenClearLine()
        {
            bool[,,] data = new bool[1, 3, 1]
            {
                {
                    { true },
                    { true },
                    { true }
                }
            };

            PathTestFactory.RegisterFromData("LineDir", data, Vector3d.Zero);

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(2, 0, 0);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
            var dir = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

            var expected = (end - start).Normalize();
            var angleDiff = Vector3d.Dot(expected, dir);

            Assert.True(angleDiff > Fixed64.Half);

            PathManager.Unload("LineDir");
        }

        [Fact]
        public void FlowField_ShouldReturnZeroDirection_AtGoal()
        {
            bool[,,] data = new bool[1, 3, 1];
            data[0, 0, 0] = true;
            data[0, 1, 0] = true;
            data[0, 2, 0] = true;

            PathTestFactory.RegisterFromData("GoalZero", data, Vector3d.Zero);

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(2, 0, 0);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            var goalField = result.Fields.Values.First(f => f.IsGoal);
            Assert.Equal(Vector3d.Zero, goalField.Direction);

            PathManager.Unload("GoalZero");
        }

        [Fact]
        public void FlowField_ShouldReturnNull_WhenUnreachable()
        {
            PathTestFactory.RegisterSingleWalkablePoint("IsolatedStart", new Vector3d(0, 0, 0));
            PathTestFactory.RegisterSingleWalkablePoint("IsolatedEnd", new Vector3d(5, 0, 5));

            PathManager.GetValidPathRequest(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5), out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            Assert.False(result.IsValid);
            Assert.Null(result.Fields);

            PathManager.Unload("IsolatedStart");
            PathManager.Unload("IsolatedEnd");
        }

        [Fact]
        public void FlowFieldGuide_ShouldReturnCorrectIndexAndDirection()
        {
            bool[,,] data = new bool[1, 3, 1];
            data[0, 0, 0] = true;
            data[0, 1, 0] = true;
            data[0, 2, 0] = true;

            PathTestFactory.RegisterFromData("GuideTest", data, Vector3d.Zero);

            var start = new Vector3d(0, 0, 0);
            var end = new Vector3d(2, 0, 0);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

            var guide = new FlowFieldGuide();
            bool initialized = guide.Initialize(result);

            Assert.True(initialized);
            Assert.True(guide.FlowFieldContainsPosition(start));

            guide.TryGetMovementDirection(start, out Vector3d dir);
            Assert.True(dir.Magnitude > Fixed64.Zero);

            PathManager.Unload("GuideTest");
        }

        [Fact]
        public void FlowField_ShouldPreferShortestStraightLineOverZigZag()
        {
            bool[,,] data = new bool[1, 3, 3]
            {
                {
                    { true, true, true },
                    { false, true, false },
                    { true, true, true }
                }
            };

            PathTestFactory.RegisterFromData("ZigZag", data, Vector3d.Zero);

            var start = new Vector3d(0, 0, 1);
            var end = new Vector3d(2, 0, 1);

            PathManager.GetValidPathRequest(start, end, out Voxel startVoxel, out Voxel endVoxel);
            var request = FlowFieldPathRequest.Create(startVoxel, endVoxel);
            request.Prepare();

            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
            var vec = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

            Assert.True(vec.x > Fixed64.Zero); // ensure direction favors straight axis
            Assert.True(vec.z.Abs() < Fixed64.Half);

            PathManager.Unload("ZigZag");
        }

        [Fact]
        public void FlowField_ShouldPointToNearestGoal_WhenMultipleGoalsExist()
        {
            bool[,,] data = new bool[1, 5, 5];

            // Make everything walkable
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    data[0, x, z] = true;

            PathTestFactory.RegisterFromData("MultiGoal", data, Vector3d.Zero);

            // Pick arbitrary start position
            Vector3d start = new(2, 0, 2);

            // Try two different end goals and compare flow field results
            var request1 = FlowFieldPathRequest.Create(start, new Vector3d(0, 0, 0)).Validate();
            var flowResult1 = FlowFieldSurveyor.Shared.FindPath(request1);
            var request2 = FlowFieldPathRequest.Create(start, new Vector3d(4, 0, 4)).Validate();
            var flowResult2 = FlowFieldSurveyor.Shared.FindPath(request2);

            // Both results should be valid and flow toward the respective goals
            flowResult1.Fields.Count.Should().BeGreaterThan(0);
            flowResult2.Fields.Count.Should().BeGreaterThan(0);

            Vector3d dir1 = FlowFieldSurveyor.SampleFlowVector(start, flowResult1.Fields);
            Vector3d dir2 = FlowFieldSurveyor.SampleFlowVector(start, flowResult2.Fields);

            dir1.Should().NotBeApproximately(dir2);

            PathManager.Unload("MultiGoal");
        }

        [Fact]
        public void FlowField_ShouldPointDiagonally_WhenGoalIsDiagonal()
        {
            bool[,,] data = new bool[1, 5, 5];

            // Make everything walkable
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    data[0, x, z] = true;

            PathTestFactory.RegisterFromData("DiagonalGoal", data, Vector3d.Zero);

            Vector3d start = new(2, 0, 2);
            Vector3d goal = new(4, 0, 4);

            var request = FlowFieldPathRequest.Create(start, goal).Validate();

            var result = FlowFieldSurveyor.Shared.FindPath(request);

            Vector3d flow = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

            // Expect a roughly diagonal vector toward the goal
            flow.x.Should().BeGreaterThan(Fixed64.Zero);
            flow.z.Should().BeGreaterThan(Fixed64.Zero);

            PathManager.Unload("DiagonalGoal");
        }

        [Fact]
        public void FlowField_ShouldReturnNull_WhenUnitSizeExceedsGrid()
        {
            bool[,,] data = new bool[1, 3, 3];

            // Make everything walkable
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    data[0, x, z] = true;

            PathTestFactory.RegisterFromData("UnitSizeTooBig", data, Vector3d.Zero);

            Vector3d start = new Vector3d(1, 0, 1);
            Vector3d goal = new Vector3d(2, 0, 2);

            var request = FlowFieldPathRequest.Create(start, goal).Validate();
            request.UnitSize = (Fixed64)10; // UnitSize larger than grid

            var result = FlowFieldSurveyor.Shared.FindPath(request);

            result.Fields.Should().BeNull();

            PathManager.Unload("UnitSizeTooBig");
        }

        [Fact]
        public void FlowField_ShouldReturnZeroDirection_WhenSamplingOutsideField()
        {
            bool[,,] data = new bool[1, 5, 5];

            // Make everything walkable
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    data[0, x, z] = true;

            PathTestFactory.RegisterFromData("OutsideField", data, Vector3d.Zero);

            Vector3d start = new(2, 0, 2);
            Vector3d goal = new(4, 0, 4);

            var result = FlowFieldSurveyor.Shared.FindPath(FlowFieldPathRequest.Create(start, goal).Validate());

            // Sample outside of known field bounds
            Vector3d outsidePos = new(10, 0, 10);

            Vector3d flow = FlowFieldSurveyor.SampleFlowVector(outsidePos, result.Fields);

            flow.Should().Be(Vector3d.Zero);

            PathManager.Unload("OutsideField");
        }

        [Fact]
        public void FlowField_Direction_ShouldAlwaysPointTowardLowerCost()
        {
            bool[,,] data = new bool[1, 5, 5];

            // Create fully walkable 5x5 grid
            for (int z = 0; z < 5; z++)
                for (int x = 0; x < 5; x++)
                    data[0, z, x] = true;

            PathTestFactory.RegisterFromData("FlowGradientTest", data, Vector3d.Zero);

            var start = new Vector3d(2, 0, 2);
            var end = new Vector3d(0, 0, 0);

            var request = FlowFieldPathRequest.Create(start, end);
            request.Prepare();
            var result = FlowFieldSurveyor.Shared.FindPath(request);

            foreach (var pair in result.Fields)
            {
                int index = pair.Key;
                FlowField field = pair.Value;

                if (field.IsGoal || field.Direction == Vector3d.Zero)
                    continue;

                // Try get neighbor index

                if (!GlobalGridManager.TryGetGridAndVoxel(field.GlobalIndex, out _, out Voxel current))
                    continue;

                Vector3d neighborPosition = current.WorldPosition + field.Direction;

                if (!GlobalGridManager.TryGetGridAndVoxel(neighborPosition, out _, out Voxel neighbor))
                    continue;

                if (!result.Fields.TryGetValue(neighbor.SpawnToken, out FlowField neighborField))
                    continue;

                neighborField.DistanceToTarget.Should()
                    .BeLessThan(field.DistanceToTarget, $"Direction from index {index} should lead downhill");
            }

            PathManager.Unload("FlowGradientTest");
        }
    }
}
