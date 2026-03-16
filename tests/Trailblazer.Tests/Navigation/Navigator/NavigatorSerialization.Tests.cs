using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Trailblazer.Serialization;
using Xunit;
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

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        target.Motor.IsInitialized.Should().BeTrue();
    }

    private static TestNavigator CreateNavigator(Vector3d position)
    {
        var navigator = new TestNavigator();
        navigator.Setup(
            position,
            rotation: FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f),
            velocity: new Vector3d(1, 0, 1),
            size: (Fixed64)2);
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

        var holdPlatform = new PlatformHandle(9, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(6, 0, 6)));
        source.Motor.Handler.Platform.IsNewPlatform = true;
        source.Motor.Handler.Platform.PreviousPlatform = new PlatformHandle(8, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 3)));
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
        source.SetTrekCondition(
            medium: TraversalMedium.Ground,
            surfaceLevel: Fixed64.Zero,
            surfaceCondition: new GroundCondition
            {
                Platform = new PlatformHandle(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1))),
                SurfaceFriction = (Fixed64)0.15f,
                MotionTransferState = MotionTransfer.PermaLocked
            },
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
        source.Motor.Handler.Platform.ActivePlatform = new PlatformHandle(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1)));
        source.Motor.Handler.Platform.MovementTransfer = MotionTransfer.PermaLocked;
        source.Motor.Handler.Platform.ScoutLocalPoint = new Vector3d(0, 0, 1);
        source.Motor.Handler.Platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        source.Motor.Handler.Jump.RegisterJump();
        source.Motor.Handler.Jump.FrameJumpDirection = Vector3d.Up;
        source.Motor.Handler.Fall.IsFalling = true;
        source.Motor.Handler.Fall.FallStart = (Fixed64)10;

        return source;
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
}
