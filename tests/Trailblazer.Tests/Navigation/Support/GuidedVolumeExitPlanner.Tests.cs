using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class GuidedVolumeExitPlannerTests : IDisposable
{
    public GuidedVolumeExitPlannerTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryPlan_ShouldRejectUnsupportedChartModes()
    {
        GuidedPathTestScene.RegisterVolumeExitHandoffScene("GuidedPlannerRejectMode");

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            GuidedPathMode.Swim,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldCreateAStarExitPlan_ForLocalSwimExit()
    {
        const string sceneKey = "GuidedPlannerAStar";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeTrue();

        request.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().NotBeNull();
        handoff.TransitionId.Should().Be($"{sceneKey}-exit");
        handoff.ChartPathMode.Should().Be(GuidedPathMode.AStar);
        totalCost.Should().BeGreaterThan(0);

        handoff.TryCreateFollowupRequest(new Vector3d(2, 0, 0), Fixed64.One, out IPathRequest followup).Should().BeTrue();
        followup.Should().BeOfType<AStarPathRequest>();
    }

    [Fact]
    public void TryPlan_ShouldCreateFlowFieldExitPlan_ForLocalSwimExit()
    {
        const string sceneKey = "GuidedPlannerFlow";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 8,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeTrue();

        request.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().NotBeNull();
        handoff.ChartPathMode.Should().Be(GuidedPathMode.FlowField);
        handoff.FlowFieldExtraFloodRange.Should().Be(8);
        totalCost.Should().BeGreaterThan(0);

        handoff.TryCreateFollowupRequest(new Vector3d(2, 0, 0), Fixed64.One, out IPathRequest followup).Should().BeTrue();
        followup.Should().BeOfType<FlowFieldPathRequest>();
    }

    [Fact]
    public void TryPlan_ShouldUseTransitionAwareAStarChartLeg_ForAerialLanding()
    {
        const string sceneKey = "GuidedPlannerAerialAStar";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeTrue();

        request.Should().NotBeNull();
        request.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        handoff.Should().NotBeNull();
        handoff.TransitionId.Should().Be($"{sceneKey}-landing");
        handoff.ChartPathMode.Should().Be(GuidedPathMode.AStar);
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldUseTransitionAwareFlowFieldChartLeg_ForAerialLanding()
    {
        const string sceneKey = "GuidedPlannerAerialFlow";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 6,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeTrue();

        request.Should().NotBeNull();
        request.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        handoff.Should().NotBeNull();
        handoff.TransitionId.Should().Be($"{sceneKey}-landing");
        handoff.ChartPathMode.Should().Be(GuidedPathMode.FlowField);
        handoff.FlowFieldExtraFloodRange.Should().Be(6);
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenAStarChartLegNeedsTransitionsButFallbackIsDisabled()
    {
        const string sceneKey = "GuidedPlannerAerialAStarDisabled";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenFlowFieldChartLegNeedsTransitionsButFallbackIsDisabled()
    {
        const string sceneKey = "GuidedPlannerAerialFlowDisabled";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 4,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Theory]
    [InlineData(GuidedPathMode.AStar)]
    [InlineData(GuidedPathMode.FlowField)]
    public void TryPlan_ShouldAllowZeroDisplacementChartLeg(GuidedPathMode chartPathMode)
    {
        const string sceneKey = "GuidedPlannerZeroChartLeg";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            chartPathMode,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 3,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeTrue();

        request.Should().NotBeNull();
        request.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().NotBeNull();
        handoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenTargetIsOutsideEveryActiveGrid()
    {
        const string sceneKey = "GuidedPlannerOutsideGrid";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(40, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenNoTransitionsCanExitVolume()
    {
        RegisterSolidTargetLine("GuidedPlannerNoTransition", new Vector3d(2, 0, 0), 3);
        GuidedPathTestScene.AddWater(Vector3d.Zero);
        GuidedPathTestScene.AddWater(new Vector3d(1, 0, 0));

        GuidedVolumeExitPlanner.TryPlan(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest request,
            out GuidedVolumeExitHandoff handoff,
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

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true);

        request.Should().NotBeNull();

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromAStar(request);

        hybridRequest.Should().NotBeNull();
        hybridRequest.RoutePlan.Should().NotBeNull();
        hybridRequest.RoutePlan.DirectedTransitions.Should().BeEmpty();

        GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost(hybridRequest, out int chartCost).Should().BeFalse();
        chartCost.Should().Be(0);
    }

    [Fact]
    public void TryGetTransitionAwareChartCost_ShouldAssignCost_WhenRouteUsesDirectedTransitions()
    {
        const string sceneKey = "GuidedPlannerTransitionAwareHelper";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        AStarPathRequest request = AStarPathRequest.Create(
            new Vector3d(1, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true);

        request.Should().NotBeNull();

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromAStar(request);

        hybridRequest.Should().NotBeNull();
        hybridRequest.RoutePlan.Should().NotBeNull();
        hybridRequest.RoutePlan.DirectedTransitions.Should().NotBeEmpty();

        GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost(hybridRequest, out int chartCost).Should().BeTrue();
        chartCost.Should().Be(hybridRequest.RoutePlan.TotalPathCost);
    }

    private static void RegisterSolidTargetLine(string chartKey, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartKey, data, minBounds);
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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenHandoffIsInvalid()
    {
        // Default TransitionId is null, so IsValid == false; the early-return branch is exercised.
        var handoff = new GuidedVolumeExitHandoff();
        handoff.TryCreateFollowupRequest(Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenAStarCreateReturnsNull()
    {
        // Positions far outside any grid make AStarPathRequest.Create return null,
        // exercising the create-failure return in the AStar case branch.
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartPathMode = GuidedPathMode.AStar,
            ChartOriginPosition = new Vector3d(1000, 0, 0),
            TargetPosition = new Vector3d(1001, 0, 0),
        };
        handoff.TryCreateFollowupRequest(Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenFlowFieldCreateReturnsNull()
    {
        // Same as above but for the FlowField case branch.
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartPathMode = GuidedPathMode.FlowField,
            ChartOriginPosition = new Vector3d(1000, 0, 0),
            TargetPosition = new Vector3d(1001, 0, 0),
        };
        handoff.TryCreateFollowupRequest(Vector3d.Zero, Fixed64.One, out _).Should().BeFalse();
    }
}
