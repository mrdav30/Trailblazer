using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using Trailblazer.Serialization;
using Xunit;
using Trailblazer.Tests;
using Trailblazer.Tests.Navigation.Motor;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorSerializationTests : IDisposable
{
    public NavigatorSerializationTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
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
    public void JsonRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();

        string json = JsonRecordSerializer.Serialize(source.Motor, writeIndented: true);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Ground);
        JsonRecordSerializer.Populate(target.Motor, json);

        AssertMotorStateMatches(source.Motor, target.Motor);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);

        target.Position.Should().Be(source.Position);
        target.LastPosition.Should().Be(source.LastPosition);
        target.Rotation.Should().Be(source.Rotation);
        target.Forward.Should().Be(source.Forward);
        target.Velocity.Should().Be(source.Velocity);
        target.Speed.Should().Be(source.Speed);
        target.Acceleration.Should().Be(source.Acceleration);
        target.Size.Should().Be(source.Size);
        target.FootPositionAdjust.Should().Be(source.FootPositionAdjust);
        target.GuidedPathMode.Should().Be(source.GuidedPathMode);
        target.GuidedAllowUnwalkable.Should().Be(source.GuidedAllowUnwalkable);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedAStarMaxClimbHeight.Should().Be(source.GuidedAStarMaxClimbHeight);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.AnimDampTime.Should().Be(source.AnimDampTime);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(source.Motor, target.Motor);
        AssertTurningStateMatches(source.Turning, target.Turning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        target.Motor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();

        byte[] data = MemoryPackRecordSerializer.Serialize(source.Motor);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Ground);
        MemoryPackRecordSerializer.Populate(target.Motor, data);

        AssertMotorStateMatches(source.Motor, target.Motor);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);

        target.Position.Should().Be(source.Position);
        target.LastPosition.Should().Be(source.LastPosition);
        target.Rotation.Should().Be(source.Rotation);
        target.Forward.Should().Be(source.Forward);
        target.Velocity.Should().Be(source.Velocity);
        target.Speed.Should().Be(source.Speed);
        target.Acceleration.Should().Be(source.Acceleration);
        target.Size.Should().Be(source.Size);
        target.FootPositionAdjust.Should().Be(source.FootPositionAdjust);
        target.GuidedPathMode.Should().Be(source.GuidedPathMode);
        target.GuidedAllowUnwalkable.Should().Be(source.GuidedAllowUnwalkable);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedAStarMaxClimbHeight.Should().Be(source.GuidedAStarMaxClimbHeight);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.AnimDampTime.Should().Be(source.AnimDampTime);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(source.Motor, target.Motor);
        AssertTurningStateMatches(source.Turning, target.Turning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        target.Motor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedAStarTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationJsonAStar");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.AStar);
        source.Steering.TrailGuide.Should().BeOfType<AStarGuide>();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(source.Steering, target.Steering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.Steering.ShouldMove.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_ShouldRebuildTurningRuntimeState_OnLoad()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.Turning.TurnRate = (Fixed64)0.35f;
        source.Turning.RequestTurnDirection(source.Forward, Vector3d.Right, interpolation: Fixed64.Half);
        source.Turning.TrySimulateTurn(
            source.Position,
            source.LastPosition,
            source.Forward,
            source.Rotation,
            out _).Should().BeTrue();

        source.Turning.TargetReached.Should().BeFalse();
        source.Turning.TargetRotation.Should().NotBe(FixedQuaternion.Identity);

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);

        target.Turning.CanTurn.Should().Be(source.Turning.CanTurn);
        target.Turning.TurnRate.Should().Be(source.Turning.TurnRate);
        target.Turning.TargetReached.Should().BeTrue();
        target.Turning.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedFlowFieldTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationMemoryPackFlowField");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.FlowField);
        source.Steering.TrailGuide.Should().BeOfType<FlowFieldGuide>();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(source.Steering, target.Steering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.Steering.ShouldMove.Should().BeTrue();
    }

    private static TestNavigator CreateNavigator(Vector3d position, Fixed64? size = null)
    {
        var navigator = new TestNavigator();
        navigator.Setup(
            position,
            rotation: FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f),
            velocity: new Vector3d(1, 0, 1),
            size: size ?? (Fixed64)2);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Ground,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static MockMotorAgent CreateConfiguredMotorAgent()
    {
        var source = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(2, 0, 3),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 5)),
            motionTransfer: MotionTransfer.PermaTransfer);

        source.Motor.Handler.IsInControl = false;
        source.Motor.Handler.Move.MaxFastSpeed = (Fixed64)1.75f;
        source.Motor.Handler.Move.FrameVelocity = new Vector3d(1, 2, 3);

        source.Motor.Handler.Jump.MaxJumpCount = 2;
        source.Motor.Handler.Jump.RegisterJump();
        source.Motor.Handler.Jump.FrameJumpDirection = new Vector3d(0, 1, 1).Normal;
        source.Motor.Handler.Jump.StartCooldown();

        source.Motor.Handler.Fall.IsFalling = true;
        source.Motor.Handler.Fall.FallStart = (Fixed64)9;
        source.Motor.Handler.Fall.FallEnd = (Fixed64)3;

        source.Motor.Handler.Slide.IsSliding = true;

        source.Motor.Handler.Swim.IsSwimming = true;
        source.Motor.Handler.Swim.IsDiving = true;
        source.Motor.Handler.Swim.UnderwaterTimer = (Fixed64)7;

        var holdPlatform = new PlatformSnapshot(9, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(6, 0, 6)));
        source.Motor.Handler.Platform.IsNewPlatform = true;
        source.Motor.Handler.Platform.PreviousPlatform = new PlatformSnapshot(8, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 3)));
        source.Motor.Handler.Platform.SetHoldPlatform(holdPlatform);
        source.Motor.Handler.Platform.TickHoldOnPlatform();
        source.Motor.Handler.Platform.ScoutLocalPoint = new Vector3d(1, 0, 1);
        source.Motor.Handler.Platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f);
        source.Motor.Handler.Platform.PlatformVelocity = new Vector3d(5, 0, 0);
        source.Motor.Handler.Platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

        return source;
    }

    private static TestNavigator CreateConfiguredNavigator()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Moderate, isRequestingJump: true);
        source.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1))),
            surfaceFriction: (Fixed64)0.15f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        source.GuidedPathMode = GuidedPathMode.FlowField;
        source.GuidedAllowUnwalkable = true;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.GuidedAStarMaxClimbHeight = (Fixed64)4;
        source.GuidedFlowFieldExtraFloodRange = 32;
        source.FootPositionAdjust = (Fixed64)0.75f;
        source.IsLockedOn = true;
        source.AnimDampTime = (Fixed64)0.25f;
        source.Motor.Handler.Move.FrameVelocity = new Vector3d(1, 0, 2);
        source.Motor.Handler.Platform.ActivePlatform = new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1)));
        source.Motor.Handler.Platform.MovementTransfer = MotionTransfer.PermaLocked;
        source.Motor.Handler.Platform.ScoutLocalPoint = new Vector3d(0, 0, 1);
        source.Motor.Handler.Platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        source.Motor.Handler.Jump.RegisterJump();
        source.Motor.Handler.Jump.FrameJumpDirection = Vector3d.Up;
        source.Motor.Handler.Fall.IsFalling = true;
        source.Motor.Handler.Fall.FallStart = (Fixed64)10;
        source.Turning.CanTurn = false;
        source.Turning.TurnRate = (Fixed64)0.35f;

        return source;
    }

    private static TestNavigator CreateConfiguredGuidedNavigator(GuidedPathMode pathMode)
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.GuidedPathMode = pathMode;
        source.GuidedAllowUnwalkable = true;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.GuidedAStarMaxClimbHeight = (Fixed64)2;
        source.GuidedFlowFieldExtraFloodRange = 24;

        source.Steering.PathRecheckCooldownFrames = 9;
        source.Steering.StopMultiplier = (Fixed64)0.75f;
        source.Steering.GroupFactor = (Fixed64)12;
        source.Steering.AvoidFactor = (Fixed64)4;
        source.Steering.BehaviorWeights = new GroupBehaviorWeights()
        {
            Separation = (Fixed64)3,
            Alignment = (Fixed64)0.75f,
            Cohesion = (Fixed64)0.4f,
            Avoidance = (Fixed64)1.25f
        };
        source.Steering.BrakingPower = (Fixed64)0.2f;

        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: pathMode,
            rate: TrekRate.Fast,
            isRequestingJump: true,
            groupId: 7);

        TrailblazerManager.Simulate();
        source.Steering.GetHeading(source);
        source.Steering.PauseAutoStop();

        if (source.Steering.TrailGuide is AStarGuide aStarGuide)
        {
            aStarGuide.AdvanceWaypoint();
            TrailblazerManager.Simulate();
            source.Steering.GetHeading(source);
        }

        return source;
    }

    private static void RegisterGuidedPathChart(string chartKey)
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

        PathTestFactory.RegisterFromData(chartKey, data, Vector3d.Zero);
    }

    private static void AssertMotorStateMatches(NavMotor expected, NavMotor actual)
    {
        actual.IsInitialized.Should().Be(expected.IsInitialized);
        actual.CurrentState.ToTrekCondition().Medium.Should().Be(expected.CurrentState.ToTrekCondition().Medium);
        actual.CurrentState.ToTrekCondition().SurfaceLevel.Should().Be(expected.CurrentState.ToTrekCondition().SurfaceLevel);
        actual.CurrentState.ToTrekCondition().CeilingLevel.Should().Be(expected.CurrentState.ToTrekCondition().CeilingLevel);
        actual.CurrentState.PreviousState.Should().Be(expected.CurrentState.PreviousState);

        actual.Handler.IsInControl.Should().Be(expected.Handler.IsInControl);

        actual.Handler.Move.IsEnabled.Should().Be(expected.Handler.Move.IsEnabled);
        actual.Handler.Move.FrameVelocity.Should().Be(expected.Handler.Move.FrameVelocity);
        actual.Handler.Move.MaxFastSpeed.Should().Be(expected.Handler.Move.MaxFastSpeed);

        actual.Handler.Jump.IsJumping.Should().Be(expected.Handler.Jump.IsJumping);
        actual.Handler.Jump.IsHoldingJump.Should().Be(expected.Handler.Jump.IsHoldingJump);
        actual.Handler.Jump.JumpStartTime.Should().Be(expected.Handler.Jump.JumpStartTime);
        actual.Handler.Jump.FrameJumpDirection.Should().Be(expected.Handler.Jump.FrameJumpDirection);
        actual.Handler.Jump.CanJump.Should().Be(expected.Handler.Jump.CanJump);

        actual.Handler.Fall.IsFalling.Should().Be(expected.Handler.Fall.IsFalling);
        actual.Handler.Fall.FallStart.Should().Be(expected.Handler.Fall.FallStart);
        actual.Handler.Fall.FallEnd.Should().Be(expected.Handler.Fall.FallEnd);

        actual.Handler.Slide.IsSliding.Should().Be(expected.Handler.Slide.IsSliding);

        actual.Handler.Swim.IsSwimming.Should().Be(expected.Handler.Swim.IsSwimming);
        actual.Handler.Swim.IsDiving.Should().Be(expected.Handler.Swim.IsDiving);
        actual.Handler.Swim.UnderwaterTimer.Should().Be(expected.Handler.Swim.UnderwaterTimer);

        actual.Handler.Platform.IsNewPlatform.Should().Be(expected.Handler.Platform.IsNewPlatform);
        actual.Handler.Platform.MovementTransfer.Should().Be(expected.Handler.Platform.MovementTransfer);
        actual.Handler.Platform.ScoutLocalPoint.Should().Be(expected.Handler.Platform.ScoutLocalPoint);
        actual.Handler.Platform.ScoutLocalRotation.Should().Be(expected.Handler.Platform.ScoutLocalRotation);
        actual.Handler.Platform.PlatformVelocity.Should().Be(expected.Handler.Platform.PlatformVelocity);
        actual.Handler.Platform.FramePlatformVelocity.Should().Be(expected.Handler.Platform.FramePlatformVelocity);
        actual.Handler.Platform.HoldPlatformFrames.Should().Be(expected.Handler.Platform.HoldPlatformFrames);

        actual.Handler.Platform.ActivePlatform.Should().NotBeNull();
        expected.Handler.Platform.ActivePlatform.Should().NotBeNull();
        actual.Handler.Platform.ActivePlatform?.Id.Should().Be(expected.Handler.Platform.ActivePlatform?.Id);
        actual.Handler.Platform.ActivePlatform?.Transform.Should().Be(expected.Handler.Platform.ActivePlatform?.Transform);

        actual.Handler.Platform.PreviousPlatform?.Id.Should().Be(expected.Handler.Platform.PreviousPlatform?.Id);
        actual.Handler.Platform.PreviousPlatform?.Transform.Should().Be(expected.Handler.Platform.PreviousPlatform?.Transform);
        actual.Handler.Platform.HoldPlatform?.Id.Should().Be(expected.Handler.Platform.HoldPlatform?.Id);
        actual.Handler.Platform.HoldPlatform?.Transform.Should().Be(expected.Handler.Platform.HoldPlatform?.Transform);
    }

    private static void AssertSteeringStateMatches(NavSteering expected, NavSteering actual)
    {
        actual.CanPathfind.Should().Be(expected.CanPathfind);
        actual.Destination.Should().Be(expected.Destination);
        actual.PathRecheckCooldownFrames.Should().Be(expected.PathRecheckCooldownFrames);
        actual.TargetDirection.Should().Be(expected.TargetDirection);
        actual.LastTargetDirection.Should().Be(expected.LastTargetDirection);
        actual.ShouldMove.Should().Be(expected.ShouldMove);
        actual.IsStuck.Should().Be(expected.IsStuck);
        actual.HasLineOfSightPath.Should().Be(expected.HasLineOfSightPath);
        actual.DistanceToTarget.Should().Be(expected.DistanceToTarget);
        actual.IsAtDestination.Should().Be(expected.IsAtDestination);
        actual.CanMove.Should().Be(expected.CanMove);
        actual.StoppedFrameCount.Should().Be(expected.StoppedFrameCount);
        actual.CanAutoStop.Should().Be(expected.CanAutoStop);
        actual.StopMultiplier.Should().Be(expected.StopMultiplier);
        actual.GroupFactor.Should().Be(expected.GroupFactor);
        actual.AvoidFactor.Should().Be(expected.AvoidFactor);
        actual.BehaviorWeights.Separation.Should().Be(expected.BehaviorWeights.Separation);
        actual.BehaviorWeights.Alignment.Should().Be(expected.BehaviorWeights.Alignment);
        actual.BehaviorWeights.Cohesion.Should().Be(expected.BehaviorWeights.Cohesion);
        actual.BehaviorWeights.Avoidance.Should().Be(expected.BehaviorWeights.Avoidance);
        actual.BrakingPower.Should().Be(expected.BrakingPower);
        actual.MovementGroupID.Should().Be(expected.MovementGroupID);

        if (expected.CurrentRequest == null)
        {
            actual.CurrentRequest.Should().BeNull();
        }
        else
        {
            actual.CurrentRequest.Should().NotBeNull();
            actual.CurrentRequest.GetType().Should().Be(expected.CurrentRequest.GetType());
            actual.CurrentRequest.Origin.Should().Be(expected.CurrentRequest.Origin);
            actual.CurrentRequest.TargetPosition.Should().Be(expected.CurrentRequest.TargetPosition);
            actual.CurrentRequest.UnitSize.Should().Be(expected.CurrentRequest.UnitSize);
            actual.CurrentRequest.AllowUnwalkable.Should().Be(expected.CurrentRequest.AllowUnwalkable);
            actual.CurrentRequest.MaxPathSearchRange.Should().Be(expected.CurrentRequest.MaxPathSearchRange);

            if (expected.CurrentRequest is AStarPathRequest expectedAStar
                && actual.CurrentRequest is AStarPathRequest actualAStar)
            {
                actualAStar.Heuristic.Should().Be(expectedAStar.Heuristic);
                actualAStar.MaxClimbHeight.Should().Be(expectedAStar.MaxClimbHeight);
            }

            if (expected.CurrentRequest is FlowFieldPathRequest expectedFlowField
                && actual.CurrentRequest is FlowFieldPathRequest actualFlowField)
            {
                actualFlowField.ExtraFloodRange.Should().Be(expectedFlowField.ExtraFloodRange);
            }
        }

        if (expected.TrailGuide == null)
        {
            actual.TrailGuide.Should().BeNull();
        }
        else
        {
            actual.TrailGuide.Should().NotBeNull();
            actual.TrailGuide.GetType().Should().Be(expected.TrailGuide.GetType());

            if (expected.TrailGuide is AStarGuide expectedAStarGuide
                && actual.TrailGuide is AStarGuide actualAStarGuide)
            {
                actualAStarGuide.CurrentWaypointIndex.Should().Be(expectedAStarGuide.CurrentWaypointIndex);
            }
        }
    }

    private static void AssertTurningStateMatches(NavTurning expected, NavTurning actual)
    {
        actual.CanTurn.Should().Be(expected.CanTurn);
        actual.TurnRate.Should().Be(expected.TurnRate);
        actual.TargetReached.Should().BeTrue();
        actual.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }
}
