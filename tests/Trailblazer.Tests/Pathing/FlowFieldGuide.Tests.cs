using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class FlowFieldGuideTests : IDisposable
{
    public FlowFieldGuideTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(12, 12, 12)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FlowFieldGuide_ShouldHandleNonStagedQueries()
    {
        RegisterLineChart("FlowFieldGuideLine", Vector3d.Zero, 3);

        var guide = new FlowFieldGuide();
        guide.Initialize(FlowFieldSurveyResult.Empty).Should().BeFalse();
        guide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.FlowFieldContainsPosition(Vector3d.Zero).Should().BeFalse();
        guide.TryGetFallbackDirection(Vector3d.Zero, out _).Should().BeFalse();

        FlowFieldSurveyResult survey = CreateSurveyResult(
            (Vector3d.Zero, new Vector3d(1, 0, 0), 2, false),
            (new Vector3d(1, 0, 0), new Vector3d(1, 0, 0), 1, false),
            (new Vector3d(2, 0, 0), Vector3d.Zero, 0, true));

        guide.Initialize(survey).Should().BeTrue();
        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d movement).Should().BeTrue();
        movement.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.FlowFieldContainsPosition(Vector3d.Zero).Should().BeTrue();
        guide.FlowFieldContainsPosition(new Vector3d(5, 0, 0)).Should().BeFalse();

        guide.TryGetFallbackDirection(new Vector3d(0.25, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.Should().NotBe(Vector3d.Zero);
        guide.TryGetFallbackDirection(new Vector3d(5, 0, 0), out _).Should().BeFalse();
    }

    [Fact]
    public void FlowFieldGuide_ShouldAdvanceWaypointStages_AndClearReleasedState()
    {
        var guide = new FlowFieldGuide();

        guide.InitializeStaged(null!).Should().BeFalse();
        guide.InitializeStaged(new HybridRoutePlan(Array.Empty<HybridRouteStep>(), Array.Empty<TraversalTransition>(), 0))
            .Should()
            .BeFalse();

        var plan = new HybridRoutePlan(
            new[]
            {
                HybridRouteStep.Waypoint(new Vector3d(1, 0, 0)),
                HybridRouteStep.Waypoint(new Vector3d(2, 0, 0))
            },
            Array.Empty<TraversalTransition>(),
            0);

        guide.InitializeStaged(plan).Should().BeTrue();
        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d firstDirection).Should().BeTrue();
        firstDirection.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetFallbackDirection(Vector3d.Zero, out Vector3d fallback).Should().BeTrue();
        fallback.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d secondDirection).Should().BeTrue();
        secondDirection.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 0), out _).Should().BeFalse();

        guide.ReleaseStagedResources(dispose: false);
        guide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
    }

    [Fact]
    public void FlowFieldGuide_ShouldBoundCompletedWaypointStageAdvances()
    {
        HybridRouteStep[] steps = new HybridRouteStep[64];
        for (int i = 0; i < steps.Length; i++)
            steps[i] = HybridRouteStep.Waypoint(Vector3d.Zero);

        var plan = new HybridRoutePlan(steps, Array.Empty<TraversalTransition>(), 0);
        var guide = new FlowFieldGuide();

        guide.InitializeStaged(plan).Should().BeTrue();
        guide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.TryGetFallbackDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.ReleaseStagedResources(dispose: false);
    }

    [Fact]
    public void FlowFieldGuide_ShouldUseFlowFieldSubGuide_ForStagedSegments()
    {
        RegisterLineChart("FlowFieldStageLine", Vector3d.Zero, 3);

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        request.Should().NotBeNull();

        var plan = new HybridRoutePlan(
            new[] { HybridRouteStep.Segment(request) },
            Array.Empty<TraversalTransition>(),
            0);

        var guide = new FlowFieldGuide();
        guide.InitializeStaged(plan).Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d direction).Should().BeTrue();
        direction.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.FlowFieldContainsPosition(Vector3d.Zero).Should().BeTrue();
        guide.TryGetFallbackDirection(new Vector3d(0.25, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.Should().NotBe(Vector3d.Zero);

        guide.ReleaseStagedResources(dispose: true);
        guide.FlowFieldContainsPosition(Vector3d.Zero).Should().BeFalse();
    }

    private static FlowFieldSurveyResult CreateSurveyResult(params (Vector3d position, Vector3d direction, int cost, bool isGoal)[] cells)
    {
        var fields = new SwiftDictionary<GlobalVoxelIndex, FlowField>(cells.Length);

        for (int i = 0; i < cells.Length; i++)
        {
            GlobalGridManager.TryGetVoxel(cells[i].position, out Voxel voxel).Should().BeTrue();
            fields.Add(voxel.GlobalIndex, new FlowField
            {
                GlobalIndex = voxel.GlobalIndex,
                Direction = cells[i].direction,
                PathCost = cells[i].cost,
                IsGoal = cells[i].isGoal
            });
        }

        return FlowFieldSurveyResult.Create(fields, Array.Empty<string>(), 1);
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }
}
