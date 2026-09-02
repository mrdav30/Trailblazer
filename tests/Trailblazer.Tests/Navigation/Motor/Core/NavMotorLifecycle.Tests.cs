using System;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class NavMotorLifecycleTests : IDisposable
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
    public void SyncTraversalState_ShouldUpdateCurrentAndPreviousMediumState()
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
    public void HorizontalSpeed_ShouldApplyFlightAndGroundFallbackRules()
    {
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
    public void HorizontalSpeedAndTraversal_ShouldRejectUnknownRatesAndUnfinalizedFrames()
    {
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalizeTraversal_ShouldResetJumpWhenEnteringLiquid_WithOrWithoutWaterBreachObserver(
        bool observeEvent)
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        bool stoppedWaterBreach = false;
        if (observeEvent)
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

        stoppedWaterBreach.Should().Be(observeEvent);
        agent.Motor.TraversalInProgress.Should().BeFalse();
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
    public void FinalizeTraversal_ShouldClampCoreOnlyUpwardVelocityAtTheCeiling()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startingMedium: TraversalMedium.Gas,
            profile: LocomotionProfile.CreateCoreOnly());
        OpenTraversal(agent);
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

        agent.Motor.Handler.Jump.Should().BeNull();
        agent.Motor.Handler.Move.FrameVelocity.Y.Should().BeLessThanOrEqualTo(Fixed64.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneTraversal_ShouldStartFallingWithoutCoolingAnUnusedJumpModule(
        bool coreOnly)
    {
        LocomotionProfile? profile = coreOnly
            ? LocomotionProfile.CreateCoreOnly()
            : null;
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas,
            profile: profile);
        int starts = 0;
        agent.Motor.Events.OnStartFall += () => starts++;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        starts.Should().Be(1);
        if (coreOnly)
        {
            agent.Motor.Handler.Jump.Should().BeNull();
        }
        else
        {
            JumpLocomotion jump = TestRequire.NotNull(agent.Motor.Handler.Jump);
            jump.JumpCount.Should().Be(0);
            jump.IsCoolingDown.Should().BeFalse();
        }
    }

    [Fact]
    public void AirborneTraversal_ShouldStartJumpCooldownWhenFallingAfterAnAvailableJump()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        JumpLocomotion jump = TestRequire.NotNull(agent.Motor.Handler.Jump);
        jump.MaxJumpCount = 2;
        jump.RegisterJump();
        jump.JumpCount.Should().Be(1);
        jump.IsCoolingDown.Should().BeFalse();

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        jump.JumpCount.Should().Be(1);
        jump.IsCoolingDown.Should().BeTrue();
    }

    [Fact]
    public void AirborneTraversal_ShouldPreserveAnAlreadyRunningJumpCooldownWhenFalling()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        JumpLocomotion jump = TestRequire.NotNull(agent.Motor.Handler.Jump);
        jump.MaxJumpCount = 2;
        jump.RegisterJump();
        jump.StartCooldown();

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        jump.JumpCount.Should().Be(1);
        jump.IsCoolingDown.Should().BeTrue();
        jump.CooldownTimer.Should().BeGreaterThan(Fixed64.Zero,
            "starting a fall must not restart an already advancing cooldown");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalizeTraversal_ShouldClearPlatformHoldWhilePlatformIsDisabledOrInLiquid(
        bool disablePlatform)
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent();
        PlatformLocomotion platform = agent.Motor.Handler.Platform;
        platform.SetHoldPlatform(new PlatformSnapshot(
            2,
            MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(2, 0, 0))));
        platform.IsEnabled = !disablePlatform;
        OpenTraversal(agent);

        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            new TrekCondition
            {
                Medium = disablePlatform ? TraversalMedium.Solid : TraversalMedium.Liquid,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = Fixed64.MaxValue,
                GroundState = disablePlatform ? new GroundCondition() : null
            },
            agent.GetFootPosition());

        platform.IsEnabled.Should().Be(!disablePlatform);
        platform.HoldPlatformFrames.Should().Be(0);
        platform.HoldPlatform.Should().BeNull();
    }

    [Fact]
    public void StationarySolidTraversalWithoutGroundSnapshot_ShouldMatchExplicitZeroFriction()
    {
        var withoutGround = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        withoutGround.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero
        });
        withoutGround.Motor.SetVelocity(Vector3d.Right);
        var zeroFriction = MockMotorAgentTestFactory.CreatePlatformAgent(surfaceFriction: Fixed64.Zero);
        zeroFriction.Motor.SetVelocity(Vector3d.Right);

        withoutGround.Motor.TryTraversal(
                withoutGround.FrameRequest,
                out Vector3d withoutGroundVelocity,
                out Vector3d withoutGroundPosition,
                out FixedQuaternion withoutGroundRotation)
            .Should().BeTrue();
        zeroFriction.Motor.TryTraversal(
                zeroFriction.FrameRequest,
                out Vector3d zeroFrictionVelocity,
                out Vector3d zeroFrictionPosition,
                out FixedQuaternion zeroFrictionRotation)
            .Should().BeTrue();

        withoutGroundVelocity.Should().Be(zeroFrictionVelocity);
        withoutGroundPosition.Should().Be(zeroFrictionPosition);
        withoutGroundRotation.Should().Be(zeroFrictionRotation);
        withoutGround.Motor.AbortTraversalFrame();
        zeroFriction.Motor.AbortTraversalFrame();
    }

    [Fact]
    public void StateAndFlight_ShouldHandleUnknownTransitions_AndDisabledFlightModules()
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

        disabledFlightAgent.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void FlightTraversal_ShouldClearFallAndMoveHorizontally_WhenFlightIsRequested()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 4, 0),
            startingMedium: TraversalMedium.Gas,
            surfaceLevel: -(Fixed64)100);
        agent.Motor.Handler.Fall.IsFalling = true;
        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingFlight = true;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fly!.IsFlying.Should().BeTrue();
        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
        agent.Motor.Handler.Move.FrameVelocity.X.Should().BeGreaterThan(Fixed64.Zero);
        agent.Position.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void GroundedTraversal_ShouldRecoverControlAndClearSlide_WhenFlightStateIsStale()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent();
        agent.Motor.Handler.Fly!.IsFlying = true;
        agent.Motor.Handler.Slide!.IsSliding = true;
        agent.Motor.Handler.IsInControl = false;
        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingFlight = true;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.IsInControl.Should().BeTrue();
        agent.Motor.Handler.Slide.IsSliding.Should().BeFalse();
        agent.Motor.Handler.Fly.IsFlying.Should().BeFalse();
    }

    [Fact]
    public void FinalizeTraversal_ShouldPreserveUpwardMotionBelowFiniteCeiling()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        JumpLocomotion jump = TestRequire.NotNull(agent.Motor.Handler.Jump);
        OpenTraversal(agent);
        jump.IsJumping = true;
        jump.IsHoldingJump = true;

        agent.Motor.FinalizeTraversal(
            new Vector3d(0, 1, 0),
            Vector3d.Zero,
            agent.Rotation,
            new TrekCondition
            {
                Medium = TraversalMedium.Gas,
                SurfaceLevel = Fixed64.Zero,
                CeilingLevel = (Fixed64)2
            },
            agent.GetFootPosition());

        agent.Motor.Handler.Move.FrameVelocity.Y.Should().BeGreaterThan(Fixed64.Zero);
        jump.IsJumping.Should().BeTrue();
        jump.IsHoldingJump.Should().BeTrue();
    }

    [Fact]
    public void FinalizeTraversal_ShouldReleaseChangedPlatformAfterHoldTimeout()
    {
        var agent = MockMotorAgentTestFactory.CreatePlatformAgent();
        PlatformLocomotion platform = agent.Motor.Handler.Platform;
        platform.SetHoldPlatform(new PlatformSnapshot(
            2,
            MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(2, 0, 0))));

        OpenTraversal(agent);
        platform.PlatformVelocity = new Vector3d(2, 0, 0);
        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            agent.FrameCondition,
            agent.GetFootPosition());

        platform.HoldPlatformFrames.Should().Be(1);
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);

        TestWorld.Context.Simulate();
        OpenTraversal(agent);
        platform.PlatformVelocity = new Vector3d(2, 0, 0);
        agent.Motor.FinalizeTraversal(
            agent.Position,
            agent.LastPosition,
            agent.Rotation,
            agent.FrameCondition,
            agent.GetFootPosition());

        platform.HoldPlatformFrames.Should().Be(0);
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(new Vector3d(-2, 0, 0));
    }

    [Fact]
    public void WaterTraversal_ShouldPublishDrowningTimeAfterBreathExpires()
    {
        var agent = MockMotorAgentTestFactory.CreateWaterAgent(
            startPosition: new Vector3d(0, -1, 0),
            surfaceLevel: Fixed64.Zero);
        WaterLocomotion water = TestRequire.NotNull(agent.Motor.Handler.Water);
        water.CanDrown = true;
        water.HoldBreathTime = Fixed64.Zero;
        Fixed64? drowningTime = null;
        agent.Motor.Events.OnDrowning += time => drowningTime = time;

        TestWorld.Context.Simulate();
        agent.Simulate();

        drowningTime.Should().NotBeNull();
        drowningTime!.Value.Should().BeGreaterThan(Fixed64.Zero);
        drowningTime.Value.Should().Be(water.UnderwaterTimer);
    }

    [Fact]
    public void AirborneTraversal_ShouldNotStartFallWhenFallLocomotionIsDisabled()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        int starts = 0;
        agent.Motor.Events.OnStartFall += () => starts++;
        agent.Motor.Handler.Fall.IsEnabled = false;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
        starts.Should().Be(0);
    }

    [Fact]
    public void FlightTraversal_ShouldRemainStationaryWhenSelectedSpeedIsZero()
    {
        Vector3d start = new(0, 10, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: start,
            startingMedium: TraversalMedium.Gas);
        agent.Motor.Handler.Move.MaxSlowSpeed = Fixed64.Zero;
        agent.Motor.Handler.Move.MaxFastSpeed = (Fixed64)4;
        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Slow;
        agent.FrameRequest.IsRequestingFlight = true;

        TestWorld.Context.Simulate();
        agent.Simulate();

        TestRequire.NotNull(agent.Motor.Handler.Fly).IsFlying.Should().BeTrue();
        agent.Position.Should().Be(start);
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void FlightTraversal_ShouldApplyConfiguredAscendSpeedToUpwardIntent()
    {
        Vector3d start = new(0, 10, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: start,
            startingMedium: TraversalMedium.Gas);
        FlyLocomotion fly = TestRequire.NotNull(agent.Motor.Handler.Fly);
        fly.MaxAscendSpeed = (Fixed64)3;
        agent.FrameRequest.Direction = Vector3d.Up;
        agent.FrameRequest.Rate = TrekRate.Fast;
        agent.FrameRequest.IsRequestingFlight = true;

        TestWorld.Context.Simulate();
        agent.Simulate();

        fly.IsFlying.Should().BeTrue();
        agent.Position.Y.Should().BeGreaterThan(start.Y);
        agent.Motor.Handler.Move.FrameVelocity.Y.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void PermanentPlatformTransfer_ShouldPreserveDownhillProjectionWhenCarrierOpposesIntent()
    {
        Fixed4x4 slope = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromAxisAngle(
                Vector3d.Right,
                Fixed64.FromRaw(0x10000000L)));
        var slopedAgent = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: slope,
            motionTransfer: MotionTransfer.PermaTransfer);
        var flatAgent = MockMotorAgentTestFactory.CreatePlatformAgent(
            motionTransfer: MotionTransfer.PermaTransfer);

        slopedAgent.Motor.Handler.Platform.FramePlatformVelocity = Vector3d.Forward * (Fixed64)2;
        flatAgent.Motor.Handler.Platform.FramePlatformVelocity = Vector3d.Forward * (Fixed64)2;
        slopedAgent.FrameRequest.Direction = Vector3d.Backward;
        slopedAgent.FrameRequest.Rate = TrekRate.Slow;
        flatAgent.FrameRequest.Direction = Vector3d.Backward;
        flatAgent.FrameRequest.Rate = TrekRate.Slow;

        slopedAgent.Motor.TryTraversal(
                slopedAgent.FrameRequest,
                out Vector3d slopedVelocity,
                out _,
                out _)
            .Should().BeTrue();
        flatAgent.Motor.TryTraversal(
                flatAgent.FrameRequest,
                out Vector3d flatVelocity,
                out _,
                out _)
            .Should().BeTrue();

        slopedVelocity.Z.Should().BeGreaterThan(Fixed64.Zero);
        flatVelocity.Z.Should().BeGreaterThan(Fixed64.Zero);
        slopedVelocity.Y.Should().BeLessThan(flatVelocity.Y);
        slopedAgent.Motor.AbortTraversalFrame();
        flatAgent.Motor.AbortTraversalFrame();
    }

    [Fact]
    public void SolidTraversal_ShouldUseZeroFrictionWhenGroundSnapshotIsMissing()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        agent.Motor.SyncTraversalState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero
        });
        agent.FrameRequest.Origin = agent.Position;
        agent.FrameRequest.FootPosition = agent.GetFootPosition();
        agent.FrameRequest.Rotation = agent.Rotation;
        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Fast;

        agent.Motor.TryTraversal(agent.FrameRequest, out Vector3d velocityDelta, out _, out _)
            .Should().BeTrue();

        velocityDelta.X.Should().BeGreaterThan(Fixed64.Zero);
        velocityDelta.Z.Should().Be(Fixed64.Zero);
        agent.Motor.AbortTraversalFrame();
    }

    [Fact]
    public void UnknownTraversal_ShouldRemainStillAndOutOfControl()
    {
        Vector3d start = new(3, 4, 5);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: start,
            startingMedium: TraversalMedium.Unknown);
        agent.FrameRequest.Direction = Vector3d.Right;
        agent.FrameRequest.Rate = TrekRate.Fast;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Position.Should().Be(start);
        agent.Motor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);
        agent.Motor.Handler.IsInControl.Should().BeFalse();
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
