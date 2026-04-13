using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using System;
using System.Reflection;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class NavMotorCoverageTailTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FinalizeTraversal_ShouldIgnoreCall_WhenTraversalNotInProgress()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        agent.Motor.SetVelocity(Vector3d.Right);

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            agent.FrameCondition,
            agent.GetFootPosition());

        agent.Motor.TraversalInProgress.Should().BeFalse();
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Right);
    }

    [Fact]
    public void GetMaxAcceleration_ShouldThrow_WhenMotorHasNotBeenInitialized()
    {
        var motor = CreateUninitializedMotor();

        motor.Invoking(m => m.GetMaxAcceleration())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*initialized*");
    }

    [Fact]
    public void StateAccessors_ShouldTrackPreviousMediumAndTransientFlags()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);

        agent.Motor.WasOnSolid.Should().BeFalse();
        agent.Motor.WasInLiquid.Should().BeFalse();
        agent.Motor.IsJumping.Should().BeFalse();
        agent.Motor.IsFalling.Should().BeFalse();

        agent.Motor.Handler.Jump!.IsJumping = true;
        agent.Motor.Handler.Fall.IsFalling = true;

        agent.Motor.IsJumping.Should().BeTrue();
        agent.Motor.IsFalling.Should().BeTrue();

        agent.Motor.UpdateTraversal(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, Fixed4x4.Identity)
            }
        });
        agent.Motor.UpdateTraversal(new TrekCondition { Medium = TraversalMedium.Liquid });
        agent.Motor.WasOnSolid.Should().BeTrue();

        agent.Motor.UpdateTraversal(new TrekCondition { Medium = TraversalMedium.Gas });
        agent.Motor.WasInLiquid.Should().BeTrue();
    }

    [Fact]
    public void SpeedHelpers_ShouldCoverTransferFlightAndGroundFallbackBranches()
    {
        var platformAgent = MockMotorAgentTestFactory.CreatePlatformAgent(motionTransfer: MotionTransfer.PermaTransfer);
        platformAgent.Motor.Handler.Platform!.FramePlatformVelocity = new Vector3d(2, 3, 0);

        InvokePrivate<Vector3d>(platformAgent.Motor, "ApplyPlatformTransferVelocity", Vector3d.Zero)
            .Should().Be(new Vector3d(2, 0, 0));

        var groundedAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        groundedAgent.Motor.Handler.Move.MaxSidewaysSpeed = Fixed64.One;
        groundedAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Stationary)
            .Should().Be(Fixed64.Zero);

        var airborneAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        airborneAgent.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)2;
        airborneAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Slow)
            .Should().Be((Fixed64)2);

        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.CanFly = false;
        flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Slow)
            .Should().Be(Fixed64.Zero);

        flyingAgent.Motor.Handler.Fly.CanFly = true;
        flyingAgent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)3;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)3;
        flyingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, (TrekRate)999)
            .Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void FinalizeTraversal_ShouldClearFallingWithoutLandingEvent_WhenGasExitsIntoLiquid()
    {
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(surfaceLevel: Fixed64.Zero);
        bool landed = false;
        agent.Motor.Events.OnLandedFall += () => landed = true;

        OpenTraversal(agent);

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Liquid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MAX_VALUE
            },
            agent.GetFootPosition());

        landed.Should().BeFalse();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void FinalizeTraversal_ShouldSubtractPlatformVelocity_WhenLandingBackOnSamePlatform()
    {
        Fixed4x4 platformTransform = MockMotorAgentTestFactory.CreatePlatformTransform();
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(platformMatrix: platformTransform);
        agent.Motor.Handler.Platform!.PlatformVelocity = new Vector3d(2, 0, 0);

        OpenTraversal(agent);

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MAX_VALUE,
                GroundState = new GroundCondition
                {
                    Platform = new PlatformSnapshot(1, platformTransform),
                    MotionTransferState = MotionTransfer.InitTransfer
                }
            },
            agent.GetFootPosition());

        agent.Motor.Handler.Move.FrameVelocity.x.Should().Be(-(Fixed64)2);
        agent.Motor.Handler.Platform.HoldPlatform.Should().BeNull();
    }

    [Fact]
    public void JsonRoundTrip_ShouldHydrateMissingCurrentState_ForUninitializedMotors()
    {
        var source = CreateUninitializedMotor();
        source.Handler = new LocomotionHandler(LocomotionProfile.CreateMoveAndFallOnly());

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateUninitializedMotor();
        JsonRecordSerializer.Populate(target, json);

        target.IsInitialized.Should().BeFalse();
        target.CurrentState.Should().NotBeNull();
        target.CurrentState.Medium.Should().Be(TraversalMedium.Unknown);
        target.TraversalInProgress.Should().BeFalse();
    }

    private static void OpenTraversal(MockMotorAgent agent)
    {
        agent.FrameRequest.Direction = Vector3d.Zero;
        agent.FrameRequest.Rate = TrekRate.Stationary;
        agent.FrameRequest.Origin = agent.Position;
        agent.FrameRequest.FootPosition = agent.GetFootPosition();
        agent.FrameRequest.Rotation = agent.Rotation;

        agent.Motor.TryTraversal(agent.FrameRequest, out _, out _, out _).Should().BeTrue();
    }

    private static NavMotor CreateUninitializedMotor()
    {
        ConstructorInfo ctor = typeof(NavMotor).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)!;
        return (NavMotor)ctor.Invoke(null);
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)method.Invoke(instance, arguments)!;
    }
}
