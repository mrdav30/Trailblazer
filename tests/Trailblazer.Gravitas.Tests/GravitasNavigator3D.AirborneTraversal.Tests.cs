using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DAirborneTraversalTests
{
    private const int FlightLossRelease = 0;
    private const int FlightLossCapability = 1;
    private const int FlightLossModule = 2;
    private const int FlightHover = 0;
    private const int FlightAscend = 1;
    private const int FlightDescend = 2;

    [Fact]
    public void WalkOffLedge_ShouldRejectFallJumpAndLandOnceOnTheLowerSupport()
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateWalkingActor();
        NavMotor motor = actor.Navigator.Motor!;
        JumpLocomotion jump = motor.Handler.Jump!;
        jump.MaxJumpCount = 2;

        int fallStarted = 0;
        int landed = 0;
        int jumpStarted = 0;
        int stoppedFall = 0;
        Fixed64 reportedFallHeight = Fixed64.Zero;
        motor.Events.OnStartFall += () => fallStarted++;
        motor.Events.OnLandedFall += () => landed++;
        motor.Events.OnStartJump += _ => jumpStarted++;
        motor.Events.OnStopFall += height =>
        {
            stoppedFall++;
            reportedFallHeight = height;
        };

        bool requestedJumpWhileFalling = false;
        for (int frame = 0; frame < 256 && landed == 0; frame++)
        {
            bool requestJump = motor.IsFalling && !requestedJumpWhileFalling;
            bool clearingLedge =
                actor.Navigator.Position.X <= fixture.Profile.Shape.Radius;
            actor.Navigator.ApplyInputTrekRequest(
                direction: clearingLedge ? Vector3d.Right : Vector3d.Zero,
                rate: clearingLedge ? TrekRate.Fast : TrekRate.Stationary,
                isRequestingJump: requestJump);
            fixture.Step(actor);
            requestedJumpWhileFalling |= requestJump;
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
        }

        for (int frame = 0; frame < 64; frame++)
        {
            actor.Navigator.ApplyInputTrekRequest();
            fixture.Step(actor);
        }

        requestedJumpWhileFalling.Should().BeTrue(
            "final pose was {0}, requested {1}, medium {2}, fall starts {3}, landings {4}",
            actor.Navigator.Position,
            actor.NavigatorRequestedPosition,
            motor.CurrentState.Medium,
            fallStarted,
            landed);
        jumpStarted.Should().Be(0);
        jump.JumpCount.Should().Be(0);
        fallStarted.Should().Be(1);
        landed.Should().Be(
            1,
            "final pose was {0}, requested {1}, host request {2}, medium {3}, velocity {4}",
            actor.Navigator.Position,
            actor.NavigatorRequestedPosition,
            actor.HostRequestedPosition,
            motor.CurrentState.Medium,
            actor.Navigator.Velocity);
        stoppedFall.Should().Be(1);
        reportedFallHeight.Should().BeGreaterThan(Fixed64.One);
        motor.IsOnSolid.Should().BeTrue();
        (actor.Navigator.FootPosition.Y - fixture.LandingSurface).Abs().Should()
            .BeLessThanOrEqualTo(Fixed64.FromFraction(1, 512));
        actor.Navigator.Position.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void GroundJump_ShouldPermitConfiguredSecondAscentJumpAndResetOnLanding()
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateWalkingActor();
        NavMotor motor = actor.Navigator.Motor!;
        JumpLocomotion jump = motor.Handler.Jump!;
        jump.MaxJumpCount = 2;

        int jumpStarted = 0;
        int jumpStopped = 0;
        motor.Events.OnStartJump += _ => jumpStarted++;
        motor.Events.OnStopJump += () => jumpStopped++;
        Fixed64 maximumRootY = actor.Navigator.Position.Y;
        Fixed64 velocityBeforeSecondJump = Fixed64.Zero;
        Fixed64 velocityAfterSecondJump = Fixed64.Zero;
        bool requestedSecondJump = false;

        for (int frame = 0; frame < 256; frame++)
        {
            bool firstJump = frame == 0;
            bool secondJump = !requestedSecondJump
                && jump.JumpCount == 1
                && motor.IsInGas
                && actor.Navigator.Velocity.Y > Fixed64.Zero;
            if (secondJump)
                velocityBeforeSecondJump = actor.Navigator.Velocity.Y;

            actor.Navigator.ApplyInputTrekRequest(
                isRequestingJump: firstJump || secondJump);
            fixture.Step(actor);

            if (secondJump)
            {
                requestedSecondJump = true;
                velocityAfterSecondJump = actor.Navigator.Velocity.Y;
            }

            maximumRootY = FixedMath.Max(maximumRootY, actor.Navigator.Position.Y);
            if (requestedSecondJump && motor.IsOnSolid && jump.JumpCount == 0)
                break;
        }

        requestedSecondJump.Should().BeTrue();
        jumpStarted.Should().Be(2);
        velocityAfterSecondJump.Should().BeGreaterThan(velocityBeforeSecondJump);
        maximumRootY.Should().BeGreaterThan(fixture.WalkingStartRoot.Y + Fixed64.One);
        motor.IsOnSolid.Should().BeTrue();
        jump.JumpCount.Should().Be(0);
        jump.IsJumping.Should().BeFalse();
        jumpStopped.Should().Be(1);
    }

    [Fact]
    public void FallingScout_ShouldAcquireFlightOnlyWhenAllowedAndDescendWithoutFalling()
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateAirborneActor();
        NavMotor motor = actor.Navigator.Motor!;
        FlyLocomotion fly = motor.Handler.Fly!;
        FallLocomotion fall = motor.Handler.Fall;

        actor.Navigator.ApplyInputTrekRequest();
        fixture.Step(actor);
        fall.IsFalling.Should().BeTrue();

        fly.CanFly = false;
        actor.Navigator.ApplyInputTrekRequest(
            direction: Vector3d.Down,
            rate: TrekRate.Fast,
            isRequestingFlight: true);
        fixture.Step(actor);

        fly.IsFlying.Should().BeFalse();
        fall.IsFalling.Should().BeTrue();
        Fixed64 deniedPositionY = actor.Navigator.Position.Y;

        fly.CanFly = true;
        actor.Navigator.ApplyInputTrekRequest(
            direction: Vector3d.Down,
            rate: TrekRate.Fast,
            isRequestingFlight: true);
        fixture.Step(actor);

        fly.IsFlying.Should().BeTrue();
        fall.IsFalling.Should().BeFalse();
        fall.IsControlledDescending.Should().BeFalse();
        motor.IsJumping.Should().BeFalse();
        actor.Navigator.Velocity.Y.Should().BeLessThan(Fixed64.Zero);
        actor.Navigator.Position.Y.Should().BeLessThan(deniedPositionY);
    }

    [Fact]
    public void ActiveFlightJumpInput_ShouldMatchTheNoJumpAcceptedMotionTrace()
    {
        FlightTrace control = RunActiveFlightTrace(requestJump: false);
        FlightTrace simultaneousJump = RunActiveFlightTrace(requestJump: true);

        simultaneousJump.Frames.Should().Equal(control.Frames);
        control.IsFlying.Should().BeTrue();
        simultaneousJump.IsFlying.Should().BeTrue();
        control.IsFalling.Should().BeFalse();
        simultaneousJump.IsFalling.Should().BeFalse();
        control.JumpCount.Should().Be(0);
        simultaneousJump.JumpCount.Should().Be(0);
        control.JumpEvents.Should().Be(0);
        simultaneousJump.JumpEvents.Should().Be(0);
        simultaneousJump.Frames.Should().OnlyContain(
            frame => frame.IsFlying && !frame.IsFalling);
    }

    [Theory]
    [InlineData(FlightLossRelease, FlightHover)]
    [InlineData(FlightLossRelease, FlightAscend)]
    [InlineData(FlightLossRelease, FlightDescend)]
    [InlineData(FlightLossCapability, FlightHover)]
    [InlineData(FlightLossCapability, FlightAscend)]
    [InlineData(FlightLossCapability, FlightDescend)]
    [InlineData(FlightLossModule, FlightHover)]
    [InlineData(FlightLossModule, FlightAscend)]
    [InlineData(FlightLossModule, FlightDescend)]
    public void FlightLoss_ShouldPreserveAcceptedMomentum(
        int lossMode,
        int verticalMode)
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateAirborneActor(
            new Vector3d(Fixed64.One, (Fixed64)5, Fixed64.Zero));
        NavMotor motor = actor.Navigator.Motor!;
        FlyLocomotion fly = motor.Handler.Fly!;
        Vector3d direction = verticalMode switch
        {
            FlightAscend => Vector3d.Up,
            FlightDescend => Vector3d.Down,
            _ => Vector3d.Zero
        };

        for (int frame = 0; frame < 3; frame++)
        {
            actor.Navigator.ApplyInputTrekRequest(
                direction: direction,
                rate: TrekRate.Fast,
                isRequestingFlight: true);
            fixture.Step(actor);
        }

        Fixed64 velocityBeforeLoss = actor.Navigator.Velocity.Y;
        Fixed64 positionBeforeLoss = actor.Navigator.Position.Y;
        bool keepRequestingFlight = lossMode != FlightLossRelease;
        if (lossMode == FlightLossCapability)
            fly.CanFly = false;
        else if (lossMode == FlightLossModule)
            fly.IsEnabled = false;

        actor.Navigator.ApplyInputTrekRequest(
            direction: direction,
            rate: TrekRate.Fast,
            isRequestingFlight: keepRequestingFlight);
        fixture.Step(actor);

        fly.IsFlying.Should().BeFalse();
        actor.Navigator.Position.Should().Be(actor.Body.Position3d);
        (actor.Navigator.Position.Y - positionBeforeLoss).Should().Be(
            actor.Navigator.Velocity.Y * fixture.Trailblazer.DeltaTime);

        if (verticalMode == FlightAscend)
        {
            velocityBeforeLoss.Should().BeGreaterThan(Fixed64.Zero);
            actor.Navigator.Velocity.Y.Should().BeGreaterThan(Fixed64.Zero);
            actor.Navigator.Position.Y.Should().BeGreaterThan(positionBeforeLoss);
            motor.IsFalling.Should().BeFalse();
        }
        else
        {
            if (verticalMode == FlightHover)
                velocityBeforeLoss.Should().Be(Fixed64.Zero);
            else
                velocityBeforeLoss.Should().BeLessThan(Fixed64.Zero);
            actor.Navigator.Velocity.Y.Should().BeLessThan(Fixed64.Zero);
            motor.IsFalling.Should().BeTrue();
        }
    }

    [Fact]
    public void ControlledDescent_ShouldBoundFallRetainSteeringAndReplayExactly()
    {
        DescentTrace first = RunDescentTrace();
        DescentTrace replay = RunDescentTrace();

        replay.Frames.Should().Equal(first.Frames);
        first.ControlledAdvantageObserved.Should().BeTrue();
        first.BoundedDecelerationObserved.Should().BeTrue();
        first.MaximumControlledDeceleration.Should().BeLessThanOrEqualTo(
            (Fixed64)4 * Fixed64.FromFraction(1, 32));
        first.TerminalSpeedObserved.Should().BeTrue();
        first.ControlledLateralPosition.Should().BeGreaterThan(first.FreeLateralPosition);
        first.CancellationSpeed.Should().BeLessThan(first.PreCancellationSpeed);
        first.Redeployed.Should().BeTrue();
        first.Frames[4].IsControlledDescending.Should().BeTrue();
        first.Frames[12].IsControlledDescending.Should().BeFalse();
        first.Frames[15].IsControlledDescending.Should().BeTrue();
        first.PoweredAscentObserved.Should().BeFalse();
        first.FreeFallStarts.Should().Be(1);
        first.ControlledFallStarts.Should().Be(1);
        first.FreeLandings.Should().Be(1);
        first.ControlledLandings.Should().Be(1);
        first.FreeFallStops.Should().Be(1);
        first.ControlledFallStops.Should().Be(1);
        first.FreeFallHeight.Should().BeGreaterThan((Fixed64)3);
        first.ControlledFallHeight.Should().BeGreaterThan((Fixed64)3);
        first.FreeImpactVelocity.Should().BeLessThan(Fixed64.Zero);
        first.ControlledImpactVelocity.Should().BeLessThan(Fixed64.Zero);
        first.ControlledImpactVelocity.Should().BeGreaterThan(
            first.FreeImpactVelocity + Fixed64.Half,
            "controlled descent should materially reduce downward impact speed");
        (first.ControlledImpactVelocity + Fixed64.One).Abs().Should()
            .BeLessThanOrEqualTo(
                Fixed64.FromFraction(1, 512),
                "controlled descent should hold the configured terminal speed through impact");
        Fixed64 landingRoot = (Fixed64)(-2) + Fixed64.Half;
        (first.Frames[^1].FreePosition.Y - landingRoot).Abs().Should()
            .BeLessThanOrEqualTo(Fixed64.FromFraction(1, 512));
        (first.Frames[^1].ControlledPosition.Y - landingRoot).Abs().Should()
            .BeLessThanOrEqualTo(Fixed64.FromFraction(1, 512));
    }

    private static FlightTrace RunActiveFlightTrace(bool requestJump)
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor actor = fixture.CreateAirborneActor(
            new Vector3d(Fixed64.One, (Fixed64)5, Fixed64.Zero));
        NavMotor motor = actor.Navigator.Motor!;
        int jumpEvents = 0;
        motor.Events.OnStartJump += _ => jumpEvents++;

        actor.Navigator.ApplyInputTrekRequest(isRequestingFlight: true);
        fixture.Step(actor);

        var frames = new List<FlightFrame>(12);
        for (int frame = 0; frame < 12; frame++)
        {
            Vector3d direction = frame < 4
                ? Vector3d.Up
                : frame < 8
                    ? Vector3d.Right
                    : Vector3d.Down;
            actor.Navigator.ApplyInputTrekRequest(
                direction: direction,
                rate: TrekRate.Fast,
                isRequestingFlight: true,
                isRequestingJump: requestJump);
            fixture.Step(actor);
            frames.Add(new FlightFrame(
                actor.Navigator.Position,
                actor.Navigator.Velocity,
                motor.IsFlying,
                motor.IsFalling));
        }

        return new FlightTrace(
            frames.ToArray(),
            motor.IsFlying,
            motor.IsFalling,
            motor.Handler.Jump!.JumpCount,
            jumpEvents);
    }

    private static DescentTrace RunDescentTrace()
    {
        using var fixture = new GravitasAirborneScenarioFixture();
        using GravitasNavigatorActor free = fixture.CreateAirborneActor(
            new Vector3d(Fixed64.One, (Fixed64)3, -Fixed64.One));
        using GravitasNavigatorActor controlled = fixture.CreateAirborneActor(
            new Vector3d(Fixed64.One, (Fixed64)3, Fixed64.One));
        NavMotor freeMotor = free.Navigator.Motor!;
        NavMotor controlledMotor = controlled.Navigator.Motor!;
        FallLocomotion controlledFall = controlledMotor.Handler.Fall;
        controlledFall.CanControlDescent = true;
        controlledFall.ControlledDescentTerminalSpeed = Fixed64.One;
        controlledFall.ControlledDescentDeceleration = (Fixed64)4;
        controlledFall.ControlledDescentControlMultiplier = Fixed64.Half;

        int freeFallStarts = 0;
        int controlledFallStarts = 0;
        int freeLandings = 0;
        int controlledLandings = 0;
        int freeFallStops = 0;
        int controlledFallStops = 0;
        Fixed64 freeFallHeight = Fixed64.Zero;
        Fixed64 controlledFallHeight = Fixed64.Zero;
        Fixed64 freeImpactVelocity = Fixed64.Zero;
        Fixed64 controlledImpactVelocity = Fixed64.Zero;
        freeMotor.Events.OnStartFall += () => freeFallStarts++;
        controlledMotor.Events.OnStartFall += () => controlledFallStarts++;
        freeMotor.Events.OnLandedFall += () =>
        {
            freeLandings++;
            freeImpactVelocity = free.Navigator.Velocity.Y;
        };
        controlledMotor.Events.OnLandedFall += () =>
        {
            controlledLandings++;
            controlledImpactVelocity = controlled.Navigator.Velocity.Y;
        };
        freeMotor.Events.OnStopFall += height =>
        {
            freeFallStops++;
            freeFallHeight = height;
        };
        controlledMotor.Events.OnStopFall += height =>
        {
            controlledFallStops++;
            controlledFallHeight = height;
        };

        var frames = new List<DescentFrame>(192);
        bool advantageObserved = false;
        bool boundedDecelerationObserved = false;
        bool terminalSpeedObserved = false;
        bool redeployed = false;
        bool poweredAscentObserved = false;
        Fixed64 maximumControlledDeceleration = Fixed64.Zero;
        Fixed64 previousControlledVelocity = Fixed64.Zero;
        Fixed64 freeLateralPosition = Fixed64.Zero;
        Fixed64 controlledLateralPosition = Fixed64.Zero;
        Fixed64 preCancellationSpeed = Fixed64.Zero;
        Fixed64 cancellationSpeed = Fixed64.Zero;
        for (int frame = 0; frame < 192; frame++)
        {
            bool deploying = (frame >= 4 && frame < 12) || frame >= 15;
            bool freeAirborne = freeLandings == 0;
            bool controlledAirborne = controlledLandings == 0;
            free.Navigator.ApplyInputTrekRequest(
                direction: freeAirborne ? Vector3d.Right : Vector3d.Zero,
                rate: freeAirborne ? TrekRate.Fast : TrekRate.Stationary);
            controlled.Navigator.ApplyInputTrekRequest(
                direction: controlledAirborne ? Vector3d.Right : Vector3d.Zero,
                rate: controlledAirborne ? TrekRate.Fast : TrekRate.Stationary,
                isRequestingControlledDescent: controlledAirborne && deploying);
            fixture.Step(free, controlled);

            if (frame >= 5 && frame < 12
                && controlled.Navigator.Velocity.Y > free.Navigator.Velocity.Y)
            {
                advantageObserved = true;
            }

            if (frame == 11)
            {
                freeLateralPosition = free.Navigator.Position.X;
                controlledLateralPosition = controlled.Navigator.Position.X;
                preCancellationSpeed = controlled.Navigator.Velocity.Y;
            }
            else if (frame == 12)
            {
                cancellationSpeed = controlled.Navigator.Velocity.Y;
            }

            if (frame >= 15 && controlledFall.IsControlledDescending)
                redeployed = true;
            if (controlledFall.IsControlledDescending)
            {
                Fixed64 controlledVelocityChange =
                    controlled.Navigator.Velocity.Y - previousControlledVelocity;
                if (controlledVelocityChange > Fixed64.Zero)
                {
                    boundedDecelerationObserved = true;
                    maximumControlledDeceleration = FixedMath.Max(
                        maximumControlledDeceleration,
                        controlledVelocityChange);
                }

                terminalSpeedObserved |=
                    controlled.Navigator.Velocity.Y == -Fixed64.One;
            }
            poweredAscentObserved |= controlled.Navigator.Velocity.Y > Fixed64.Zero;
            frames.Add(new DescentFrame(
                free.Navigator.Position,
                free.Navigator.Velocity,
                controlled.Navigator.Position,
                controlled.Navigator.Velocity,
                controlledFall.IsControlledDescending,
                freeMotor.IsOnSolid,
                controlledMotor.IsOnSolid));
            previousControlledVelocity = controlled.Navigator.Velocity.Y;
        }

        return new DescentTrace(
            frames.ToArray(),
            advantageObserved,
            boundedDecelerationObserved,
            maximumControlledDeceleration,
            terminalSpeedObserved,
            freeLateralPosition,
            controlledLateralPosition,
            preCancellationSpeed,
            cancellationSpeed,
            redeployed,
            poweredAscentObserved,
            freeFallStarts,
            controlledFallStarts,
            freeLandings,
            controlledLandings,
            freeFallStops,
            controlledFallStops,
            freeFallHeight,
            controlledFallHeight,
            freeImpactVelocity,
            controlledImpactVelocity);
    }

    private readonly record struct FlightFrame(
        Vector3d Position,
        Vector3d Velocity,
        bool IsFlying,
        bool IsFalling);

    private readonly record struct FlightTrace(
        FlightFrame[] Frames,
        bool IsFlying,
        bool IsFalling,
        int JumpCount,
        int JumpEvents);

    private readonly record struct DescentFrame(
        Vector3d FreePosition,
        Vector3d FreeVelocity,
        Vector3d ControlledPosition,
        Vector3d ControlledVelocity,
        bool IsControlledDescending,
        bool FreeIsGrounded,
        bool ControlledIsGrounded);

    private readonly record struct DescentTrace(
        DescentFrame[] Frames,
        bool ControlledAdvantageObserved,
        bool BoundedDecelerationObserved,
        Fixed64 MaximumControlledDeceleration,
        bool TerminalSpeedObserved,
        Fixed64 FreeLateralPosition,
        Fixed64 ControlledLateralPosition,
        Fixed64 PreCancellationSpeed,
        Fixed64 CancellationSpeed,
        bool Redeployed,
        bool PoweredAscentObserved,
        int FreeFallStarts,
        int ControlledFallStarts,
        int FreeLandings,
        int ControlledLandings,
        int FreeFallStops,
        int ControlledFallStops,
        Fixed64 FreeFallHeight,
        Fixed64 ControlledFallHeight,
        Fixed64 FreeImpactVelocity,
        Fixed64 ControlledImpactVelocity);
}
