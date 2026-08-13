using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class GuidedVolumeExitPlannerTests : IDisposable
{
    public GuidedVolumeExitPlannerTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryPlan_ShouldRejectUnsupportedChartModes()
    {
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, "GuidedPlannerRejectMode");

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            (SolidPathAlgorithm)123, // Invalid enum value to trigger the unsupported mode branch.
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldCreateAStarExitPlan_ForLocalSwimExit()
    {
        const string sceneKey = "GuidedPlannerAStar";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.TransitionId.Should().Be($"{sceneKey}-exit");
        plannedHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.AStar);
        totalCost.Should().BeGreaterThan(0);

        plannedHandoff.TryCreateFollowupRequest(TestWorld.Context, new Vector3d(2, 0, 0), Fixed64.One, out IPathRequest? followup).Should().BeTrue();
        followup.Should().BeOfType<AStarPathRequest>();
    }

    [Fact]
    public void TryPlan_ShouldCreateFlowFieldExitPlan_ForLocalSwimExit()
    {
        const string sceneKey = "GuidedPlannerFlow";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 8,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.FlowField);
        plannedHandoff.FlowFieldExtraFloodRange.Should().Be(8);
        totalCost.Should().BeGreaterThan(0);

        plannedHandoff.TryCreateFollowupRequest(TestWorld.Context, new Vector3d(2, 0, 0), Fixed64.One, out IPathRequest? followup).Should().BeTrue();
        followup.Should().BeOfType<FlowFieldPathRequest>();
    }

    [Fact]
    public void TryPlan_ShouldUseTransitionAwareAStarChartLeg_ForAerialLanding()
    {
        const string sceneKey = "GuidedPlannerAerialAStar";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        plannedHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        plannedHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.AStar);
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldUseTransitionAwareFlowFieldChartLeg_ForAerialLanding()
    {
        const string sceneKey = "GuidedPlannerAerialFlow";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 6,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        plannedHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        plannedHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.FlowField);
        plannedHandoff.FlowFieldExtraFloodRange.Should().Be(6);
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenAStarChartLegNeedsTransitionsButFallbackIsDisabled()
    {
        const string sceneKey = "GuidedPlannerAerialAStarDisabled";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenFlowFieldChartLegNeedsTransitionsButFallbackIsDisabled()
    {
        const string sceneKey = "GuidedPlannerAerialFlowDisabled";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 4,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Theory]
    [InlineData(SolidPathAlgorithm.AStar)]
    [InlineData(SolidPathAlgorithm.FlowField)]
    public void TryPlan_ShouldAllowZeroDisplacementChartLeg(SolidPathAlgorithm chartPathMode)
    {
        const string sceneKey = "GuidedPlannerZeroChartLeg";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            chartPathMode,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 3,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenTargetIsOutsideEveryActiveGrid()
    {
        const string sceneKey = "GuidedPlannerOutsideGrid";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(40, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenNoTransitionsCanExitVolume()
    {
        RegisterSolidTargetLine("GuidedPlannerNoTransition", new Vector3d(2, 0, 0), 3);
        GuidedPathTestScene.AddWater(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(1, 0, 0));

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryGetTransitionAwareChartCost_ShouldReturnFalse_WhenHybridRequestIsNull()
    {
        GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost((HybridPathRequest)null!, out int chartCost).Should().BeFalse();
        chartCost.Should().Be(0);
    }

    [Fact]
    public void TryGetTransitionAwareChartCost_ShouldReturnFalse_WhenRouteHasNoDirectedTransitions()
    {
        RegisterSolidTargetLine("GuidedPlannerDirectHybrid", Vector3d.Zero, 3);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        HybridPathRequest hybridRequest = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(request));
        Assert.NotNull(hybridRequest.RoutePlan);
        hybridRequest.RoutePlan.DirectedTransitions.Should().BeEmpty();

        GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost(hybridRequest, out int chartCost).Should().BeFalse();
        chartCost.Should().Be(0);
    }

    [Fact]
    public void TryGetTransitionAwareChartCost_ShouldAssignCost_WhenRouteUsesDirectedTransitions()
    {
        const string sceneKey = "GuidedPlannerTransitionAwareHelper";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(1, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        HybridPathRequest hybridRequest = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(request));
        Assert.NotNull(hybridRequest.RoutePlan);
        hybridRequest.RoutePlan.DirectedTransitions.Should().NotBeEmpty();

        GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost(hybridRequest, out int chartCost).Should().BeTrue();
        chartCost.Should().Be(hybridRequest.RoutePlan.TotalPathCost);
    }

    private static void RegisterSolidTargetLine(string chartKey, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(TestWorld.Context, chartKey, data, minBounds);
    }
}

/// <summary>
/// Standalone tests for <see cref="GuidedVolumeExitHandoff"/> failure paths that do not require
/// a live chart or transition infrastructure.
/// </summary>
[Collection("PathingCollection")]
public sealed class GuidedVolumeExitHandoffTests : IDisposable
{
    public GuidedVolumeExitHandoffTests()
    {
        if (TestWorld.IsActive)
            TestWorld.Reset();
        else
            TestWorld.Setup();

        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenHandoffIsInvalid()
    {
        // Default TransitionId is null, so IsValid == false; the early-return branch is exercised.
        var handoff = new GuidedVolumeExitHandoff();
        handoff.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenAStarCreateReturnsNull()
    {
        // Positions far outside any grid make AStarPathRequest.Create return null,
        // exercising the create-failure return in the AStar case branch.
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartPathMode = SolidPathAlgorithm.AStar,
            ChartOriginPosition = new Vector3d(1000, 0, 0),
            TargetPosition = new Vector3d(1001, 0, 0),
        };
        handoff.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenFlowFieldCreateReturnsNull()
    {
        // Same as above but for the FlowField case branch.
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartPathMode = SolidPathAlgorithm.FlowField,
            ChartOriginPosition = new Vector3d(1000, 0, 0),
            TargetPosition = new Vector3d(1001, 0, 0),
        };
        handoff.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }
}
