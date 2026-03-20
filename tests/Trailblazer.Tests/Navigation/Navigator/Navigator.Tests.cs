using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Animation;
using Trailblazer.Navigation.Motor;
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
        VolumeTraversalRules.SetWaterVoxelPartition<TestWaterPartition>();
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
        navigator.GuidedAllowUnwalkable = true;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        navigator.GuidedAStarMaxClimbHeight = (Fixed64)2;

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
        request.AllowUnwalkable.Should().BeTrue();
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
        navigator.GuidedAllowUnwalkable = true;
        navigator.GuidedAllowTraversalTransitions = true;
        navigator.GuidedFlowFieldExtraFloodRange = 24;

        Vector3d target = new(4, 0, 0);
        navigator.ApplyGuidedTrekRequest(target, pathMode: GuidedPathMode.FlowField, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.AllowUnwalkable.Should().BeTrue();
        request.AllowTraversalTransitions.Should().BeTrue();
        request.ExtraFloodRange.Should().Be(24);

        PathManager.UnloadChart("NavigatorFlowField");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateAerialRequest_AndEnableFlight()
    {
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
        request.TraversalMode.Should().Be(VolumeTraversalMode.Open);
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
        request.TraversalMode.Should().Be(VolumeTraversalMode.Water);
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
    public void ApplyInputTrekRequest_ShouldCaptureFlightIntent()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Up,
            TrekRate.Fast,
            isRequestingJump: false,
            isRequestingFlight: true);

        navigator.FrameRequest.Direction.Should().Be(Vector3d.Up);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);
        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
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

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Ground);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);
        navigator.FrameCondition.GroundState.Value.Platform.Transform.Translation.Should().Be(snapshot.Transform.Translation);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.2f);
        navigator.FrameCondition.GroundState.Value.MotionTransferState.Should().Be(MotionTransfer.PermaLocked);

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Ground);
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

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Air);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        navigator.FrameCondition.GroundState.Should().NotBeNull();
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Air);
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

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Water);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)2);
        navigator.FrameCondition.GroundState.Should().BeNull();

        TrekCondition motorCondition = navigator.Motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Water);
        motorCondition.GroundState.Should().BeNull();
    }

    private static TestNavigator CreateNavigator(Vector3d position)
    {
        var navigator = new TestNavigator();
        navigator.Setup(position, size: Fixed64.One);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Ground,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static void AddWater(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        voxel.TryAddPartition(new TestWaterPartition()).Should().BeTrue();
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
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Volume(new Vector3d(1, 0, 0), VolumeTraversalMode.Water),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "navigator-transition-fallback-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Volume(new Vector3d(3, 0, 0), VolumeTraversalMode.Water),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(4, 0, 0)),
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
}
