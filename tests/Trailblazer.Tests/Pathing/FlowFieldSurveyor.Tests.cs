using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using System.Linq;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class FlowFieldSurveyorTests : IDisposable
{
    public FlowFieldSurveyorTests()
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
        PathManager.UnloadAllCharts();
        PathManager.ClearAll();

        GlobalGridManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
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

        var request = FlowFieldPathRequest.Create(start, end);

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        Assert.True(result.HasPath);
        Assert.NotNull(result.Fields);
        Assert.Equal(5, result.Fields.Count);

        var sorted = result.Fields.Values.OrderBy(f => f.PathCost).ToList();
        for (int i = 1; i < sorted.Count; i++)
            Assert.True(sorted[i].PathCost > sorted[i - 1].PathCost);

        PathManager.UnloadChart("FloodTest");
    }

    [Fact]
    public void FlowField_ShouldRespectUnitSizeBlockers()
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

        PathTestFactory.RegisterFromData("BlockedChoke", data, Vector3d.Zero);

        var start = new Vector3d(1, 0, 0);
        var end = new Vector3d(5, 0, 0);

        var request = FlowFieldPathRequest.Create(start, end, Fixed64.Two);

        request.IsValid.Should().BeTrue();

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        Assert.False(result.HasPath);
        Assert.Null(result.Fields);

        PathManager.UnloadChart("BlockedChoke");
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
        var end = new Vector3d(3, 0, 3);

        var request = FlowFieldPathRequest.Create(start, end);
        request.ExtraFloodRange = 5;

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        Assert.True(result.HasPath);

        var distanceToTarget = Vector3d.Distance(end, start).CeilToInt() + 2 + request.ExtraFloodRange;
        foreach (FlowField flow in result.Fields.Values)
            Assert.True(flow.PathCost <= distanceToTarget);

        PathManager.UnloadChart("ShortRange");
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

        var request = FlowFieldPathRequest.Create(start, end);

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
        var dir = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

        var expected = (end - start).Normalize();
        var angleDiff = Vector3d.Dot(expected, dir);

        Assert.True(angleDiff > Fixed64.Half);

        PathManager.UnloadChart("LineDir");
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

        var request = FlowFieldPathRequest.Create(start, end);

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        var goalField = result.Fields.Values.First(f => f.IsGoal);
        Assert.Equal(Vector3d.Zero, goalField.Direction);

        PathManager.UnloadChart("GoalZero");
    }

    [Fact]
    public void FlowField_ShouldReturnNull_WhenUnreachable()
    {
        PathTestFactory.RegisterSingleWalkablePoint("IsolatedStart", new Vector3d(0, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("IsolatedEnd", new Vector3d(5, 0, 5));

        var request = FlowFieldPathRequest.Create(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5));

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        Assert.False(result.HasPath);
        Assert.Null(result.Fields);

        PathManager.UnloadChart("IsolatedStart");
        PathManager.UnloadChart("IsolatedEnd");
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

        var request = FlowFieldPathRequest.Create(start, end);

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        var guide = new FlowFieldGuide();
        bool initialized = guide.Initialize(result);

        Assert.True(initialized);
        Assert.True(guide.FlowFieldContainsPosition(start));

        guide.TryGetMovementDirection(start, out Vector3d dir);
        Assert.True(dir.Magnitude > Fixed64.Zero);

        PathManager.UnloadChart("GuideTest");
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

        var request = FlowFieldPathRequest.Create(start, end);

        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
        var vec = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

        Assert.True(vec.x > Fixed64.Zero); // ensure direction favors straight axis
        Assert.True(vec.z.Abs() < Fixed64.Half);

        PathManager.UnloadChart("ZigZag");
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
        var request1 = FlowFieldPathRequest.Create(start, new Vector3d(0, 0, 0));
        var flowResult1 = FlowFieldSurveyor.Shared.FindPath(request1);
        var request2 = FlowFieldPathRequest.Create(start, new Vector3d(4, 0, 4));
        var flowResult2 = FlowFieldSurveyor.Shared.FindPath(request2);

        // Both results should be valid and flow toward the respective goals
        flowResult1.Fields.Count.Should().BeGreaterThan(0);
        flowResult2.Fields.Count.Should().BeGreaterThan(0);

        Vector3d dir1 = FlowFieldSurveyor.SampleFlowVector(start, flowResult1.Fields);
        Vector3d dir2 = FlowFieldSurveyor.SampleFlowVector(start, flowResult2.Fields);

        dir1.Should().NotBeApproximately(dir2);

        PathManager.UnloadChart("MultiGoal");
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

        var request = FlowFieldPathRequest.Create(start, goal);

        var result = FlowFieldSurveyor.Shared.FindPath(request);

        Vector3d flow = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

        // Expect a roughly diagonal vector toward the goal
        flow.x.Should().BeGreaterThan(Fixed64.Zero);
        flow.z.Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("DiagonalGoal");
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

        Vector3d start = new(1, 0, 1);
        Vector3d goal = new(2, 0, 2);

        // UnitSize larger than grid
        var request = FlowFieldPathRequest.Create(start, goal, (Fixed64)10);

        request.IsValid.Should().BeFalse();

        var result = FlowFieldSurveyor.Shared.FindPath(request);

        result.Fields.Should().BeNull();

        PathManager.UnloadChart("UnitSizeTooBig");
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

        var result = FlowFieldSurveyor.Shared.FindPath(FlowFieldPathRequest.Create(start, goal));

        // Sample outside of known field bounds
        Vector3d outsidePos = new(10, 0, 10);

        Vector3d flow = FlowFieldSurveyor.SampleFlowVector(outsidePos, result.Fields);

        flow.Should().Be(Vector3d.Zero);

        PathManager.UnloadChart("OutsideField");
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

            neighborField.PathCost.Should()
                .BeLessThan(field.PathCost, $"Direction from index {index} should lead downhill");
        }

        PathManager.UnloadChart("FlowGradientTest");
    }

    [Fact]
    public void FlowField_ShouldRespect_DirectionalResolutionBias()
    {
        bool[,,] data = new bool[1, 3, 3];
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("DirectionalBias", data, Vector3d.Zero);

        Vector3d start = new(1, 0, 1);
        Vector3d goal = new(2, 0, 2);

        var request = FlowFieldPathRequest.Create(start, goal);
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        Vector3d flow = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

        flow.x.Should().BeApproximately(flow.z, (Fixed64)0.05);
        flow.x.Should().BeGreaterThan(Fixed64.Zero);
        flow.z.Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("DirectionalBias");
    }

    [Fact]
    public void FlowField_ShouldPrefer_CardinalOverDiagonal_WhenEqualCost()
    {
        bool[,,] data = new bool[1, 3, 3];
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("CardinalOverDiagonal", data, Vector3d.Zero);

        Vector3d start = new(1, 0, 1);
        Vector3d goal = new(1, 0, 0);

        var request = FlowFieldPathRequest.Create(start, goal);
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        Vector3d flow = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);
        flow.Should().Be(Vector3d.Backward);

        PathManager.UnloadChart("CardinalOverDiagonal");
    }

    [Fact]
    public void FlowField_ShouldProduceDifferentDirections_ForDifferentGoals()
    {
        bool[,,] data = new bool[1, 5, 5];
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("DifferentGoals", data, Vector3d.Zero);

        Vector3d start = new(2, 0, 2);
        var request1 = FlowFieldPathRequest.Create(start, new Vector3d(0, 0, 0));
        var request2 = FlowFieldPathRequest.Create(start, new Vector3d(4, 0, 4));

        var result1 = FlowFieldSurveyor.Shared.FindPath(request1);
        var result2 = FlowFieldSurveyor.Shared.FindPath(request2);

        FlowField field1 = FlowFieldSurveyor.GetFlowField(start, result1.Fields);
        FlowField field2 = FlowFieldSurveyor.GetFlowField(start, result2.Fields);

        field1.Direction.Should().NotBe(Vector3d.Zero);
        field2.Direction.Should().NotBe(Vector3d.Zero);
        field1.Direction.Should().NotBe(field2.Direction);

        PathManager.UnloadChart("DifferentGoals");
    }

    [Fact]
    public void FlowField_ShouldBeDeterministic_WithSameGoal()
    {
        bool[,,] data = new bool[1, 5, 5];
        for (int z = 0; z < 5; z++)
            for (int x = 0; x < 5; x++)
                data[0, z, x] = true;

        PathTestFactory.RegisterFromData("DeterminismTest", data, Vector3d.Zero);

        var start = new Vector3d(4, 0, 4);
        var end = new Vector3d(0, 0, 0);

        var request1 = FlowFieldPathRequest.Create(start, end);
        var result1 = FlowFieldSurveyor.Shared.FindPath(request1);

        var request2 = FlowFieldPathRequest.Create(start, end);
        var result2 = FlowFieldSurveyor.Shared.FindPath(request2);

        result1.Fields.Count.Should().Be(result2.Fields.Count);

        foreach (var kvp in result1.Fields)
        {
            result2.Fields.Should().ContainKey(kvp.Key);
            FlowField field1 = kvp.Value;
            FlowField field2 = result2.Fields[kvp.Key];

            field1.Direction.Should().Be(field2.Direction);
            field1.PathCost.Should().Be(field2.PathCost);
        }

        PathManager.UnloadChart("DeterminismTest");
    }

    [Fact]
    public void FlowField_ShouldReroute_WhenAdjacentBlockersExist()
    {
        bool[,,] data = new bool[1, 3, 3];
        data[0, 0, 0] = true;
        data[0, 0, 2] = true;
        data[0, 2, 0] = true;
        data[0, 2, 2] = true;
        data[0, 1, 0] = false;
        data[0, 1, 1] = false;
        data[0, 1, 2] = false;

        PathTestFactory.RegisterFromData("RerouteAroundBlockers", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 2);
        var goal = new Vector3d(2, 0, 0);
        var request = FlowFieldPathRequest.Create(start, goal);
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        Vector3d flow = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);
        flow.Should().NotBe(Vector3d.Left);

        PathManager.UnloadChart("RerouteAroundBlockers");
    }

    [Fact]
    public void FlowField_ShouldHandle_UnreachableGoal_AtGridEdge()
    {
        bool[,,] data = new bool[1, 4, 4];
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("EdgeGoalTest", data, Vector3d.Zero);

        var start = new Vector3d(2, 0, 2);
        var goal = new Vector3d(5, 0, 5);
        var request = FlowFieldPathRequest.Create(start, goal);
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeFalse();

        PathManager.UnloadChart("EdgeGoalTest");
    }

    [Fact]
    public void FlowField_ShouldProduce_ConsistentResults()
    {
        bool[,,] data = new bool[1, 5, 5];
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("ConsistencyTest", data, Vector3d.Zero);

        var start = new Vector3d(4, 0, 4);
        var end = new Vector3d(0, 0, 0);

        var request1 = FlowFieldPathRequest.Create(start, end);
        var result1 = FlowFieldSurveyor.Shared.FindPath(request1);

        var request2 = FlowFieldPathRequest.Create(start, end);
        var result2 = FlowFieldSurveyor.Shared.FindPath(request2);

        result1.Fields.Count.Should().Be(result2.Fields.Count);

        foreach (var kv in result1.Fields)
        {
            result2.Fields.TryGetValue(kv.Key, out var f2).Should().BeTrue();
            f2.Direction.Should().Be(kv.Value.Direction);
            f2.PathCost.Should().Be(kv.Value.PathCost);
        }

        PathManager.UnloadChart("ConsistencyTest");
    }

    [Fact]
    public void FlowField_ShouldAvoidHighCostModifierPartition()
    {
        bool[,,] data = new bool[1, 3, 3];

        // Fully walkable 3x3 grid
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData("HighCostModifier", data, Vector3d.Zero);

        Vector3d start = new(0, 0, 1);  // Left-middle
        Vector3d goal = new(2, 0, 1);   // Right-middle

        // Mark the center partition with a high cost modifier
        GlobalGridManager.TryGetGridAndVoxel(new Vector3d(1, 0, 1), out _, out Voxel center);
        if (center.TryGetPartition(out PathPartition partition))
            partition.PathCostModifier = 10; // Arbitrary high cost to penalize direct path

        var request = FlowFieldPathRequest.Create(start, goal);
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        Vector3d dir = FlowFieldSurveyor.SampleFlowVector(start, result.Fields);

        // Expect diagonal movement to avoid center
        dir.x.Should().NotBe(Fixed64.One, "the center partition is penalized");
        dir.z.Abs().Should().BeGreaterThan(Fixed64.Zero, "path should detour around the penalty");

        // Cleanup
        PathManager.UnloadChart("HighCostModifier");
    }

    [Fact]
    public void FlowField_ShouldNotIncludeDiagonal_WhenLegsAreBlocked()
    {
        bool[,,] data = new bool[1, 3, 3];

        // All walkable except the two "leg" cells
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                data[0, x, z] = true;

        data[0, 1, 0] = false; // West leg
        data[0, 0, 1] = false; // South leg

        PathTestFactory.RegisterFromData("BlockedDiagonal", data, Vector3d.Zero);

        Vector3d start = new(0, 0, 0);
        Vector3d goal = new(2, 0, 2);

        var request = FlowFieldPathRequest.Create(start, goal);
        request.Validate();

        var result = FlowFieldSurveyor.Shared.FindPath(request);

        result.IsValid.Should().BeFalse();
        result.Fields.Should().BeNull();

        PathManager.UnloadChart("BlockedDiagonal");
    }

    [Fact]
    public void FlowField_ShouldFlow_Upward_WhenTargetIsAbove()
    {
        bool[,,] data = new bool[3, 1, 1];  // y, x, z

        // Vertical stack of 3 walkable voxels
        for (int y = 0; y < 3; y++)
            data[y, 0, 0] = true;

        PathTestFactory.RegisterFromData("VerticalAscend", data, Vector3d.Zero);

        var request = FlowFieldPathRequest.Create(new Vector3d(0, 0, 0), new Vector3d(0, 2, 0));
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        var dir = FlowFieldSurveyor.SampleFlowVector(new Vector3d(0, 0, 0), result.Fields);
        dir.Should().Be(Vector3d.Up, "flow should direct agent upward");

        PathManager.UnloadChart("VerticalAscend");
    }

    [Fact]
    public void FlowField_ShouldFlow_Downward_WhenTargetIsBelow()
    {
        bool[,,] data = new bool[3, 1, 1];  // y, x, z

        for (int y = 0; y < 3; y++)
            data[y, 0, 0] = true;

        PathTestFactory.RegisterFromData("VerticalDescend", data, Vector3d.Zero);

        var request = FlowFieldPathRequest.Create(new Vector3d(0, 2, 0), new Vector3d(0, 0, 0));
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        var dir = FlowFieldSurveyor.SampleFlowVector(new Vector3d(0, 2, 0), result.Fields);
        dir.Should().Be(Vector3d.Down, "flow should direct agent downward");

        PathManager.UnloadChart("VerticalDescend");
    }

    [Fact]
    public void FlowField_ShouldHandle_ZigZagStairs()
    {
        bool[,,] data = new bool[3, 3, 3];  // y, x, z

        // Create a stair-like path:
        // (0,0,0) → (1,0,0) → (1,1,0) → (2,1,0)
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;
        data[1, 1, 0] = true;
        data[2, 1, 0] = true;

        PathTestFactory.RegisterFromData("ZigZagStairs", data, Vector3d.Zero);

        var request = FlowFieldPathRequest.Create(new Vector3d(0, 0, 0), new Vector3d(1, 2, 0));
        var result = FlowFieldSurveyor.Shared.FindPath(request);

        var dir0 = FlowFieldSurveyor.SampleFlowVector(new Vector3d(0, 0, 0), result.Fields);
        dir0.Should().Be(new Vector3d(1, 0, 0), "first step should be right");

        var dir1 = FlowFieldSurveyor.SampleFlowVector(new Vector3d(1, 0, 0), result.Fields);
        dir1.Should().Be(new Vector3d(0, 1, 0), "second step should go up");

        var dir2 = FlowFieldSurveyor.SampleFlowVector(new Vector3d(1, 1, 0), result.Fields);
        dir2.Should().Be(Vector3d.Up, "final step should go up");

        PathManager.UnloadChart("ZigZagStairs");
    }
}
