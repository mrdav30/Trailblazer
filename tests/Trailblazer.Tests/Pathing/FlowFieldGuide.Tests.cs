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
        TrailblazerWorldManager.Setup();
        TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(12, 12, 12)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
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

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

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

    /// <summary>
    /// Covers the <c>direction == Vector3d.Zero</c> branch in TryGetMovementDirection for non-staged guides.
    /// At the goal voxel the stored flow vector is Zero, so sampling it returns false.
    /// </summary>
    [Fact]
    public void FlowFieldGuide_TryGetMovementDirection_ShouldReturnFalse_WhenFlowVectorAtPositionIsZero()
    {
        RegisterLineChart("FlowFieldGuideZeroDir", Vector3d.Zero, 3);

        FlowFieldSurveyResult survey = CreateSurveyResult(
            (Vector3d.Zero, new Vector3d(1, 0, 0), 2, false),
            (new Vector3d(1, 0, 0), new Vector3d(1, 0, 0), 1, false),
            (new Vector3d(2, 0, 0), Vector3d.Zero, 0, true));

        var guide = new FlowFieldGuide();
        guide.Initialize(survey).Should().BeTrue();

        // Position (2,0,0) is the goal — its stored direction is Zero, so TryGetMovementDirection returns false
        guide.TryGetMovementDirection(new Vector3d(2, 0, 0), out Vector3d dir).Should().BeFalse();
        dir.Should().Be(Vector3d.Zero);
    }

    /// <summary>
    /// Covers the staged FlowFieldContainsPosition path where the active stage guide is not a FlowFieldGuide.
    /// When the active stage is backed by an AStarGuide, the cast to FlowFieldGuide fails and the method
    /// short-circuits to false without delegating to the inner guide.
    /// </summary>
    [Fact]
    public void FlowFieldGuide_StagedWithAStarSegment_FlowFieldContainsPosition_ShouldReturnFalse()
    {
        RegisterLineChart("FlowFieldGuideStagedAStar", Vector3d.Zero, 3);

        var aStarRequest = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        Assert.NotNull(aStarRequest);

        var plan = new HybridRoutePlan(
            new[] { HybridRouteStep.Segment(aStarRequest) },
            Array.Empty<TraversalTransition>(),
            0);

        var guide = new FlowFieldGuide();
        guide.InitializeStaged(plan).Should().BeTrue();

        // Prime the active stage guide by requesting a movement direction first
        guide.TryGetMovementDirection(Vector3d.Zero, out _);

        // Active stage guide is AStarGuide, not FlowFieldGuide — returns false
        guide.FlowFieldContainsPosition(Vector3d.Zero).Should().BeFalse();
        guide.ReleaseStagedResources(dispose: true);
    }

    [Fact]
    public void FlowFieldGuide_ShouldFailGracefully_WhenStagedSegmentCannotCreateAnInnerGuide()
    {
        RegisterLineChart("FlowFieldGuideUnsupportedSegment", Vector3d.Zero, 2);

        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        var plan = new HybridRoutePlan(
            new[] { HybridRouteStep.Segment(new UnsupportedRequest(start, end)) },
            Array.Empty<TraversalTransition>(),
            0);

        var guide = new FlowFieldGuide();
        guide.InitializeStaged(plan).Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.TryGetFallbackDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.ReleaseStagedResources(dispose: true);
    }

    [Fact]
    public void FlowFieldGuide_ShouldReturnFalse_WhenStagedPlanContainsNullSteps()
    {
        var guide = new FlowFieldGuide();
        var plan = new HybridRoutePlan(
            new HybridRouteStep[] { null! },
            Array.Empty<TraversalTransition>(),
            0);

        guide.InitializeStaged(plan).Should().BeTrue();
        guide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        guide.TryGetFallbackDirection(Vector3d.Zero, out _).Should().BeFalse();
    }

    [Fact]
    public void FlowFieldGuide_PrivateStageHelpers_ShouldBoundAdvanceWithoutBudget_AndRejectWaypointStageGuides()
    {
        var guide = new FlowFieldGuide();
        var waypoint = HybridRouteStep.Waypoint(Vector3d.Zero);
        var plan = new HybridRoutePlan(new[] { waypoint }, Array.Empty<TraversalTransition>(), 0);

        guide.InitializeStaged(plan).Should().BeTrue();

        object[] advanceArgs = { 0 };
        ReflectionUtility.InvokePrivate<bool>(guide, "TryAdvanceStage", advanceArgs).Should().BeFalse();
        advanceArgs[0].Should().Be(0);

        object[] guideArgs = { waypoint, null! };
        ReflectionUtility.InvokePrivate<bool>(guide, "TryGetOrCreateActiveStageGuide", guideArgs).Should().BeFalse();
        guideArgs[1].Should().BeNull();
    }

    [Fact]
    public void FlowFieldGuide_PrivateSegmentHelpers_ShouldReuseStageGuide_AndAdvanceCompletedSegment()
    {
        RegisterLineChart("FlowFieldGuidePrivateSegment", Vector3d.Zero, 3);

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        var step = HybridRouteStep.Segment(request);
        var guide = new FlowFieldGuide();
        guide.InitializeStaged(new HybridRoutePlan(new[] { step }, Array.Empty<TraversalTransition>(), 0))
            .Should()
            .BeTrue();

        object[] firstGuideArgs = { step, null! };
        ReflectionUtility.InvokePrivate<bool>(guide, "TryGetOrCreateActiveStageGuide", firstGuideArgs).Should().BeTrue();
        IGuide cachedGuide = (IGuide)firstGuideArgs[1];

        object[] secondGuideArgs = { step, null! };
        ReflectionUtility.InvokePrivate<bool>(guide, "TryGetOrCreateActiveStageGuide", secondGuideArgs).Should().BeTrue();
        secondGuideArgs[1].Should().BeSameAs(cachedGuide);

        object[] segmentArgs = { new Vector3d(2, 0, 0), step, 1, null! };
        ReflectionUtility.InvokePrivate<bool>(guide, "TryGetSegmentStageMovementDirection", segmentArgs).Should().BeTrue();
        segmentArgs[2].Should().Be(0);
        segmentArgs[3].Should().Be(Vector3d.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 0), out _).Should().BeFalse();

        PathManager.UnloadChart("FlowFieldGuidePrivateSegment");
    }

    private static FlowFieldSurveyResult CreateSurveyResult(params (Vector3d position, Vector3d direction, int cost, bool isGoal)[] cells)
    {
        var fields = new SwiftDictionary<WorldVoxelIndex, FlowField>(cells.Length);

        for (int i = 0; i < cells.Length; i++)
        {
            Voxel voxel = TestRequire.VoxelAt(cells[i].position);
            fields.Add(voxel.WorldIndex, new FlowField
            {
                GlobalIndex = voxel.WorldIndex,
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

    private sealed class UnsupportedRequest : IPathRequest
    {
        public UnsupportedRequest(Voxel start, Voxel end)
        {
            Origin = start.WorldPosition;
            StartNode = start;
            TargetPosition = end.WorldPosition;
            EndNode = end;
        }

        public TrailblazerWorldContext Context => TrailblazerManager.DefaultContext;

        public Vector3d Origin { get; }

        public Voxel StartNode { get; }

        public Vector3d TargetPosition { get; }

        public Voxel EndNode { get; }

        public Fixed64 UnitSize => Fixed64.One;

        public bool HasZeroDisplacement => StartNode == EndNode;

        public bool AllowUnwalkableEndpoints => false;

        public int MaxPathSearchRange { get; set; } = 1;

        public bool HasOrigin => true;

        public bool HasDestination => true;

        public bool HasValidEndpoints => true;

        public bool IsValid => true;

        public int RequestCacheKey => 314;

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }
}
