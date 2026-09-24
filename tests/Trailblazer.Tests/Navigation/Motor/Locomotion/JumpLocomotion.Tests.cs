using System;
using Chronicler.Timing;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FixedMathSharp.Chronicler;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class JumpLocomotionTests : IDisposable
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void FinalAllowedJumpKeepsItsHoldWithoutAdmittingAnotherJump(int maxJumpCount, bool canAffordJump)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.MaxJumpCount = maxJumpCount;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        int started = 0;
        scout.Motor.Events.OnStartJump += _ => started++;

        for (int index = 0; index < maxJumpCount; index++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        // Each admitted jump adds 4 m/s. Holding offsets the 1/4 m/s gravity
        // step; it must not add another impulse or require another admission.
        Fixed64 speed = (Fixed64)(4 * maxJumpCount);
        Assert.Equal(speed, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.True(jump.IsCoolingDown);
        ChronicleTimestamp start = jump.JumpStartTime;
        for (int frame = 1; frame <= 8; frame++)
        {
            Vector3d previous = scout.Position;
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.FrameRequest.CanAffordJump = canAffordJump;
            scout.Simulate();

            Assert.Equal(speed, scout.Motor.Handler.Move.FrameVelocity.Y);
            Assert.Equal(Vector3d.Up * (speed / 32), scout.Position - previous);
            Assert.Equal(maxJumpCount, jump.JumpCount);
            Assert.Equal(maxJumpCount, started);
            Assert.Equal(start, jump.JumpStartTime);
            Assert.False(jump.CanJump);
        }

        // Cooldown expiry alone does not replenish the exhausted jump count.
        Assert.False(jump.IsCoolingDown);
        Vector3d beforeExpiry = scout.Position;
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal(speed - Fixed64.One / 4, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Vector3d.Up * ((speed - Fixed64.One / 4) / 32), scout.Position - beforeExpiry);
        Assert.Equal(maxJumpCount, started);
        Assert.Equal(start, jump.JumpStartTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(3155760000L)]
    public void HeldJumpPreservesUpwardSpeedRegardlessOfWorldStartTime(long seconds)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        TimingTestUtility.SetTime(TestWorld.Context, 0, new ChronicleTimestamp(seconds, 0));
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Fixed64 speed = scout.Motor.Handler.Move.FrameVelocity.Y;
        Assert.True(speed > Fixed64.Zero);

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;
        scout.Simulate();

        Assert.Equal(speed, scout.Motor.Handler.Move.FrameVelocity.Y);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(int.MaxValue, false)]
    public void HeldJumpUsesElapsedDurationIncludingTheExactEndOfItsWindow(int elapsedFrames, bool held)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var motor = scout.Motor;
        JumpLocomotion jump = motor.Handler.Jump!;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        motor.Handler.Forces.GravityForce = (Fixed64)8;
        TimingTestUtility.SetTime(TestWorld.Context, int.MaxValue, new ChronicleTimestamp(3155760000L, 0));
        jump.RegisterJump();
        jump.FrameJumpDirection = Vector3d.Up;
        ChronicleDuration elapsed = elapsedFrames == int.MaxValue
            ? new ChronicleDuration(3155760000L, 0)
            : FixedChronicleTime.FromFixed64((Fixed64)elapsedFrames / 32);
        TimingTestUtility.SetTime(TestWorld.Context, int.MaxValue + 1L, jump.JumpStartTime + elapsed);
        motor.CurrentState.Update(new TrekCondition { Medium = TraversalMedium.Gas }, null);
        motor.Handler.Move.FrameVelocity = Vector3d.Up * (Fixed64)4;
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;

        Assert.True(motor.TryTraversal(scout.FrameRequest, out var displacement, out _, out _));
        Assert.Equal((held ? (Fixed64)4 : Fixed64.FromFraction(15, 4)) * TestWorld.Context.DeltaTime,
            displacement.Y);
        motor.AbortTraversalFrame();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3155760000L)]
    public void ActiveJumpKeepsItsElapsedWindowWhenTheHostChangesStepSize(long seconds)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        TimingTestUtility.SetTime(TestWorld.Context, 0, new ChronicleTimestamp(seconds, 0));
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal((Fixed64)4, scout.Motor.Handler.Move.FrameVelocity.Y);
        ChronicleTimestamp start = jump.JumpStartTime;

        foreach (int rate in new[] { 32, 32, 16, 16, 16 })
        {
            TestWorld.Context.SetFrameRate(rate);
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.FrameRequest.CanAffordJump = false;
            scout.Simulate();
            Assert.Equal((Fixed64)4, scout.Motor.Handler.Move.FrameVelocity.Y);
            Assert.Equal(start, jump.JumpStartTime);
        }
        Assert.Equal(new ChronicleDuration(0, 0x40000000), TestWorld.Context.ElapsedTime - start);
        TestWorld.Context.SetFrameRate(8);
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;
        scout.Simulate();
        Assert.Equal((Fixed64)3, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(new ChronicleDuration(0, 0x60000000), TestWorld.Context.ElapsedTime - start);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldJumpWithZeroSpeedOrExtraHeightDoesNotCancelGravity(bool zeroSpeed)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        var motor = scout.Motor;
        JumpLocomotion jump = motor.Handler.Jump!;
        jump.BaseJumpHeight = zeroSpeed ? Fixed64.Zero : Fixed64.One;
        jump.ExtraJumpHeight = zeroSpeed ? Fixed64.One : Fixed64.Zero;
        motor.Handler.Forces.GravityForce = (Fixed64)8;
        jump.RegisterJump();
        jump.FrameJumpDirection = Vector3d.Up;
        motor.CurrentState.Update(new TrekCondition { Medium = TraversalMedium.Gas }, null);
        motor.Handler.Move.FrameVelocity = Vector3d.Up * (Fixed64)4;
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;

        Assert.True(motor.TryTraversal(scout.FrameRequest, out var displacement, out _, out _));
        Assert.Equal(Fixed64.FromFraction(15, 4) * TestWorld.Context.DeltaTime, displacement.Y);
        motor.AbortTraversalFrame();
    }

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
        jump.JumpStartTime.Should().Be(ChronicleTimestamp.Zero);
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

        ChronicleTimestamp expectedJumpTime = jump.JumpStartTime;

        // Attempt to jump again immediately
        scout.FrameRequest.IsRequestingJump = true;

        TestWorld.Context.Simulate();
        scout.Simulate();

        // Assert
        scout.Motor.IsInGas.Should().BeTrue();
        jump.IsCoolingDown.Should().BeTrue();
        jump.JumpStartTime.Should().Be(expectedJumpTime);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReleasedJumpCannotResumeItsHoldWithoutANewAdmission(int maxJumpCount)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.MaxJumpCount = maxJumpCount;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        ChronicleTimestamp start = jump.JumpStartTime;
        Assert.Equal((Fixed64)4, scout.Motor.Handler.Move.FrameVelocity.Y);

        TestWorld.Context.Simulate();
        scout.Simulate(); // Requests are cleared between frames: release.
        Assert.Equal(Fixed64.FromFraction(15, 4), scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Fixed64.FromFraction(31, 128), scout.Position.Y);
        Assert.False(jump.IsHoldingJump);

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;
        scout.Simulate();
        Assert.Equal(Fixed64.FromFraction(7, 2), scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Fixed64.FromFraction(45, 128), scout.Position.Y);
        Assert.False(jump.IsHoldingJump);
        Assert.Equal(start, jump.JumpStartTime);

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate(); // Affordable again; only the two-jump profile can admit it.
        Assert.Equal(Fixed64.FromFraction(maxJumpCount == 2 ? 29 : 13, 4),
            scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(maxJumpCount, jump.JumpCount);
        Assert.Equal(maxJumpCount == 2, jump.IsHoldingJump);
        Assert.Equal(maxJumpCount == 2 ? TestWorld.Context.ElapsedTime : start, jump.JumpStartTime);
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
    public void Given_HeldJumpWithNoExtraHeight_When_NextFrameSimulates_Then_GravityReducesVelocity()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = TestRequire.NotNull(scout.Motor.Handler.Jump);
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.Zero;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal((Fixed64)4, scout.Motor.Handler.Move.FrameVelocity.Y);

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.FrameRequest.CanAffordJump = false;
        scout.Simulate();

        jump.IsHoldingJump.Should().BeTrue();
        Assert.Equal(Fixed64.FromFraction(15, 4), scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Fixed64.FromFraction(31, 128), scout.Position.Y);
    }

    [Theory]
    [InlineData(false, 17)]
    [InlineData(true, 33)]
    public void HeldAndReleasedJumpsFollowTheirBoundedArcAndResetOnLanding(bool hold, int peakSixteenths)
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        int starts = 0;
        int stops = 0;
        int falls = 0;
        int fallLandings = 0;
        scout.Motor.Events.OnStartJump += _ => starts++;
        scout.Motor.Events.OnStopJump += () => stops++;
        scout.Motor.Events.OnStartFall += () => falls++;
        scout.Motor.Events.OnLandedFall += () => fallLandings++;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Fixed64 peak = scout.Position.Y;
        for (int frame = 0; frame < 64 && !scout.Motor.IsOnSolid; frame++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = hold;
            scout.Simulate();
            peak = FixedMath.Max(peak, scout.Position.Y);
        }

        // The 32 Hz discrete arc reaches 17/16 m after a tap. Eight held
        // steps add exactly 1 m, then ordinary gravity resumes until landing.
        Assert.Equal(Fixed64.FromFraction(peakSixteenths, 16), peak);
        Assert.True(scout.Motor.IsOnSolid);
        Assert.Equal(Vector3d.Zero, scout.Position);
        Assert.Equal(0, jump.JumpCount);
        Assert.False(jump.IsJumping);
        Assert.False(jump.IsHoldingJump);
        Assert.True(jump.CanJump);
        Assert.Equal(1, starts);
        Assert.Equal(1, stops);
        Assert.Equal(1, falls);
        Assert.Equal(0, fallLandings); // Jump landing uses OnStopJump.
    }

    [Fact]
    public void LandingReplenishesJumpCountButMustNotBypassAnActiveCooldown()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        jump.CooldownTime = (Fixed64)2;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        int started = 0;
        scout.Motor.Events.OnStartJump += _ => started++;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        for (int frame = 0; frame < 64 && !scout.Motor.IsOnSolid; frame++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        Assert.True(scout.Motor.IsOnSolid);
        Assert.Equal(0, jump.JumpCount);
        Assert.True(jump.IsCoolingDown);
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal(Vector3d.Zero, scout.Position);
        Assert.Equal(Fixed64.Zero, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(1, started);
        Assert.Equal(0, jump.JumpCount);
        Assert.False(jump.IsHoldingJump);

        for (int frame = 0; frame < 64 && jump.IsCoolingDown; frame++)
        {
            TestWorld.Context.Simulate();
            scout.Simulate();
        }

        Assert.False(jump.IsCoolingDown);
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal((Fixed64)4, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Vector3d.Up / 8, scout.Position);
        Assert.Equal(2, started);
        Assert.Equal(1, jump.JumpCount);
    }

    [Fact]
    public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
    {
        var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();
        JumpLocomotion jump = scout.Motor.Handler.Jump!;
        jump.BaseJumpHeight = Fixed64.One;
        jump.ExtraJumpHeight = Fixed64.One;
        scout.Motor.Handler.Forces.GravityForce = (Fixed64)8;
        scout.FrameCondition.CeilingLevel = Fixed64.One / 4;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        ChronicleTimestamp start = jump.JumpStartTime;
        for (int frame = 0; frame < 2; frame++)
        {
            TestWorld.Context.Simulate();
            scout.FrameRequest.IsRequestingJump = true;
            scout.Simulate();
        }

        Assert.False(jump.IsJumping);
        Assert.False(jump.IsHoldingJump);
        Assert.Equal(Fixed64.Zero, scout.Motor.Handler.Move.FrameVelocity.Y);
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();
        Assert.Equal(-Fixed64.One / 4, scout.Motor.Handler.Move.FrameVelocity.Y);
        Assert.Equal(Fixed64.FromFraction(47, 128), scout.Position.Y);
        Assert.Equal(start, jump.JumpStartTime);
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
