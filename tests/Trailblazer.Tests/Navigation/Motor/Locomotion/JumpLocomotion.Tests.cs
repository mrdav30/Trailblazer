using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class JumpLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RegisterJump_ShouldRejectStandaloneLocomotionWithoutWorldContext()
    {
        var jump = new JumpLocomotion();

        jump.Invoking(value => value.RegisterJump())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*TrailblazerWorldContext*");

        jump.JumpCount.Should().Be(0);
        jump.IsJumping.Should().BeFalse();
        jump.IsHoldingJump.Should().BeFalse();
        jump.JumpStartTime.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        scout.FrameRequest = new()
        {
            Origin = scout.Position,
            Rotation = scout.Rotation,
            Direction = Vector3d.Zero,
            Rate = TrekRate.Stationary,
            IsRequestingJump = true
        };

        // Act
        scout.Simulate();

        // Assert
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreateMockAgent(
            startPosition: Vector3d.Up,
            startVelocity: Vector3d.Down,
            startingMedium: TraversalMedium.Gas);

        Vector3d expectedVelocity = Vector3d.Down;
        expectedVelocity.Y += -scout.Motor.Handler.Forces.GravityForce * TestWorld.Context.DeltaTime;

        // Act
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Assert
        scout.Motor.Handler.Move.FrameVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
    }

    [Fact]
    public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        // Act - First Jump
        scout.FrameRequest.IsRequestingJump = true;

        TestWorld.Context.Simulate();
        scout.Simulate();

        Fixed64 expectedJumpFrame = jump.JumpStartTime;

        // Attempt to jump again immediately
        scout.FrameRequest.IsRequestingJump = true;

        TestWorld.Context.Simulate();
        scout.Simulate();

        // Assert
        scout.Motor.IsInGas.Should().BeTrue();
        jump.IsCoolingDown.Should().BeTrue();
        jump.JumpStartTime.Should().Be(expectedJumpFrame);
    }

    [Fact]
    public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        var fall = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Fall);

        // Act - First Jump
        scout.FrameRequest.IsRequestingJump = true;

        TestWorld.Context.Simulate();
        scout.Simulate();

        // Release jump after 2 frames
        for (int i = 0; i < 29; i++)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
        }

        // Act - Simulate next frame
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Assert
        fall.IsFalling.Should().BeFalse();
        jump.IsJumping.Should().BeFalse();
        jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we simulate 31
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
    }

    [Fact]
    public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
    {
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        scout.FrameRequest.CanAffordJump = false;
        scout.FrameRequest.IsRequestingJump = true;

        scout.Simulate();

        scout.Motor.Handler.Move.FrameVelocity.Y.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);

        // Act - Initial Jump
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        Vector3d previousVelocity = scout.Motor.Handler.Move.FrameVelocity;

        // Continue holding jump for 3 frames
        for (int i = 0; i < 3; i++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        // Assert
        var expected = previousVelocity.Y - (scout.Motor.Handler.Forces.GravityForce * TestWorld.Context.DeltaTime * 3);
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeGreaterThan(expected);
    }

    [Fact]
    public void Given_HeldJumpWithNoExtraHeight_When_NextFrameSimulates_Then_GravityReducesVelocity()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = TestRequire.NotNull(scout.Motor.Handler.Jump);
        jump.MaxJumpCount = 2;
        jump.ExtraJumpHeight = Fixed64.Zero;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Fixed64 initialVelocity = scout.Motor.Handler.Move.FrameVelocity.Y;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;
        scout.Simulate();

        jump.IsHoldingJump.Should().BeTrue();
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeLessThan(initialVelocity);
    }

    [Fact]
    public void Given_ScoutOnGround_When_JumpHeld_Then_ShouldJumpHigher()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        for (int i = 0; i < 13; i++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        // Higher than default jump height
        scout.Position.Y.Should().BeGreaterThan(jump.BaseJumpHeight);
    }

    [Fact]
    public void Given_ScoutOnGround_When_JumpNotHeld_Then_ShouldNotJumpHigher()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        for (int i = 0; i < 13; i++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        scout.Position.Y.Should().BeGreaterThan(jump.BaseJumpHeight + Fixed64.Epsilon); // Higher than default jump height
    }

    [Fact]
    public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent(new Vector3d(0, 5, 0));
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.Rate = TrekRate.Slow;
        scout.Simulate();

        scout.Simulate();

        jump.IsJumping.Should().BeTrue();

        scout.FrameCondition = new()
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = Fixed64.FromRaw(5 << 16),
            CeilingLevel = Fixed64.FromRaw(6 << 16)
        };

        scout.Simulate();

        // Jump should be canceled
        jump.IsJumping.Should().BeFalse();
        // Should stop rising
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeLessThanOrEqualTo(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutHoldingJump_When_LandsOnGround_Then_ShouldResetJumpState()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        bool jumpStarted = false;
        bool jumpStopped = false;
        bool fallStarted = false;
        bool fallStopped = false;

        scout.Motor.Events.OnStartJump += (avoidTimer) => jumpStarted = true;
        scout.Motor.Events.OnStopJump += () => jumpStopped = true;
        scout.Motor.Events.OnStartFall += () => fallStarted = true;
        scout.Motor.Events.OnLandedFall += () => fallStopped = true;

        // Start Jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.Rate = TrekRate.Slow;
        scout.Simulate();

        // Simulate entire jump arc until landing
        for (int i = 0; i < 30; i++)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
            if (scout.Position.Y <= Fixed64.Zero) // If we've landed
                break;
        }

        scout.Simulate();

        scout.FrameCondition.Medium = TraversalMedium.Solid;
        scout.FrameCondition.SurfaceLevel = Fixed64.Zero;

        // Assert that jump state has been reset after actual landing
        jump.IsJumping.Should().BeFalse();
        jumpStarted.Should().BeTrue();
        jumpStopped.Should().BeTrue();
        fallStarted.Should().BeTrue();
        // We treat landing a jump differently
        fallStopped.Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutHoldingJump_When_HeldTooLong_Then_ShouldNotExceedMaxJump()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        var jumpLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        Fixed64 maxExpectedHeight = jumpLocomotion.BaseJumpHeight + jumpLocomotion.ExtraJumpHeight;

        Fixed64 maxY = scout.Position.Y;

        // Start jump
        scout.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Continue holding jump and track peak height until we land
        while (!scout.Motor.IsOnSolid)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();

            if (scout.Position.Y > maxY)
                maxY = scout.Position.Y;
        }

        maxY.Should().BeLessThanOrEqualTo(maxExpectedHeight);
    }

    [Fact]
    public void Given_ScoutTapsJump_When_ReleasedImmediately_Then_JumpHeightShouldBeReduced()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

        // Start jump
        scout.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Tap release
        for (int i = 0; i < 5; i++)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
        }

        scout.Position.Y.Should().BeGreaterThan((Fixed64)0.5).And.BeLessThan((Fixed64)1.0);
    }

    [Fact]
    public void Given_ScoutWithMultipleJumps_When_JumpsInAir_Then_JumpCountShouldIncrement()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.MaxJumpCount = 2;

        // First jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        jump.JumpCount.Should().Be(1);

        // Simulate midair
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Second jump in air
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        jump.JumpCount.Should().Be(2);
    }

    [Fact]
    public void Given_Scout_When_JumpCountEqualsMax_Then_JumpShouldBeBlocked()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.MaxJumpCount = 2;

        // First jump (ground)
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Second jump (air)
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        jump.JumpCount.Should().Be(2);

        var currentVelocity = scout.Velocity;

        // Attempt third jump
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Velocity shouldn't increase anymore
        jump.JumpCount.Should().Be(2);
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeLessThan(currentVelocity.Y);
    }

    [Fact]
    public void Given_ScoutWithMidairJumps_When_LandsOnGround_Then_JumpCountShouldReset()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.MaxJumpCount = 2;

        // First jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Second jump
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Land
        for (int i = 0; i < 30; i++)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
            if (scout.Position.Y <= Fixed64.Zero) break;
        }

        scout.FrameCondition.Medium = TraversalMedium.Solid;
        scout.FrameCondition.SurfaceLevel = Fixed64.Zero;
        scout.Simulate();

        jump.JumpCount.Should().Be(0);
    }

    [Fact]
    public void Given_ScoutMidairJump_When_SecondJumpOccurs_Then_VelocityShouldSpikeAgain()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.MaxJumpCount = 2;

        // First jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        var velocityAfterFirstJump = scout.Motor.Handler.Move.FrameVelocity.Y;

        // Midair frame
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Second jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        var velocityAfterSecondJump = scout.Motor.Handler.Move.FrameVelocity.Y;

        velocityAfterSecondJump.Should().BeGreaterThan(velocityAfterFirstJump);
    }

    [Fact]
    public void Given_ScoutJumpThenFall_When_JumpRequestedWhileFalling_Then_ShouldNotJump()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.MaxJumpCount = 2;

        // Frame 1: perform the initial jump
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        jump.JumpCount.Should().Be(1);

        // Simulate until fall state is entered
        while (!scout.Motor.Handler.Fall.IsFalling)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
        }

        scout.Motor.Handler.Fall.IsFalling.Should().BeTrue();
        jump.IsCoolingDown.Should().BeTrue();

        // Attempt second jump while falling
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Jump count should not increase
        jump.JumpCount.Should().Be(1);
        scout.Motor.Handler.Move.FrameVelocity.Y.Should().BeLessThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutJumping_When_CalculatingMaxSpeed_Then_ControlMultiplierShouldApply()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)6;
        jump.JumpControlMultiplier = (Fixed64)0.5;

        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        var speed = scout.Motor.MaxHoritzontalSpeedInDirection(
            Vector3d.Right,
            TrekRate.Moderate);

        speed.Should().Be((Fixed64)3);
    }

    [Fact]
    public void Given_ScoutJumping_When_ControlMultiplierIsZero_Then_NoHorizontalSpeed()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);

        scout.Motor.Handler.Move.MaxSidewaysSpeed = (Fixed64)8;
        jump.JumpControlMultiplier = Fixed64.Zero;

        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        var speed = scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Left, TrekRate.Fast);

        speed.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutInCooldown_When_CooldownNotExpired_Then_CooldownTimerShouldAdvanceButRemainCooling()
    {
        // Arrange — trigger a jump so IsCoolingDown = true
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        var jump = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Jump);
        jump.CooldownTime = (Fixed64)100; // very long cooldown so it never expires

        scout.FrameRequest.IsRequestingJump = true;
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Let one frame pass so UpdateCooldown runs, but timer < CooldownTime
        TestWorld.Context.Simulate();
        scout.Simulate();

        // IsCoolingDown should still be true (the false branch of CooldownTimer >= CooldownTime)
        jump.IsCoolingDown.Should().BeTrue();
        jump.CooldownTimer.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void UpdateCooldown_ShouldLeaveTimerUnchangedWhenCooldownIsInactive()
    {
        var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Solid);
        JumpLocomotion jump = TestRequire.NotNull(scout.Motor.Handler.Jump);

        jump.UpdateCooldown();

        jump.IsCoolingDown.Should().BeFalse();
        jump.CooldownTimer.Should().Be(Fixed64.Zero);
    }
}
