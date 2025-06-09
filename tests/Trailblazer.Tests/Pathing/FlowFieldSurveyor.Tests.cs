using Xunit;
using FixedMathSharp;
using Trailblazer.Pathing;
using GridForge.Grids;
using SwiftCollections;
using System.Linq;
using GridForge.Configuration;

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

            bool success = FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields);

            Assert.True(success);
            Assert.NotNull(fields);
            Assert.Equal(5, fields.Count);

            var sorted = fields.Values.OrderBy(f => f.DistanceToTarget).ToList();
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

            bool success = FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields);
            Assert.False(success);
            Assert.Null(fields);

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

            bool success = FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields, out int distanceToTarget);

            Assert.True(success);
            foreach (FlowField flow in fields.Values)
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

            FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields);
            var dir = FlowFieldSurveyor.SampleFlowVector(start, fields);

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

            FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields);

            var goalField = fields.Values.First(f => f.IsGoal);
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

            bool success = FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> result);

            Assert.False(success);
            Assert.Null(result);

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

            var guide = new FlowFieldGuide();
            bool initialized = guide.Initialize(request);

            Assert.True(initialized);
            int index = guide.GetIndex(start);
            Assert.NotEqual(-1, index);

            Vector3d dir = guide.GetMovementDirection(start, index);
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

            FlowFieldSurveyor.Shared.FindPath(request, out SwiftDictionary<int, FlowField> fields);
            var vec = FlowFieldSurveyor.SampleFlowVector(start, fields);

            Assert.True(vec.x > Fixed64.Zero); // ensure direction favors straight axis
            Assert.True(vec.z.Abs() < Fixed64.Half);

            PathManager.Unload("ZigZag");
        }
    }
}
