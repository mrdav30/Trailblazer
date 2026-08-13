using System;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class NavMotorCoverageTailTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
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
        var motor = NavMotor.CreateUninitialized(TestWorld.Context);

        motor.Invoking(m => m.GetMaxAcceleration())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*initialized*");
    }

    [Fact]
    public void TryTraversal_ShouldReturnFalse_WhenMotorHasNotBeenInitialized()
    {
        var motor = NavMotor.CreateUninitialized(TestWorld.Context);
        TrekRequest request = new()
        {
            Origin = Vector3d.Zero,
            FootPosition = Vector3d.Zero,
            Rotation = FixedQuaternion.Identity,
            Direction = Vector3d.Right,
            Rate = TrekRate.Moderate
        };

        motor.TryTraversal(request, out Vector3d velocityDelta, out Vector3d positionDelta, out FixedQuaternion rotationDelta)
            .Should().BeFalse();
        velocityDelta.Should().Be(Vector3d.Zero);
        positionDelta.Should().Be(Vector3d.Zero);
        rotationDelta.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void SyncTraversalState_ShouldPreserveLegacyTraversalUpdateBehavior()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);

        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = (Fixed64)3,
            CeilingLevel = (Fixed64)8
        }, isInitializing: true);

        agent.Motor.CurrentState.Medium.Should().Be(TraversalMedium.Gas);
        agent.Motor.CurrentState.PreviousMedium.Should().Be(TraversalMedium.Gas);

        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = Fixed64.One
        });

        agent.Motor.CurrentState.Medium.Should().Be(TraversalMedium.Liquid);
        agent.Motor.CurrentState.PreviousMedium.Should().Be(TraversalMedium.Gas);
        agent.Motor.StateChanged.Should().BeTrue();
    }

    [Fact]
    public void StateAccessors_ShouldTrackPreviousMediumAndTransientFlags()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);

        agent.Motor.InLimbo.Should().BeTrue();
        agent.Motor.WasOnSolid.Should().BeFalse();
        agent.Motor.WasInLiquid.Should().BeFalse();
        agent.Motor.IsJumping.Should().BeFalse();
        agent.Motor.IsFalling.Should().BeFalse();

        agent.Motor.Handler.Jump!.IsJumping = true;
        agent.Motor.Handler.Fall.IsFalling = true;

        agent.Motor.IsJumping.Should().BeTrue();
        agent.Motor.IsFalling.Should().BeTrue();

        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, Fixed4x4.Identity)
            }
        });
        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Liquid });
        agent.Motor.WasOnSolid.Should().BeTrue();

        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        agent.Motor.WasInLiquid.Should().BeTrue();
    }

    [Fact]
    public void StateAccessors_ShouldHandleMissingLocomotions_AndPreviousNonMatchingMedia()
    {
        var moveAndFallOnly = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Gas,
            profile: LocomotionProfile.CreateCoreOnly());

        moveAndFallOnly.Motor.IsJumping.Should().BeFalse();
        moveAndFallOnly.Motor.IsFalling.Should().BeFalse();

        moveAndFallOnly.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        moveAndFallOnly.Motor.WasOnSolid.Should().BeFalse();
        moveAndFallOnly.Motor.WasInLiquid.Should().BeFalse();
        moveAndFallOnly.Motor.StateChanged.Should().BeFalse();

        var airborneAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        airborneAgent.Motor.Handler.Jump!.IsJumping = true;
        airborneAgent.Motor.Handler.Fall.IsFalling = true;
        airborneAgent.Motor.InLimbo.Should().BeFalse();
    }

    [Fact]
    public void SpeedHelpers_ShouldCoverTransferFlightAndGroundFallbackBranches()
    {
        var platformAgent = MockMotorAgentTestFactory.CreatePlatformAgent(motionTransfer: MotionTransfer.PermaTransfer);
        platformAgent.Motor.Handler.Platform!.FramePlatformVelocity = new Vector3d(2, 3, 0);

        ReflectionUtility.InvokePrivate<Vector3d>(platformAgent.Motor, "ApplyPlatformTransferVelocity", Vector3d.Zero)
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
    public void SpeedAndTraversalHelpers_ShouldCoverRemainingFlightLiquidAndTraversalBranches()
    {
        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.CanFly = true;
        flyingAgent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)5;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)5;

        TrekRequest flightRequest = new()
        {
            Rotation = FixedQuaternion.Identity,
            Direction = Vector3d.Right,
            Rate = (TrekRate)999
        };

        ReflectionUtility.InvokePrivate<Vector3d>(flyingAgent.Motor, "GetFlightVelocity", flightRequest)
            .Should().Be(Vector3d.Zero);

        var swimmingAgent = MockMotorAgentTestFactory.CreateWaterAgent();
        swimmingAgent.Motor.Handler.Water!.IsSwimming = true;
        swimmingAgent.Motor.Handler.Water!.MaxSwimSidewaysSpeed = Fixed64.One;
        swimmingAgent.Motor.Handler.Water.MaxSwimSpeed = Fixed64.Zero;
        swimmingAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().Be(Fixed64.Zero);

        var fallbackProfileAgent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Gas,
            profile: LocomotionProfile.CreateCoreOnly());
        fallbackProfileAgent.Motor.SetLocomotionProfile(LocomotionProfile.CreateCoreOnly());

        var staleTraversalAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        staleTraversalAgent.FrameRequest.Origin = staleTraversalAgent.Position;
        staleTraversalAgent.FrameRequest.FootPosition = staleTraversalAgent.GetFootPosition();
        staleTraversalAgent.FrameRequest.Rotation = staleTraversalAgent.Rotation;
        staleTraversalAgent.Motor.TryTraversal(staleTraversalAgent.FrameRequest, out _, out _, out _).Should().BeTrue();
        TestWorld.Context.Simulate();

        staleTraversalAgent.Motor.Invoking(m => m.TryTraversal(staleTraversalAgent.FrameRequest, out _, out _, out _))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*never finalized*");
    }

    [Fact]
    public void SetLocomotionProfile_ShouldNotTouchPlatformState_WhenNavigatorIsNotGrounded()
    {
        var airborneAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        var profile = LocomotionProfile.CreateCoreOnly();

        airborneAgent.Motor.SetLocomotionProfile(profile);

        airborneAgent.Motor.Handler.Platform.ActivePlatform.Should().BeNull();
        airborneAgent.Motor.IsInGas.Should().BeTrue();
    }

    [Fact]
    public void SetLocomotionProfile_ShouldRejectNullProfile()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        agent.Motor.Invoking(motor => motor.SetLocomotionProfile(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetLocomotionProfile_ShouldAllowUninitializedMotorShell()
    {
        var motor = NavMotor.CreateUninitialized(TestWorld.Context);
        var profile = LocomotionProfile.CreateCoreOnly();

        motor.Invoking(m => m.SetLocomotionProfile(profile))
            .Should().NotThrow();

        Assert.NotNull(motor.Handler.Platform);
        motor.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void SetLocomotionProfile_ShouldKeepRequiredPlatformModule_WhenGroundedProfileUsesCoreProfile()
    {
        var groundedAgent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Solid,
            profile: LocomotionProfile.CreateCoreOnly());

        groundedAgent.Motor.Invoking(motor => motor.SetLocomotionProfile(LocomotionProfile.CreateCoreOnly()))
            .Should().NotThrow();

        groundedAgent.Motor.IsOnSolid.Should().BeTrue();
        Assert.NotNull(groundedAgent.Motor.Handler.Platform);
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
                CeilingLevel = Fixed64.MaxValue
            },
            agent.GetFootPosition());

        landed.Should().BeFalse();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void FinalizeTraversal_ShouldFireWaterBreachStop_WhenJumpingIntoLiquid()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        bool stoppedWaterBreach = false;
        agent.Motor.Events.OnStopWaterBreach += () => stoppedWaterBreach = true;

        OpenTraversal(agent);
        agent.Motor.Handler.Jump!.IsJumping = true;
        agent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Liquid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MaxValue
            },
            newFootPosition: null);

        stoppedWaterBreach.Should().BeTrue();
    }

    [Fact]
    public void FinalizeTraversal_ShouldFireJumpAndLandingEvents_WhenExitingGasIntoSolid()
    {
        var jumpingAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        bool stoppedJump = false;
        jumpingAgent.Motor.Events.OnStopJump += () => stoppedJump = true;

        OpenTraversal(jumpingAgent);
        jumpingAgent.Motor.Handler.Jump!.IsJumping = true;
        jumpingAgent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });

        jumpingAgent.Motor.FinalizeTraversal(
            jumpingAgent.Position,
            jumpingAgent.LastPosition,
            jumpingAgent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MaxValue,
                GroundState = new GroundCondition
                {
                    Platform = new PlatformSnapshot(1, Fixed4x4.Identity)
                }
            },
            jumpingAgent.GetFootPosition());

        stoppedJump.Should().BeTrue();

        var fallingAgent = MockMotorAgentTestFactory.CreateFallingAgent(surfaceLevel: Fixed64.Zero);
        bool landed = false;
        fallingAgent.Motor.Events.OnLandedFall += () => landed = true;

        OpenTraversal(fallingAgent);
        fallingAgent.Motor.FinalizeTraversal(
            fallingAgent.Position,
            fallingAgent.LastPosition,
            fallingAgent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MaxValue,
                GroundState = new GroundCondition
                {
                    Platform = new PlatformSnapshot(1, Fixed4x4.Identity)
                }
            },
            fallingAgent.GetFootPosition());

        landed.Should().BeTrue();
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
                CeilingLevel = Fixed64.MaxValue,
                GroundState = new GroundCondition
                {
                    Platform = new PlatformSnapshot(1, platformTransform),
                    MotionTransferState = MotionTransfer.InitTransfer
                }
            },
            agent.GetFootPosition());

        agent.Motor.Handler.Move.FrameVelocity.X.Should().Be(-(Fixed64)2);
        agent.Motor.Handler.Platform.HoldPlatform.Should().BeNull();
    }

    [Fact]
    public void FinalizeTraversal_ShouldHoldNewPlatform_WhenLandingOnDifferentPlatform()
    {
        Fixed4x4 priorPlatform = MockMotorAgentTestFactory.CreatePlatformTransform();
        Fixed4x4 newPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 0));
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(platformMatrix: priorPlatform);

        OpenTraversal(agent);

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Solid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MaxValue,
                GroundState = new GroundCondition
                {
                    Platform = new PlatformSnapshot(2, newPlatform),
                    MotionTransferState = MotionTransfer.InitTransfer
                }
            },
            agent.GetFootPosition());

        agent.Motor.Handler.Platform!.HoldPlatform.Should().Be(new PlatformSnapshot(2, newPlatform));
    }

    [Fact]
    public void FinalizeTraversal_ShouldIgnoreCall_WhenMotorWasNeverInitialized()
    {
        var motor = NavMotor.CreateUninitialized(TestWorld.Context);

        motor.FinalizeTraversal(
            Vector3d.Zero,
            Vector3d.Zero,
            FixedQuaternion.Identity,
            new TrekCondition { Medium = TraversalMedium.Gas },
            newFootPosition: null);

        motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void FinalizeTraversal_ShouldClampJumpingVelocity_WhenCeilingIsReached()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        OpenTraversal(agent);
        agent.Motor.Handler.Jump!.IsJumping = true;
        agent.Motor.Handler.Jump.IsHoldingJump = true;
        agent.Motor.Handler.Move.FrameVelocity = new Vector3d(0, 4, 0);

        agent.Motor.FinalizeTraversal(
            new Vector3d(0, 2, 0),
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Gas,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.One
            },
            agent.GetFootPosition());

        agent.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.Zero);
        agent.Motor.Handler.Jump.IsJumping.Should().BeFalse();
        agent.Motor.Handler.Jump.IsHoldingJump.Should().BeFalse();
    }

    [Fact]
    public void PrivateForceHelpers_ShouldRespectGroundFriction_AndAirControlGuards()
    {
        var groundedAgent = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: (Fixed64)0.25f);
        ReflectionUtility.SetPrivateField(groundedAgent.Motor, "_forceOutput", new Vector3d(4, 0, 0));

        ReflectionUtility.InvokePrivate<bool>(groundedAgent.Motor, "TryApplyStationaryGroundFriction", Vector3d.Zero)
            .Should().BeTrue();
        ReflectionUtility.GetPrivateField<Vector3d>(groundedAgent.Motor, "_forceOutput").Should().Be(new Vector3d(3, 0, 0));

        var airborneAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        airborneAgent.Motor.Handler.IsInControl = false;
        ReflectionUtility.SetPrivateField(airborneAgent.Motor, "_forceOutput", Vector3d.Zero);

        ReflectionUtility.InvokePrivate<object>(airborneAgent.Motor, "ApplyDesiredVelocity", Vector3d.Right);

        ReflectionUtility.GetPrivateField<Vector3d>(airborneAgent.Motor, "_forceOutput").Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void PrivateStateHandlers_ShouldRaiseDrowning_AndClearFallWhileFlying()
    {
        var swimmingAgent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        swimmingAgent.Motor.Handler.Water!.CanDrown = true;
        swimmingAgent.Motor.Handler.Water.HoldBreathTime = Fixed64.Zero;

        Fixed64 drowningTime = Fixed64.Zero;
        swimmingAgent.Motor.Events.OnDrowning += time => drowningTime = time;

        ReflectionUtility.InvokePrivate<object>(swimmingAgent.Motor, "HandleSwimState", new Vector3d(0, -1, 0));

        drowningTime.Should().BeGreaterThan(Fixed64.Zero);

        var flyingAgent = MockMotorAgentTestFactory.CreateFallingAgent();
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fall.IsFalling = true;

        ReflectionUtility.InvokePrivate<object>(flyingAgent.Motor, "HandleFlightState");

        flyingAgent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void PrivateJumpAndFlightHelpers_ShouldRejectWaterJumpWithoutBreach_AndPreserveHeadroom()
    {
        var waterAgent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        waterAgent.Motor.Handler.Water!.CanBreachWater = false;

        ReflectionUtility.InvokePrivate<bool>(waterAgent.Motor, "CanApplyJumpForce", new TrekRequest
        {
            IsRequestingJump = true
        }).Should().BeFalse();

        var ceilingSafeAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        ceilingSafeAgent.Motor.Handler.Move.FrameVelocity = new Vector3d(0, 4, 0);
        ceilingSafeAgent.Motor.Handler.Jump!.IsJumping = true;
        ceilingSafeAgent.Motor.Handler.Jump.IsHoldingJump = true;
        ceilingSafeAgent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Gas,
            CeilingLevel = (Fixed64)5
        });

        ReflectionUtility.InvokePrivate<object>(ceilingSafeAgent.Motor, "CheckJumpStatus", new Vector3d(0, 2, 0));

        ceilingSafeAgent.Motor.Handler.Move.FrameVelocity.Y.Should().Be((Fixed64)4);
        ceilingSafeAgent.Motor.Handler.Jump.IsJumping.Should().BeTrue();

        TrekRequest request = new()
        {
            IsRequestingFlight = true
        };

        var unknownMediumAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);
        ReflectionUtility.InvokePrivate<object>(unknownMediumAgent.Motor, "UpdateFlightState", request);
        unknownMediumAgent.Motor.Handler.Fly!.IsFlying.Should().BeFalse();
    }

    [Fact]
    public void PrivateJumpAndGroundHelpers_ShouldPreserveExistingJumpDirection_AndIgnoreZeroGroundFrictionForce()
    {
        var jumpingAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        jumpingAgent.Motor.Handler.Jump!.IsJumping = true;
        jumpingAgent.Motor.Handler.Jump.FrameJumpDirection = Vector3d.Right;

        ReflectionUtility.InvokePrivate<object>(jumpingAgent.Motor, "EnsureJumpDirectionInitialized");

        jumpingAgent.Motor.Handler.Jump.FrameJumpDirection.Should().Be(Vector3d.Right);

        var groundedAgent = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: (Fixed64)0.25f);
        ReflectionUtility.SetPrivateField(groundedAgent.Motor, "_forceOutput", Vector3d.Zero);

        ReflectionUtility.InvokePrivate<bool>(groundedAgent.Motor, "TryApplyStationaryGroundFriction", Vector3d.Zero)
            .Should().BeFalse();
    }

    [Fact]
    public void StateAndFlightHelpers_ShouldCoverUnknownTransitions_AndDisabledFlightModules()
    {
        var unknownAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);
        unknownAgent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, Fixed4x4.Identity)
            }
        });
        unknownAgent.Motor.StateChanged.Should().BeFalse();

        unknownAgent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        unknownAgent.Motor.StateChanged.Should().BeTrue();

        var disabledFlightAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        disabledFlightAgent.Motor.Handler.Fly!.IsEnabled = false;
        disabledFlightAgent.Motor.Handler.Fly.IsFlying = true;

        TrekRequest request = new()
        {
            Rotation = FixedQuaternion.Identity,
            Direction = Vector3d.Right,
            Rate = TrekRate.Moderate
        };

        ReflectionUtility.InvokePrivate<Vector3d>(disabledFlightAgent.Motor, "GetFlightVelocity", request)
            .Should().Be(Vector3d.Zero);
        disabledFlightAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void FlightSpeedHelpers_ShouldReturnZero_WhenInputHasNoHorizontalComponent()
    {
        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.CanFly = true;
        flyingAgent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)6;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)6;

        Fixed64 horizontalSpeed = ReflectionUtility.InvokePrivate<Fixed64>(
            flyingAgent.Motor,
            "GetFlightHorizontalSpeed",
            Vector3d.Up,
            TrekRate.Fast);

        horizontalSpeed.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void FlightAndAccelerationHelpers_ShouldCoverAscendAndStationaryBranches()
    {
        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.CanFly = true;
        flyingAgent.Motor.Handler.Fly.MaxAscendSpeed = (Fixed64)8;
        flyingAgent.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)6;
        flyingAgent.Motor.Handler.Move.MaxSlowSpeed = (Fixed64)3;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)6;

        TrekRequest upwardRequest = new()
        {
            Rotation = FixedQuaternion.Identity,
            Direction = Vector3d.Up,
            Rate = TrekRate.Slow
        };

        ReflectionUtility.InvokePrivate<Vector3d>(flyingAgent.Motor, "GetFlightVelocity", upwardRequest).Y
            .Should().Be((Fixed64)4);

        ReflectionUtility.InvokePrivate<Fixed64>(
            flyingAgent.Motor,
            "GetFlightHorizontalSpeed",
            Vector3d.Right,
            TrekRate.Stationary).Should().Be(Fixed64.Zero);

        var airborneAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        airborneAgent.Motor.GetMaxAcceleration().Should().Be(airborneAgent.Motor.Handler.Move.MaxAirAcceleration);
    }

    [Fact]
    public void FlightAndAccelerationHelpers_ShouldCoverDisabledHorizontalSpeed_AndFallingAcceleration()
    {
        var disabledFlightAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        disabledFlightAgent.Motor.Handler.Fly!.CanFly = false;

        ReflectionUtility.InvokePrivate<Fixed64>(
            disabledFlightAgent.Motor,
            "GetFlightHorizontalSpeed",
            Vector3d.Right,
            TrekRate.Fast).Should().Be(Fixed64.Zero);

        var fallingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        fallingAgent.Motor.Handler.Fall.IsFalling = true;
        fallingAgent.Motor.GetMaxAcceleration().Should().Be(fallingAgent.Motor.Handler.Move.MaxAirAcceleration);
    }

    [Fact]
    public void JumpAndStateHelpers_ShouldCoverAffordabilityGuard_AndSteepJumpDirection()
    {
        var jumpAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        ReflectionUtility.InvokePrivate<bool>(jumpAgent.Motor, "CanApplyJumpForce", new TrekRequest
        {
            IsRequestingJump = true,
            CanAffordJump = false
        }).Should().BeFalse();

        Fixed4x4 steepPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles((Fixed64)1.25f, Fixed64.Zero, Fixed64.Zero));
        var steepAgent = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: steepPlatform);
        steepAgent.Motor.Handler.Move.SlopeLimit = Fixed64.One;
        steepAgent.Motor.Handler.Jump!.SteepPerpendicularJumpAmount = Fixed64.One;
        ReflectionUtility.SetPrivateField(steepAgent.Motor, "<FrameSlopeAngle>k__BackingField", (Fixed64)2);

        ReflectionUtility.InvokePrivate<object>(steepAgent.Motor, "EnsureJumpDirectionInitialized");

        steepAgent.Motor.Handler.Jump.FrameJumpDirection.Should().Be(
            Vector3d.Slerp(Vector3d.Up, steepAgent.Motor.CurrentState.SurfaceNormal, Fixed64.One));
    }

    [Fact]
    public void JumpAndFlightHelpers_ShouldCoverIdleJumpRejection_AndLiquidFlightRejection()
    {
        var jumpAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        ReflectionUtility.InvokePrivate<bool>(jumpAgent.Motor, "CanApplyJumpForce", new TrekRequest
        {
            IsRequestingJump = false
        }).Should().BeFalse();

        var liquidAgent = MockMotorAgentTestFactory.CreateWaterAgent();
        liquidAgent.Motor.Handler.Fly!.IsFlying = true;

        ReflectionUtility.InvokePrivate<object>(liquidAgent.Motor, "UpdateFlightState", new TrekRequest
        {
            IsRequestingFlight = true
        });

        liquidAgent.Motor.Handler.Fly.IsFlying.Should().BeFalse();
    }

    [Fact]
    public void EnvironmentalAndPlatformHelpers_ShouldCoverUnknownMediumNoOp_AndPlatformRelease()
    {
        var unknownAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Unknown);
        ReflectionUtility.SetPrivateField(unknownAgent.Motor, "_forceOutput", Vector3d.Right);

        ReflectionUtility.InvokePrivate<object>(unknownAgent.Motor, "ApplyEnvironmentalForces");

        ReflectionUtility.GetPrivateField<Vector3d>(unknownAgent.Motor, "_forceOutput").Should().Be(Vector3d.Right);

        Fixed4x4 activePlatform = MockMotorAgentTestFactory.CreatePlatformTransform();
        Fixed4x4 heldPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 0));
        var platformAgent = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: activePlatform,
            motionTransfer: MotionTransfer.InitTransfer);
        platformAgent.Motor.Handler.Platform!.SetHoldPlatform(new PlatformSnapshot(2, heldPlatform));
        platformAgent.Motor.Handler.Platform.PlatformVelocity = new Vector3d(2, 0, 0);
        platformAgent.Motor.Handler.Move.FrameVelocity = Vector3d.Zero;

        ReflectionUtility.InvokePrivate<object>(platformAgent.Motor, "HandlePlatformTransitions");
        ReflectionUtility.InvokePrivate<object>(platformAgent.Motor, "HandlePlatformTransitions");

        platformAgent.Motor.Handler.Move.FrameVelocity.Should().Be(new Vector3d(-2, 0, 0));
    }

    [Fact]
    public void GasExitAndGroundFrictionHelpers_ShouldCoverLiquidNonJumpExit_AndFlyingFrictionBypass()
    {
        var liquidExitAgent = MockMotorAgentTestFactory.CreateWaterAgent();
        bool landed = false;
        liquidExitAgent.Motor.Events.OnLandedFall += () => landed = true;

        ReflectionUtility.InvokePrivate<object>(liquidExitAgent.Motor, "HandleGasExitTransition");

        landed.Should().BeFalse();

        var flyingGroundAgent = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: (Fixed64)0.25f);
        flyingGroundAgent.Motor.Handler.Fly!.IsFlying = true;
        ReflectionUtility.SetPrivateField(flyingGroundAgent.Motor, "_forceOutput", new Vector3d(4, 0, 0));

        ReflectionUtility.InvokePrivate<bool>(flyingGroundAgent.Motor, "TryApplyStationaryGroundFriction", Vector3d.Zero)
            .Should().BeFalse();
        ReflectionUtility.GetPrivateField<Vector3d>(flyingGroundAgent.Motor, "_forceOutput").Should().Be(new Vector3d(4, 0, 0));
    }

    [Fact]
    public void FallAndGroundHelpers_ShouldCoverSteepSurfaceStart_AndProjectedSlopeAdjustment()
    {
        Fixed4x4 steepPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles((Fixed64)1.25f, Fixed64.Zero, Fixed64.Zero));
        var steepAgent = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: steepPlatform);
        steepAgent.Motor.Handler.Move.SlopeLimit = Fixed64.One;
        ReflectionUtility.SetPrivateField(steepAgent.Motor, "<FrameSlopeAngle>k__BackingField", (Fixed64)2);
        ReflectionUtility.SetPrivateField(steepAgent.Motor, "_forceOutput", Vector3d.Down);

        ReflectionUtility.InvokePrivate<object>(steepAgent.Motor, "TryStartFall", steepAgent.Position);

        steepAgent.Motor.Handler.Fall.IsFalling.Should().BeTrue();

        var slopedAgent = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: steepPlatform);
        ReflectionUtility.SetPrivateField(slopedAgent.Motor, "<FrameSlopeAngle>k__BackingField", -(Fixed64)2);

        Vector3d adjustedVelocity = ReflectionUtility.InvokePrivate<Vector3d>(
            slopedAgent.Motor,
            "ApplyGroundVelocityConstraints",
            Vector3d.Forward * (Fixed64)3);

        adjustedVelocity.Y.Should().BeLessThan(Fixed64.Zero);
    }

    [Fact]
    public void PrivateEnvironmentalAndFallHelpers_ShouldCoverDisabledSwim_AndFallEvents()
    {
        var swimmingAgent = MockMotorAgentTestFactory.CreateWaterAgent(surfaceLevel: Fixed64.Zero);
        swimmingAgent.Motor.Handler.Water!.IsEnabled = false;
        swimmingAgent.Motor.Handler.Move.FrameVelocity = new Vector3d(0, -2, 0);
        ReflectionUtility.SetPrivateField(swimmingAgent.Motor, "_forceOutput", Vector3d.Zero);

        ReflectionUtility.InvokePrivate<object>(swimmingAgent.Motor, "ApplyEnvironmentalForces");

        ReflectionUtility.GetPrivateField<Vector3d>(swimmingAgent.Motor, "_forceOutput").Y
            .Should().BeLessThan((Fixed64)(-2));

        var inactiveFallAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        inactiveFallAgent.Motor.Handler.Fall.IsFalling = false;
        ReflectionUtility.InvokePrivate<object>(inactiveFallAgent.Motor, "ClearFallState");
        inactiveFallAgent.Motor.Handler.Fall.IsFalling.Should().BeFalse();

        var activeFallAgent = MockMotorAgentTestFactory.CreateFallingAgent();
        activeFallAgent.Motor.Handler.Fall.FallStart = (Fixed64)10;
        activeFallAgent.Motor.Handler.Fall.MaxFallHeight = Fixed64.One;

        bool maxFallTriggered = false;
        bool startFallTriggered = false;
        activeFallAgent.Motor.Events.OnMaxFallHeightReached += () => maxFallTriggered = true;
        activeFallAgent.Motor.Events.OnStartFall += () => startFallTriggered = true;

        ReflectionUtility.InvokePrivate<object>(activeFallAgent.Motor, "UpdateActiveFallState", new Vector3d(0, 0, 0));
        maxFallTriggered.Should().BeTrue();

        var startFallAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        ReflectionUtility.SetPrivateField(startFallAgent.Motor, "_forceOutput", Vector3d.Down);
        startFallAgent.Motor.Events.OnStartFall += () => startFallTriggered = true;

        ReflectionUtility.InvokePrivate<object>(startFallAgent.Motor, "TryStartFall", startFallAgent.Position);

        startFallTriggered.Should().BeTrue();
        startFallAgent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void FallHelpers_ShouldClearActiveFallState_AndStartCooldownFromJumpedFall()
    {
        var fallingAgent = MockMotorAgentTestFactory.CreateFallingAgent();
        fallingAgent.Motor.Handler.Fall.IsFalling = true;

        ReflectionUtility.InvokePrivate<object>(fallingAgent.Motor, "ClearFallState");

        fallingAgent.Motor.Handler.Fall.IsFalling.Should().BeFalse();

        var jumpAgent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        jumpAgent.Motor.SyncTraversalState(new TrekCondition { Medium = TraversalMedium.Gas });
        jumpAgent.Motor.Handler.Jump!.MaxJumpCount = 2;
        jumpAgent.Motor.Handler.Jump.RegisterJump();
        ReflectionUtility.SetPrivateField(jumpAgent.Motor, "_forceOutput", Vector3d.Down);

        ReflectionUtility.InvokePrivate<object>(jumpAgent.Motor, "TryStartFall", jumpAgent.Position);

        jumpAgent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        jumpAgent.Motor.Handler.Jump.IsCoolingDown.Should().BeTrue();
    }

    [Fact]
    public void HandleFallState_ShouldRespectDisabledAndActiveBranches()
    {
        var disabledFallAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        disabledFallAgent.Motor.Handler.Fall.IsEnabled = false;
        ReflectionUtility.SetPrivateField(disabledFallAgent.Motor, "_forceOutput", Vector3d.Down);

        ReflectionUtility.InvokePrivate<object>(disabledFallAgent.Motor, "HandleFallState", disabledFallAgent.Position);

        disabledFallAgent.Motor.Handler.Fall.IsFalling.Should().BeFalse();

        var activeFallAgent = MockMotorAgentTestFactory.CreateFallingAgent();
        activeFallAgent.Motor.Handler.Fall.FallStart = (Fixed64)2;

        ReflectionUtility.InvokePrivate<object>(activeFallAgent.Motor, "HandleFallState", new Vector3d(0, 3, 0));

        activeFallAgent.Motor.Handler.Fall.FallStart.Should().Be((Fixed64)3);
    }

    [Fact]
    public void ApplyGroundVelocityConstraints_ShouldPreserveVelocity_WhenSolidStateHasNoGroundSample()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent();
        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            GroundState = null
        });

        Vector3d constrainedVelocity = ReflectionUtility.InvokePrivate<Vector3d>(
            agent.Motor,
            "ApplyGroundVelocityConstraints",
            new Vector3d(3, 0, 4));

        constrainedVelocity.Should().Be(new Vector3d(3, 0, 4));
    }

    [Fact]
    public void ApplyGroundVelocityConstraints_ShouldSkipReprojection_WhenSliding()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: (Fixed64)0.25f);
        agent.Motor.Handler.Slide!.IsSliding = true;
        Fixed4x4 slopedPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles((Fixed64)0.25f, Fixed64.Zero, Fixed64.Zero));
        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, slopedPlatform),
                SurfaceFriction = (Fixed64)0.25f
            }
        });

        Vector3d constrainedVelocity = ReflectionUtility.InvokePrivate<Vector3d>(
            agent.Motor,
            "ApplyGroundVelocityConstraints",
            new Vector3d(4, 0, 0));

        constrainedVelocity.Should().Be(new Vector3d(3, 0, 0));
    }

    [Fact]
    public void FlightVelocity_ShouldSupportDescendingInputWithoutHorizontalMovement()
    {
        var flyingAgent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        flyingAgent.Motor.Handler.Fly!.IsFlying = true;
        flyingAgent.Motor.Handler.Fly.CanFly = true;
        flyingAgent.Motor.Handler.Fly.MaxDescendSpeed = (Fixed64)6;
        flyingAgent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)10;

        TrekRequest request = new()
        {
            Rotation = FixedQuaternion.Identity,
            Direction = Vector3d.Down,
            Rate = TrekRate.Moderate
        };

        Vector3d velocity = ReflectionUtility.InvokePrivate<Vector3d>(flyingAgent.Motor, "GetFlightVelocity", request);
        Fixed64 expectedDescentSpeed = -flyingAgent.Motor.Handler.Fly.MaxDescendSpeed
            * (flyingAgent.Motor.Handler.Move.MaxModerateSpeed / flyingAgent.Motor.Handler.Move.MaxFastSpeed);

        velocity.Should().Be(new Vector3d(Fixed64.Zero, expectedDescentSpeed, Fixed64.Zero));
    }

    [Fact]
    public void JsonRoundTrip_ShouldHydrateMissingCurrentState_ForUninitializedMotors()
    {
        var source = NavMotor.CreateUninitialized(TestWorld.Context, new LocomotionHandler(LocomotionProfile.CreateCoreOnly()));

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = NavMotor.CreateUninitialized(TestWorld.Context);
        JsonRecordSerializer.Populate(target, json);

        target.IsInitialized.Should().BeFalse();
        Assert.NotNull(target.CurrentState);
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
}
