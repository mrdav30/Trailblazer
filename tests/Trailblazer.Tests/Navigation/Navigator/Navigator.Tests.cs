using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Animation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorTests : IDisposable
{
    public NavigatorTests()
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
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateAStarRequest_FromNavigatorDefaults()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorAStar", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowUnwalkableEndpoints = true;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        navigator.GuidedMaxClimbHeight = (Fixed64)2;

        Vector3d target = new(4, 0, 0);
        navigator.ApplyGuidedTrekRequest(target, rate: TrekRate.Moderate, groupId: 4);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
        navigator.Steering.MovementGroupID.Should().Be(4);

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.AllowUnwalkableEndpoints.Should().BeTrue();
        request.AllowTraversalTransitions.Should().BeTrue();
        request.Heuristic.Should().Be(HeuristicMethod.Euclidean);
        request.MaxClimbHeight.Should().Be((Fixed64)2);

        PathManager.UnloadChart("NavigatorAStar");
    }

    [Fact]
    public void Setup_ShouldHonorExplicitGlobalId()
    {
        Guid explicitId = new("11111111-2222-3333-4444-555555555555");
        var navigator = new TestNavigator();

        navigator.Setup(Vector3d.Zero, size: Fixed64.One, globalId: explicitId);

        navigator.GlobalId.Should().Be(explicitId);
    }

    [Fact]
    public void Setup_ShouldAssignDeterministicGlobalIds_AndReplayAfterReset()
    {
        var first = new TestNavigator();
        var second = new TestNavigator();

        first.Setup(Vector3d.Zero, size: Fixed64.One);
        second.Setup(Vector3d.Right, size: Fixed64.One);

        Guid firstId = first.GlobalId;
        Guid secondId = second.GlobalId;

        secondId.Should().NotBe(firstId);
        firstId.Should().NotBe(Guid.Empty);

        TrailblazerManager.Reset();

        var replayFirst = new TestNavigator();
        var replaySecond = new TestNavigator();

        replayFirst.Setup(Vector3d.Zero, size: Fixed64.One);
        replaySecond.Setup(Vector3d.Right, size: Fixed64.One);

        replayFirst.GlobalId.Should().Be(firstId);
        replaySecond.GlobalId.Should().Be(secondId);
    }

    [Fact]
    public void Setup_ShouldRejectEmptyExplicitGlobalId()
    {
        var navigator = new TestNavigator();

        Action act = () => navigator.Setup(Vector3d.Zero, size: Fixed64.One, globalId: Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("globalId");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_Allow_PerCallPathModeOverride()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorFlowField", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowUnwalkableEndpoints = true;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedMaxClimbHeight = (Fixed64)2;
        navigator.GuidedFlowFieldExtraFloodRange = 24;

        Vector3d target = new(4, 0, 0);
        navigator.ApplyGuidedTrekRequest(target, pathMode: GuidedPathMode.FlowField, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.AllowUnwalkableEndpoints.Should().BeTrue();
        request.AllowTraversalTransitions.Should().BeTrue();
        request.MaxClimbHeight.Should().Be((Fixed64)2);
        request.ExtraFloodRange.Should().Be(24);

        PathManager.UnloadChart("NavigatorFlowField");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateAerialRequest_AndEnableFlight()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(0, 2, 0));
        AddOpen(new Vector3d(0, 3, 0));

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedAllowTraversalTransitions = true;
        Vector3d target = new(0, 3, 0);

        navigator.ApplyGuidedTrekRequest(target, pathMode: GuidedPathMode.Aerial, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.Heuristic.Should().Be(navigator.GuidedAStarHeuristic);
        request.Medium.Should().Be(TraversalMedium.Gas);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateAerialLandingHandoff_WhenTransitionOptInIsEnabled()
    {
        const string sceneKey = "NavigatorAerialLandingHandoff";
        RegisterAerialLandingHandoffScene(sceneKey);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedAStarHeuristic = HeuristicMethod.Euclidean;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Aerial,
            rate: TrekRate.Fast,
            groupId: 9);

        VolumePathRequest initialRequest = navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        initialRequest.Medium.Should().Be(TraversalMedium.Gas);
        initialRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();

        navigator.SetTestPosition(new Vector3d(1, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        AStarPathRequest followupRequest = navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject;
        followupRequest.TargetPosition.x.Should().Be((Fixed64)4);
        followupRequest.TargetPosition.y.Should().Be(Fixed64.Zero);
        followupRequest.TargetPosition.z.Should().Be(Fixed64.Zero);
        followupRequest.AllowTraversalTransitions.Should().BeTrue();
        navigator.Steering.TrailGuide.Should().BeOfType<AStarGuide>();
        navigator.Steering.Destination.x.Should().Be((Fixed64)4);
        navigator.Steering.MovementGroupID.Should().Be(9);
        navigator.Steering.ShouldMove.Should().BeTrue();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();

        UnloadAerialLandingHandoffScene(sceneKey);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateTransitionAwareAStarGuide_WhenNavigatorOptInIsEnabled()
    {
        RegisterTransitionFallbackScene();

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedAStarHeuristic = HeuristicMethod.Euclidean;

        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);

        navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>()
            .Which.AllowTraversalTransitions.Should().BeTrue();

        TrailblazerManager.Simulate();
        navigator.Steering.GetHeading(navigator);

        navigator.Steering.TrailGuide.Should().BeOfType<AStarGuide>();
        navigator.Steering.ShouldMove.Should().BeTrue();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateTransitionAwareFlowFieldGuide_WhenNavigatorOptInIsEnabled()
    {
        RegisterTransitionFallbackScene();

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.FlowField;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedFlowFieldExtraFloodRange = 8;

        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);

        navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>()
            .Which.AllowTraversalTransitions.Should().BeTrue();

        TrailblazerManager.Simulate();
        navigator.Steering.GetHeading(navigator);

        navigator.Steering.TrailGuide.Should().BeOfType<FlowFieldGuide>();
        navigator.Steering.ShouldMove.Should().BeTrue();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldCaptureExplicitClimbIntent()
    {
        var data = new bool[1, 4, 1]
        {
            {
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorGuidedClimbIntent", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(3, 0, 0),
            rate: TrekRate.Fast,
            isRequestingClimb: true);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart("NavigatorGuidedClimbIntent");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldEnableClimb_WhenTransitionAwareRouteRequestsIt()
    {
        GuidedPathTestScene.RegisterTransitionFallbackClimbScene();

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);

        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>()
            .Which.AllowTraversalTransitions.Should().BeTrue();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldPreserveClimbAcrossSwimExitHandoff_WhenGeneratedSwimExitTargetsLiquidClimbShoreline()
    {
        const string chartKey = "NavigatorLiquidClimbExit";
        GuidedPathTestScene.RegisterLiquidClimbExitScene(chartKey);

        var navigator = CreateNavigator(new Vector3d(1, 0, 0));
        navigator.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.GuidedPathMode = GuidedPathMode.FlowField;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(5, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast);

        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        VolumePathRequest initialRequest = navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        initialRequest.TargetPosition.Should().Be(new Vector3d(3, 0, 0));

        navigator.SetTestPosition(new Vector3d(4, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart(chartKey);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateSwimRequest_WithoutFlight_WhenInWater()
    {
        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(0, 0, 1));
        AddWater(new Vector3d(0, 0, 2));

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetWaterContact(surfaceLevel: (Fixed64)2, updateMotorState: true);

        Vector3d target = new(0, 0, 2);
        navigator.ApplyGuidedTrekRequest(target, pathMode: GuidedPathMode.Swim, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.Medium.Should().Be(TraversalMedium.Liquid);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldPreserveClimbAcrossGuidedHandoff_WhenTransitionRequestsIt()
    {
        const string sceneKey = "NavigatorAerialClimbHandoff";
        GuidedPathTestScene.RegisterAerialClimbHandoffScene(sceneKey);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Aerial,
            rate: TrekRate.Fast);

        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        navigator.SetTestPosition(new Vector3d(1, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart($"{sceneKey}-Landing");
        PathManager.UnloadChart($"{sceneKey}-Target");
    }

    [Fact]
    public void Simulate_ShouldSyncAutoGuidedClimbIntent_WhenSteeringPublishesClimbRouteTopology()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorAutoGuidedClimbSync", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetTestSteering(new ScriptedRouteTopologySteering(
            navigator.Radius,
            new ScriptedRouteTopologyFrame(Vector3d.Right, RequestsClimbIntent: true)));

        navigator.ApplyGuidedTrekRequest(new Vector3d(2, 0, 0), rate: TrekRate.Fast);
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart("NavigatorAutoGuidedClimbSync");
    }

    [Fact]
    public void Simulate_ShouldClearAutoGuidedClimbIntent_WhenSteeringPublishesNonClimbRouteTopology()
    {
        GuidedPathTestScene.RegisterTransitionFallbackClimbScene();

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetTestSteering(new ScriptedRouteTopologySteering(
            navigator.Radius,
            new ScriptedRouteTopologyFrame(Vector3d.Right, RequestsClimbIntent: false)));
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
    }

    [Fact]
    public void Simulate_ShouldPreserveExplicitGuidedClimbIntent_WhenSteeringPublishesDifferentRouteTopology()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorExplicitGuidedClimbSticky", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetTestSteering(new ScriptedRouteTopologySteering(
            navigator.Radius,
            new ScriptedRouteTopologyFrame(Vector3d.Right, RequestsClimbIntent: true)));

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(2, 0, 0),
            rate: TrekRate.Fast,
            isRequestingClimb: false);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();

        PathManager.UnloadChart("NavigatorExplicitGuidedClimbSticky");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateSwimExitHandoff_WhenTransitionOptInIsEnabled()
    {
        RegisterVolumeExitHandoffScene("NavigatorSwimExitHandoff");

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.GuidedPathMode = GuidedPathMode.FlowField;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedMaxClimbHeight = (Fixed64)2;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast,
            groupId: 7);

        VolumePathRequest initialRequest = navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        initialRequest.Medium.Should().Be(TraversalMedium.Liquid);
        initialRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        navigator.SetTestPosition(new Vector3d(2, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        FlowFieldPathRequest followupRequest = navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        followupRequest.TargetPosition.Should().Be(new Vector3d(4, 0, 0));
        followupRequest.MaxClimbHeight.Should().Be((Fixed64)2);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.Steering.MovementGroupID.Should().Be(7);
        navigator.Steering.ShouldMove.Should().BeTrue();
        navigator.FrameRequest.Direction.x.Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("NavigatorSwimExitHandoff");
    }

    [Fact]
    public void Simulate_ShouldRecomputeAutoGuidedClimbIntent_FromFollowupRouteTopology_AfterVolumeExitHandoff()
    {
        const string chartKey = "NavigatorVolumeExitFollowupClimb";
        GuidedPathTestScene.RegisterVolumeExitFollowupClimbScene(chartKey);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast);

        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>()
            .Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        navigator.SetTestPosition(new Vector3d(2, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>()
            .Which.AllowTraversalTransitions.Should().BeTrue();
        navigator.Steering.TrailGuide.Should().BeOfType<AStarGuide>();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart(chartKey);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_RejectSwimRequest_WhenTransitionOptInIsDisabled_AndTargetRequiresExitHandoff()
    {
        RegisterVolumeExitHandoffScene("NavigatorSwimExitDisabled");

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.GuidedPathMode = GuidedPathMode.FlowField;
        navigator.GuidedAllowTraversalTransitions = false;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeFalse();
        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.Steering.ShouldMove.Should().BeFalse();

        PathManager.UnloadChart("NavigatorSwimExitDisabled");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_RejectSwimRequests_WhenNavigatorIsNotInWater()
    {
        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(0, 0, 1));
        AddWater(new Vector3d(0, 0, 2));

        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyGuidedTrekRequest(new Vector3d(0, 0, 2), pathMode: GuidedPathMode.Swim, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.Steering.ShouldMove.Should().BeFalse();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_IgnoreInvalidTargets_WithoutEnteringGuidedMode()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyGuidedTrekRequest(new Vector3d(100, 0, 100), rate: TrekRate.Moderate);

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.Steering.ShouldMove.Should().BeFalse();
    }

    [Fact]
    public void Reset_ShouldClearGuidedMode()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorResetGuided", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Moderate);

        navigator.IsGuideded.Should().BeTrue();

        navigator.Reset();

        navigator.IsGuideded.Should().BeFalse();

        PathManager.UnloadChart("NavigatorResetGuided");
    }

    [Fact]
    public void Simulate_ShouldResolveHeading_ForGuidedRequests()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorGuidedHeading", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Moderate);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        Vector3d.Dot(navigator.FrameRequest.Direction, Vector3d.Right).Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("NavigatorGuidedHeading");
    }

    [Fact]
    public void Simulate_Should_PersistGuidedFlightIntent_BetweenFrames()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(0, 2, 0));
        AddOpen(new Vector3d(0, 3, 0));

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(0, 3, 0), pathMode: GuidedPathMode.Aerial, rate: TrekRate.Fast);

        TrailblazerManager.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();

        navigator.ApplyGuidedTrekRequest(new Vector3d(0, 3, 0), pathMode: GuidedPathMode.Aerial, rate: TrekRate.Fast);
        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
        navigator.FrameRequest.Direction.y.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Simulate_Should_NotReuseGuidedDirection_AfterSteeringArrive()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorArriveStopsHeading", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);

        TrailblazerManager.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();

        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Steering.ShouldMove.Should().BeFalse();
        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);

        PathManager.UnloadChart("NavigatorArriveStopsHeading");
    }

    [Fact]
    public void BindAnimationHandler_ShouldForwardAnimationUpdatesDuringSimulate()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var handler = new TestAnimationHandler();
        navigator.BindAnimationHandler(handler);

        navigator.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Fast, isRequestingJump: false);
        TrailblazerManager.Simulate();
        navigator.Simulate();

        handler.LastForward.Should().Be(Fixed64.One);
        handler.LastSideways.Should().Be(Fixed64.Zero);
        handler.LastDampTime.Should().Be(navigator.AnimDampTime);
        handler.LastIsSprinting.Should().BeTrue();
        handler.UpdateCount.Should().Be(1);
    }

    [Fact]
    public void BindAnimationHandler_ShouldRejectNull()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.Invoking(n => n.BindAnimationHandler(null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("handler");
    }

    [Fact]
    public void UnbindAnimationHandler_ShouldStopForwardingAnimationUpdates()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var handler = new TestAnimationHandler();
        navigator.BindAnimationHandler(handler);

        navigator.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Moderate, isRequestingJump: false);
        TrailblazerManager.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();

        navigator.UnbindAnimationHandler();

        navigator.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Fast, isRequestingJump: false);
        TrailblazerManager.Simulate();
        navigator.Simulate();

        handler.UpdateCount.Should().Be(1);
    }

    [Fact]
    public void BindAnimationHandler_ShouldUseLocalMovementAxes_WhenLockedOnAndBackpedaling()
    {
        var navigator = CreateNavigator(
            Vector3d.Zero,
            rotation: FixedQuaternion.FromDirection(Vector3d.Right));
        var handler = new TestAnimationHandler();
        navigator.BindAnimationHandler(handler);
        navigator.IsLockedOn = true;

        navigator.ApplyInputTrekRequest(
            Vector3d.Left,
            TrekRate.Moderate,
            isRequestingJump: false,
            isRequestingFlight: false,
            facingDirection: Vector3d.Right);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        handler.LastForward.Should().Be(-Fixed64.One);
        handler.LastSideways.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldCaptureFlightAndClimbIntent()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Up,
            TrekRate.Fast,
            isRequestingJump: false,
            isRequestingFlight: true,
            isRequestingClimb: true);

        navigator.FrameRequest.Direction.Should().Be(Vector3d.Up);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldCaptureFacingDirection()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Moderate,
            isRequestingJump: false,
            isRequestingFlight: false,
            facingDirection: Vector3d.Forward);

        navigator.FrameRequest.Direction.Should().Be(Vector3d.Backward);
        navigator.FrameRequest.FacingDirection.Should().Be(Vector3d.Forward);
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldUseDefaults_WhenArgumentsAreOmitted()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest();

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Stationary);
        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.FrameRequest.FacingDirection.Should().BeNull();
    }

    [Fact]
    public void Simulate_ShouldUseFacingDirectionForTurnSelection()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Fast,
            isRequestingJump: false,
            isRequestingFlight: false,
            facingDirection: Vector3d.Right);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
    }

    [Fact]
    public void Simulate_ShouldNotAutoTurnToMovement_WhenLockedOnAndNotSprinting()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.IsLockedOn = true;

        navigator.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Moderate,
            isRequestingJump: false,
            isRequestingFlight: false);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Rotation.Should().Be(FixedQuaternion.Identity);
        navigator.Turning.TargetReached.Should().BeTrue();
    }

    [Fact]
    public void Simulate_ShouldAutoTurnToMovement_WhenLockedOnAndSprinting()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.IsLockedOn = true;

        navigator.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Fast,
            isRequestingJump: false,
            isRequestingFlight: false);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
    }

    [Fact]
    public void Simulate_ShouldKeepGuidedTurnBehavior_WhenLockedOn()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorGuidedTurnWhileLockedOn", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.IsLockedOn = true;
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Moderate);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(navigator.FrameRequest.Direction));

        PathManager.UnloadChart("NavigatorGuidedTurnWhileLockedOn");
    }

    [Fact]
    public void Simulate_ShouldAllowBackpedalWithoutChangingFacing_WhenFacingDirectionMatchesForward()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Fast,
            isRequestingJump: false,
            isRequestingFlight: false,
            facingDirection: Vector3d.Forward);

        TrailblazerManager.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();

        navigator.Rotation.Should().Be(FixedQuaternion.Identity);
        navigator.Forward.Should().Be(Vector3d.Forward);
        navigator.Position.z.Should().BeLessThan(Fixed64.Zero);
        navigator.Velocity.z.Should().BeLessThan(Fixed64.Zero);
    }

    [Fact]
    public void NotifyCollision_ShouldForwardToTurningController()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetTestPosition(new Vector3d(1, 0, 0), syncLastPosition: false);

        navigator.NotifyCollision();

        TrailblazerManager.Simulate();
        navigator.Simulate();
        navigator.Turning.TargetReached.Should().BeTrue();
        navigator.CommitFrameMotion();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Turning.TargetReached.Should().BeFalse();
        navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
        navigator.CommitFrameMotion();
    }

    [Fact]
    public void SetGroundContact_ShouldPopulateGroundStateAndUpdateMotorWhenRequested()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var snapshot = new PlatformSnapshot(
            7,
            Fixed4x4.CreateTransform(new Vector3d(3, 1, 2), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: (Fixed64)3,
            platform: snapshot,
            surfaceFriction: (Fixed64)0.2f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Solid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);
        navigator.FrameCondition.GroundState.Value.Platform.Transform.Translation.Should().Be(snapshot.Transform.Translation);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.2f);
        navigator.FrameCondition.GroundState.Value.MotionTransferState.Should().Be(MotionTransfer.PermaLocked);

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Solid);
        motorCondition.SurfaceLevel.Should().Be((Fixed64)3);
        motorCondition.GroundState.Should().NotBeNull();
        motorCondition.GroundState.Value.Platform.Should().Be(snapshot);
        motorCondition.GroundState.Value.Platform.Transform.Translation.Should().Be(snapshot.Transform.Translation);
    }

    [Fact]
    public void SetAirborne_ShouldPreserveGroundStateByDefault()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var snapshot = new PlatformSnapshot(
            5,
            Fixed4x4.CreateTransform(new Vector3d(1, 0, 1), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: snapshot,
            motionTransfer: MotionTransfer.InitTransfer,
            updateMotorState: true);

        navigator.SetAirborne(surfaceLevel: (Fixed64)4, updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Gas);
        motorCondition.GroundState.Should().NotBeNull();
        motorCondition.GroundState.Value.Platform.Should().Be(snapshot);
    }

    [Fact]
    public void SetWaterContact_ShouldClearGroundState()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var snapshot = new PlatformSnapshot(
            5,
            Fixed4x4.CreateTransform(new Vector3d(1, 0, 1), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: snapshot,
            motionTransfer: MotionTransfer.InitTransfer,
            updateMotorState: true);

        navigator.SetWaterContact(surfaceLevel: (Fixed64)2, updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Liquid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)2);
        navigator.FrameCondition.GroundState.Should().BeNull();

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Liquid);
        motorCondition.GroundState.Should().BeNull();
    }

    [Fact]
    public void SyncCurrentTrekConditionToMotor_ShouldPushCurrentFrameConditionImmediately()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        TrekCondition replacement = new()
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = (Fixed64)2,
            GroundState = null,
            CeilingLevel = (Fixed64)5
        };

        navigator.ReplaceTrekCondition(replacement, updateMotorState: false);

        navigator.Motor.CurrentState.Medium.Should().Be(TraversalMedium.Solid);

        navigator.SyncCurrentTrekConditionToMotor();

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Liquid);
        motorCondition.SurfaceLevel.Should().Be((Fixed64)2);
        motorCondition.GroundState.Should().BeNull();
        motorCondition.CeilingLevel.Should().Be((Fixed64)5);
    }

    [Fact]
    public void InactiveNavigator_ShouldThrowForPrewarmSimulateAndCommit()
    {
        var navigator = new TestNavigator();

        navigator.Invoking(n => n.PrewarmMovementGroup())
            .Should().Throw<InvalidOperationException>();
        navigator.Invoking(n => n.Simulate())
            .Should().Throw<InvalidOperationException>();
        navigator.Invoking(n => n.CommitFrameMotion())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InactiveNavigator_ShouldIgnoreRequestConditionAndCollisionUpdates()
    {
        var navigator = new TestNavigator();

        navigator.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Fast,
            isRequestingJump: true,
            isRequestingFlight: true,
            isRequestingClimb: true,
            facingDirection: Vector3d.Forward);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), pathMode: GuidedPathMode.FlowField, rate: TrekRate.Fast, isRequestingJump: true, groupId: 7);
        navigator.SetTrekCondition(
            medium: TraversalMedium.Liquid,
            surfaceLevel: (Fixed64)3,
            surfaceCondition: new GroundCondition(),
            ceilingLevel: (Fixed64)6,
            updateMotorState: true);
        navigator.NotifyCollision();

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Stationary);
        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.FrameRequest.FacingDirection.Should().BeNull();
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Unknown);
        navigator.FrameCondition.SurfaceLevel.Should().Be(Fixed64.Zero);
        navigator.FrameCondition.GroundState.Should().BeNull();
        navigator.FrameCondition.CeilingLevel.Should().Be(Fixed64.MAX_VALUE);
    }

    [Fact]
    public void GuidedRequestSetters_ShouldUpdateFrameRequestState()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ToggleGuidedJump(true);
        navigator.ToggleGuidedFlight(true);
        navigator.ToggleGuidedClimb(true);
        navigator.SetGuidedTrekRate(TrekRate.Moderate);

        navigator.FrameRequest.IsRequestingJump.Should().BeTrue();
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
    }

    [Fact]
    public void Simulate_ShouldClearGuidedClimbIntent_WhenGuidedRequestCompletes()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorGuidedClimbClear", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(
            new Vector3d(2, 0, 0),
            rate: TrekRate.Fast,
            isRequestingClimb: true);

        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        navigator.Steering.Arrive();

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();

        PathManager.UnloadChart("NavigatorGuidedClimbClear");
    }

    [Fact]
    public void ReplaceAndSetTrekCondition_ShouldCloneState_AndOnlyUpdateMotorWhenRequested()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        TrekCondition replacement = new()
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = (Fixed64)4,
            CeilingLevel = (Fixed64)8
        };

        navigator.ReplaceTrekCondition(replacement, updateMotorState: false);
        replacement.Medium = TraversalMedium.Liquid;
        replacement.SurfaceLevel = (Fixed64)9;

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        navigator.Motor.CurrentState.Medium.Should().Be(TraversalMedium.Solid);

        navigator.ReplaceTrekCondition(new TrekCondition
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = (Fixed64)2,
            GroundState = null,
            CeilingLevel = (Fixed64)5
        }, updateMotorState: true);

        navigator.Motor.CurrentState.Medium.Should().Be(TraversalMedium.Liquid);
        navigator.Motor.CurrentState.SurfaceLevel.Should().Be((Fixed64)2);

        GroundCondition updatedGround = new()
        {
            SurfaceFriction = (Fixed64)0.25f
        };
        navigator.SetTrekCondition(
            surfaceLevel: (Fixed64)6,
            surfaceCondition: updatedGround,
            ceilingLevel: (Fixed64)7,
            updateMotorState: true);

        updatedGround.SurfaceFriction = Fixed64.Zero;

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Liquid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)6);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.25f);
        navigator.FrameCondition.CeilingLevel.Should().Be((Fixed64)7);
        navigator.Motor.CurrentState.SurfaceLevel.Should().Be((Fixed64)6);
        navigator.Motor.CurrentState.GroundState.Should().NotBeNull();
        navigator.Motor.CurrentState.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.25f);
    }

    [Fact]
    public void SetTrekCondition_ShouldPreserveExistingSurfaceState_WhenOptionalArgumentsAreOmitted()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var snapshot = new PlatformSnapshot(
            12,
            Fixed4x4.CreateTransform(new Vector3d(2, 0, 2), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: (Fixed64)3,
            platform: snapshot,
            surfaceFriction: (Fixed64)0.4f,
            motionTransfer: MotionTransfer.PermaLocked,
            ceilingLevel: (Fixed64)8,
            updateMotorState: false);

        navigator.SetTrekCondition(medium: TraversalMedium.Gas, updateMotorState: false);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.FrameCondition.CeilingLevel.Should().Be((Fixed64)8);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.4f);
        navigator.FrameCondition.GroundState.Value.MotionTransferState.Should().Be(MotionTransfer.PermaLocked);
    }

    [Fact]
    public void Simulate_ShouldIgnoreInvalidPendingGuidedVolumeExitHandoff()
    {
        RegisterVolumeExitHandoffScene("NavigatorInvalidPendingHandoff");

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.GuidedPathMode = GuidedPathMode.FlowField;
        navigator.GuidedAllowTraversalTransitions = true;

        navigator.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast,
            groupId: 5);

        navigator.SetTestPosition(new Vector3d(2, 0, 0));
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        navigator.Steering.Arrive();

        GuidedVolumeExitHandoff handoff = ReflectionUtility.GetPrivateFieldFromBase<GuidedVolumeExitHandoff>(
            navigator,
            "_pendingGuidedVolumeExitHandoff");
        handoff.TransitionId = null;

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.Steering.ShouldMove.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();

        PathManager.UnloadChart("NavigatorInvalidPendingHandoff");
    }

    [Fact]
    public void DeltaHelpers_ShouldIgnoreZeroInputs_AndApplyQueuedMotionOnCommit()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        FixedQuaternion quarterTurn = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);

        navigator.AddPositionDelta(Vector3d.Zero);
        navigator.ApplyRotationDelta(FixedQuaternion.Identity);
        navigator.AddVelocityDelta(Vector3d.Zero);
        navigator.AddPositionDelta(Vector3d.Right);
        navigator.ApplyRotationDelta(quarterTurn);
        navigator.AddVelocityDelta(Vector3d.Forward);

        navigator.CommitFrameMotion();

        navigator.Position.Should().Be(new Vector3d(1, 0, 1));
        navigator.Rotation.Should().Be(quarterTurn);
        navigator.Forward.Should().Be(quarterTurn.Rotate(Vector3d.Forward));
        navigator.Speed.Should().BeGreaterThan(Fixed64.Zero);
        navigator.Acceleration.Should().NotBe(Vector3d.Zero);
    }

    [Fact]
    public void CommitFrameMotion_ShouldReportZeroSpeed_WhenNoMovementOccurred()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.CommitFrameMotion();

        navigator.Speed.Should().Be(Fixed64.Zero);
        navigator.StuckThresholdSpeed.Should().Be(Fixed64.Zero);
        navigator.Acceleration.Should().Be(Vector3d.Zero);
    }

    private static TestNavigator CreateNavigator(Vector3d position, FixedQuaternion? rotation = null)
    {
        var navigator = new TestNavigator();
        navigator.Setup(position, rotation: rotation, size: Fixed64.One);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "NavigatorWater");
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "NavigatorOpen");
    }

    private static void AddObstacle(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        GridObstacleManager.TryAddObstacle(
            voxel.GlobalIndex,
            new BoundsKey(position, position)).Should().BeTrue();
    }

    private static void AddObstaclePlaneAtX(int x)
    {
        for (int y = -4; y <= 4; y++)
        {
            for (int z = -4; z <= 4; z++)
                AddObstacle(new Vector3d(x, y, z));
        }
    }

    private static void RegisterTransitionFallbackScene()
    {
        PathTestFactory.RegisterSingleWalkablePoint("NavigatorTransitionFallbackStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("NavigatorTransitionFallbackEnd", new Vector3d(4, 0, 0));

        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "navigator-transition-fallback-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "navigator-transition-fallback-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private static void RegisterAerialLandingHandoffScene(string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(Vector3d.Zero);

        AddObstaclePlaneAtX(2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    private static void UnloadAerialLandingHandoffScene(string sceneKey)
    {
        PathManager.UnloadChart($"{sceneKey}-Landing");
        PathManager.UnloadChart($"{sceneKey}-Target");
    }

    private static void RegisterVolumeExitHandoffScene(string chartKey)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1]
        {
            {
                { NavigationChartCell.SolidLiquid },
                { NavigationChartCell.Solid },
                { NavigationChartCell.Solid }
            }
        };

        PathManager.Register(NavigationChart.From3D(chartKey, data, new Vector3d(2, 0, 0), Fixed64.One));

        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(1, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private sealed class TestAnimationHandler : INavAnimationHandler
    {
        public Fixed64 LastForward { get; private set; }

        public Fixed64 LastSideways { get; private set; }

        public Fixed64 LastDampTime { get; private set; }

        public bool LastIsSprinting { get; private set; }

        public int UpdateCount { get; private set; }

        public void SetDirectionalInput(Fixed64 forward, Fixed64 sideways, Fixed64 dampTime)
        {
            LastForward = forward;
            LastSideways = sideways;
            LastDampTime = dampTime;
            UpdateCount++;
        }

        public void SetIsSprinting(bool isSprinting)
        {
            LastIsSprinting = isSprinting;
        }

        public void ApplyRootMotion(Vector3d deltaPosition, Fixed64 forceMultiplier)
        {
        }
    }

    private readonly record struct ScriptedRouteTopologyFrame(
        Vector3d Heading,
        bool? RequestsClimbIntent = null);

    private sealed class ScriptedRouteTopologySteering : NavSteering
    {
        private readonly ScriptedRouteTopologyFrame[] _frames;
        private int _frameIndex;

        public ScriptedRouteTopologySteering(Fixed64 radius, params ScriptedRouteTopologyFrame[] frames)
            : base(radius)
        {
            _frames = frames;
        }

        public override Vector3d GetHeading(ISteer navigator)
        {
            if (_frameIndex >= _frames.Length)
                return Vector3d.Zero;

            ScriptedRouteTopologyFrame frame = _frames[_frameIndex++];
            if (frame.RequestsClimbIntent.HasValue)
            {
                CurrentRouteRequestsClimbIntent = frame.RequestsClimbIntent.Value;
                CurrentRouteTopologyVersion++;
            }

            return frame.Heading;
        }
    }

}
