using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class FallLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_FallingAgent_When_JumpIsTriggered_Then_ShouldNotJump()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateFallingAgent();

        Vector3d expectedVelocity = Vector3d.Down;
        expectedVelocity.Y += -agent.Motor.Handler.Forces.GravityForce
            * TestWorld.Context.DeltaTime;

        agent.FrameRequest.IsRequestingJump = true;

        // Act
        agent.Simulate();

        // Assert
        agent.Motor.Handler.Move.FrameVelocity.Should()
            .BeApproximately(expectedVelocity, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_FallingAgent_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateFallingAgent();

        // Act - First Frame (Falling)
        TestWorld.Context.Simulate();
        agent.Simulate();

        // Assert
        agent.Motor.IsInGas.Should().BeTrue();

        // Simulate hitting the ground before the next frame
        agent.FrameCondition = new()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition
            {
                Platform = default
            }
        };

        // 2nd Frame
        TestWorld.Context.Simulate();

        // Act - Second Frame (After Ground Contact)
        agent.Simulate();

        // Assert
        agent.Motor.IsOnSolid.Should().BeTrue();
    }

    [Fact]
    public void Given_AirborneAgent_When_SimulatedOverMultipleFrames_Then_VelocityShouldMatchGravity()
    {
        // Arrange
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 100, 0),
            startingMedium: TraversalMedium.Gas
        );
        Vector3d expectedVelocity = Vector3d.Zero;  // store impulse-based velocity change per frame

        // Act - Simulate falling for 5 frames
        for (int i = 0; i < 5; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();

            // Calculate expected velocity update from gravity impulse
            expectedVelocity.Y += -agent.Motor.Handler.Forces.GravityForce * TestWorld.Context.DeltaTime;
        }

        // Assert
        agent.Motor.Handler.Move.FrameVelocity.Should().BeApproximately(
            expectedVelocity,
            Fixed64.Epsilon);
    }

    [Fact]
    public void Given_AgentInAir_When_NoMovement_Then_ShouldFallNaturally()
    {
        var initialPosition = new Vector3d(0, 10, 0);
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startPosition: initialPosition, startingMedium: TraversalMedium.Gas);

        for (int i = 0; i < 20; i++) // Simulate multiple frames
        {
            agent.Simulate();
        }

        agent.Position.Y.Should().BeLessThan(initialPosition.Y); // Should be falling
    }

    [Fact]
    public void Given_AgentInAir_When_MovesForward_Then_ShouldStillBeAffectedByGravity()
    {
        var initialPosition = new Vector3d(0, 10, 0);
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(startPosition: initialPosition);

        for (int i = 0; i < 20; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.Direction = Vector3d.Right;
            agent.FrameRequest.Rate = TrekRate.Moderate;
            agent.Simulate();
        }

        agent.Position.Y.Should().BeLessThan(initialPosition.Y); // Gravity should still apply
        agent.Position.X.Should().BeGreaterThan(Fixed64.Zero); // Should also move forward
    }

    [Fact]
    public void Given_AgentFallsFar_When_Lands_Then_ShouldTriggerMaxFallHeightEvent()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        agent.Motor.Handler.Fall.MaxFallHeight = Fixed64.One;

        bool eventCalled = false;
        agent.Motor.Events.OnMaxFallHeightReached += () => eventCalled = true;

        while (!agent.Motor.IsOnSolid)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        eventCalled.Should().BeTrue();
    }

    [Fact]
    public void Given_AgentFallsAndLands_When_FallHeightIsValid_Then_ShouldCallOnStopFallWithHeight()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 10, 0), startingMedium: TraversalMedium.Gas);

        Fixed64 fallHeight = Fixed64.Zero;
        agent.Motor.Events.OnStopFall += (height) => fallHeight = height;

        while (!agent.Motor.IsOnSolid)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        fallHeight.Should().BeGreaterThan(Fixed64.One);
    }

    [Fact]
    public void Given_AgentSlidesDownhill_When_SlopeIsShallow_Then_ShouldNotStartFalling()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)10);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(0, 0, 0),
            platformMatrix: platform,
            surfaceNormal: platform.Up);

        agent.Motor.Handler.Move.SlopeLimit = (Fixed64)45;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
    }

    [Fact]
    public void Given_AgentSlidesDownhill_When_SlopeIsSteep_Then_ShouldStartFalling()
    {
        var slopeAngle = FixedMath.DegToRad((Fixed64)60);
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(
            platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

        var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(0, 0, 0),
            platformMatrix: platform,
            surfaceNormal: platform.Up);

        agent.Motor.Handler.Move.SlopeLimit = (Fixed64)45;

        for (int i = 0; i < 2; i++)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        var motor = TestRequire.NotNull(agent.Motor);
        TestRequire.NotNull(motor.Handler.Slide).IsSliding.Should().BeTrue();
        TestRequire.NotNull(motor.Handler.Fall).IsFalling.Should().BeTrue();
    }

    [Fact]
    public void Given_AgentStartsFallingMidJump_When_StillRising_Then_ShouldNotTriggerFallStart()
    {
        var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        bool fallTriggered = false;
        agent.Motor.Events.OnStartFall += () => fallTriggered = true;

        // Start jump

        agent.FrameRequest.IsRequestingJump = true;

        TestWorld.Context.Simulate();
        agent.Simulate();

        // Simulate a few frames of upward motion
        for (int i = 0; i < 13; i++)
        {
            TestWorld.Context.Simulate();
            agent.FrameRequest.IsRequestingJump = true;
            agent.Simulate();
        }

        fallTriggered.Should().BeFalse();
    }

    [Fact]
    public void Given_AgentFallsZeroDistance_When_Lands_Then_FallHeightShouldBeZero()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 0, 0),
            startingMedium: TraversalMedium.Gas);

        agent.FrameCondition.Medium = TraversalMedium.Solid;
        agent.FrameCondition.SurfaceLevel = Fixed64.Zero;

        TestWorld.Context.Simulate();
        agent.Simulate();

        agent.Motor.Handler.Fall.FallHeight.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_AgentFalls_When_Lands_Then_FallStartShouldBeGreaterThanFallEnd()
    {
        var agent = MockMotorAgentTestFactory.CreateFallingAgent(new(0, 20, 0));

        bool eventCalled = false;
        agent.Motor.Events.OnStopFall += (height) =>
        {
            var fallLocomotion = agent.Motor.Handler.Fall;
            fallLocomotion.FallStart.Should().BeGreaterThan(fallLocomotion.FallEnd);
            fallLocomotion.FallHeight.Should().Be(fallLocomotion.FallStart - fallLocomotion.FallEnd);
            eventCalled = true;
        };

        while (!agent.Motor.IsOnSolid)
        {
            TestWorld.Context.Simulate();
            agent.Simulate();
        }

        eventCalled.Should().BeTrue();
    }

    [Fact]
    public void Given_AgentFalls_When_Disabled_Then_FallStateShouldReset()
    {
        var agent = MockMotorAgentTestFactory.CreateMockAgent(
            new(0, 10, 0),
            startingMedium: TraversalMedium.Gas);

        agent.Motor.Handler.Fall.IsFalling = true;
        agent.Motor.Handler.Fall.IsControlledDescending = true;
        agent.Motor.Handler.Fall.FallStart = (Fixed64)10;

        agent.Motor.Handler.Fall.IsEnabled = false;

        agent.Motor.Handler.Fall.IsFalling.Should().BeFalse();
        agent.Motor.Handler.Fall.IsControlledDescending.Should().BeFalse();
        agent.Motor.Handler.Fall.FallStart.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutFalling_When_CalculatingMaxSpeed_Then_ControlMultiplierShouldApply()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();

        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)4;
        scout.Motor.Handler.Fall.FallControlMultiplier = (Fixed64)0.25;

        var speed = scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate);

        speed.Should().BeApproximately((Fixed64)1, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_TooFastFallingScout_When_ControlledDescentIsRequested_Then_ShouldDecelerateTowardTerminalWithoutLeavingFall()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: new Vector3d(Fixed64.Zero, (Fixed64)(-10), Fixed64.Zero));
        FallLocomotion fall = scout.Motor.Handler.Fall;
        fall.CanControlDescent = true;
        fall.ControlledDescentTerminalSpeed = (Fixed64)3;
        fall.ControlledDescentDeceleration = (Fixed64)8;
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        fall.IsFalling.Should().BeTrue();
        fall.IsControlledDescending.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.FromFraction(-39, 4));
    }

    [Fact]
    public void Given_CoreOnlyFallingScout_When_ControlledDescentIsRequested_Then_ShouldActivateWithoutOptionalLocomotions()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: new Vector3d(Fixed64.Zero, (Fixed64)(-4), Fixed64.Zero),
            profile: LocomotionProfile.CreateCoreOnly());
        FallLocomotion fall = ConfigureControlledDescent(
            scout,
            terminalSpeed: (Fixed64)2,
            deceleration: (Fixed64)8);
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        scout.Motor.Handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        fall.IsControlledDescending.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.FromFraction(-15, 4));
    }

    [Fact]
    public void Given_FallingScout_When_ControlledDescentReleasesAndRedeploys_Then_ShouldResumeGravityBeforeReactivating()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: new Vector3d(Fixed64.Zero, -Fixed64.One, Fixed64.Zero));
        FallLocomotion fall = scout.Motor.Handler.Fall;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        fall.CanControlDescent = true;
        fall.ControlledDescentTerminalSpeed = Fixed64.One;
        fall.ControlledDescentDeceleration = (Fixed64)8;

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();
        fall.IsControlledDescending.Should().BeTrue();

        scout.Simulate();

        fall.IsControlledDescending.Should().BeFalse();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.FromFraction(-5, 4));

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        fall.IsFalling.Should().BeTrue();
        fall.IsControlledDescending.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(-Fixed64.One);
    }

    [Fact]
    public void Given_ControlledDescendingScout_When_CalculatingLateralSpeed_Then_ShouldUseTheConfiguredControlMultiplier()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();
        FallLocomotion fall = scout.Motor.Handler.Fall;
        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)4;
        fall.FallControlMultiplier = Fixed64.Zero;
        fall.CanControlDescent = true;
        fall.ControlledDescentTerminalSpeed = Fixed64.One;
        fall.ControlledDescentDeceleration = Fixed64.One;
        fall.ControlledDescentControlMultiplier = Fixed64.Half;
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().BeApproximately((Fixed64)2, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_FallingScoutAtControlledTerminal_When_ControlledDescentIsRequested_Then_ShouldApplyGravityWithoutExceedingTerminalSpeed()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero));
        FallLocomotion fall = ConfigureControlledDescent(scout, terminalSpeed: Fixed64.One, deceleration: (Fixed64)8);
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        fall.IsControlledDescending.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.FromFraction(-3, 4));

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(-Fixed64.One);
    }

    [Fact]
    public void Given_UnexpectedUpwardVelocityWhileFalling_When_ControlledDescentIsRequested_Then_ShouldNeverProducePositiveVerticalVelocity()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: new Vector3d(Fixed64.Zero, (Fixed64)4, Fixed64.Zero));
        FallLocomotion fall = ConfigureControlledDescent(scout, terminalSpeed: Fixed64.One, deceleration: Fixed64.One);

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        fall.IsControlledDescending.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ControlledDescendingScout_When_ControlMultiplierIsOutsideUnitRange_Then_ShouldClampLateralControl()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();
        FallLocomotion fall = ConfigureControlledDescent(scout);
        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)4;
        fall.FallControlMultiplier = Fixed64.One;

        fall.ControlledDescentControlMultiplier = -Fixed64.One;
        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().Be(Fixed64.Zero);

        fall.ControlledDescentControlMultiplier = (Fixed64)2;
        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate)
            .Should().Be((Fixed64)4);
    }

    [Fact]
    public void Given_GasScoutThatHasNotStartedFalling_When_ControlledDescentIsRequested_Then_ShouldNotActivateUntilALaterFallFrame()
    {
        var scout = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: new Vector3d(0, 10, 0),
            startingMedium: TraversalMedium.Gas);
        FallLocomotion fall = ConfigureControlledDescent(scout);
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        fall.IsFalling.Should().BeTrue();
        fall.IsControlledDescending.Should().BeFalse();

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        fall.IsFalling.Should().BeTrue();
        fall.IsControlledDescending.Should().BeTrue();
    }

    [Fact]
    public void Given_FallingScoutOutsideGas_When_ControlledDescentIsRequested_Then_ShouldNotActivate()
    {
        var scout = MockMotorAgentTestFactory.CreateWaterAgent();
        FallLocomotion fall = ConfigureControlledDescent(scout);
        fall.IsFalling = true;
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        scout.Motor.IsInLiquid.Should().BeTrue();
        fall.IsControlledDescending.Should().BeFalse();
    }

    [Fact]
    public void Given_FallingScout_When_FlightAdmitsOnTheSameFrameAsControlledDescent_Then_FlightOwnsTheTransition()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();
        FallLocomotion fall = ConfigureControlledDescent(scout);
        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.FrameRequest.IsRequestingFlight = true;

        scout.Simulate();

        scout.Motor.Handler.Fly!.IsFlying.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();
        fall.IsControlledDescending.Should().BeFalse();
    }

    [Fact]
    public void Given_FallingScout_When_ClimbAdmitsOnTheSameFrameAsControlledDescent_Then_ClimbOwnsTheTransition()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();
        FallLocomotion fall = ConfigureControlledDescent(scout);
        scout.Motor.Handler.Climb!.ClimbResolver = new ControlledDescentClimbResolver();
        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.FrameRequest.IsRequestingClimb = true;

        scout.Simulate();

        scout.Motor.Handler.Climb.IsClimbing.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();
        fall.IsControlledDescending.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Given_FallingScoutWithNonPositiveControlledDescentTuning_When_Requested_Then_ShouldNotActivate(bool invalidTerminalSpeed)
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent();
        FallLocomotion fall = ConfigureControlledDescent(scout);
        if (invalidTerminalSpeed)
            fall.ControlledDescentTerminalSpeed = Fixed64.Zero;
        else
            fall.ControlledDescentDeceleration = Fixed64.Zero;
        scout.FrameRequest.IsRequestingControlledDescent = true;

        scout.Simulate();

        fall.IsFalling.Should().BeTrue();
        fall.IsControlledDescending.Should().BeFalse();
    }

    [Fact]
    public void Given_ControlledDescendingScout_When_Landing_Then_ShouldClearControlledDescentAndPreserveOrdinaryFallEndEvents()
    {
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(startPosition: new Vector3d(0, 5, 0));
        FallLocomotion fall = ConfigureControlledDescent(scout);
        bool landed = false;
        Fixed64 stoppedFallHeight = Fixed64.Zero;
        scout.Motor.Events.OnLandedFall += () => landed = true;
        scout.Motor.Events.OnStopFall += height => stoppedFallHeight = height;

        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();
        fall.IsControlledDescending.Should().BeTrue();

        scout.FrameCondition = new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            CeilingLevel = Fixed64.MaxValue,
            GroundState = new GroundCondition
            {
                Platform = default,
                SurfaceNormal = Vector3d.Up
            }
        };
        scout.FrameRequest.IsRequestingControlledDescent = true;
        scout.Simulate();

        landed.Should().BeTrue();
        stoppedFallHeight.Should().BeGreaterThan(Fixed64.Zero);
        fall.IsFalling.Should().BeFalse();
        fall.IsControlledDescending.Should().BeFalse();
    }

    private static FallLocomotion ConfigureControlledDescent(
        MockMotorAgent scout,
        Fixed64? terminalSpeed = null,
        Fixed64? deceleration = null)
    {
        FallLocomotion fall = scout.Motor.Handler.Fall;
        fall.CanControlDescent = true;
        fall.ControlledDescentTerminalSpeed = terminalSpeed ?? Fixed64.One;
        fall.ControlledDescentDeceleration = deceleration ?? Fixed64.One;
        return fall;
    }

    private sealed class ControlledDescentClimbResolver : IClimbAffordanceResolver
    {
        public bool TryResolveClimbAffordance(
            TrekRequest request,
            TransitState currentState,
            out ClimbAffordanceSnapshot snapshot)
        {
            snapshot = new ClimbAffordanceSnapshot(
                kind: ClimbAffordanceKind.Ladder,
                attachmentPoint: Vector3d.Zero,
                surfaceNormal: Vector3d.Backward,
                upDirection: Vector3d.Up,
                canStartClimb: true,
                canContinueClimb: true,
                allowLateralTraverse: false,
                allowDescent: true);
            return true;
        }
    }
}
