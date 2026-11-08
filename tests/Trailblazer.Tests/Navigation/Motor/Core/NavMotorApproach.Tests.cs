using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class NavMotorApproachTests : IDisposable
{
    public NavMotorApproachTests()
    {
        TestWorld.Setup();
        TestWorld.Context.SetFrameRate(32);
    }

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Approach_ShouldBoundGroundMovementWithoutReplacingGravityOrAddingVerticalMotion()
    {
        var unbounded = CreateGroundAgent();
        var bounded = CreateGroundAgent();
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining + Vector3d.Up;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        expected.Move.X.Should().BeGreaterThan(remaining);
        expected.Move.Y.Should().BeLessThan(Fixed64.Zero);
        actual.Move.Should().Be(new Vector3d(remaining, expected.Move.Y, expected.Move.Z));
    }

    [Theory]
    [InlineData(1, 0, false, 11)]
    [InlineData(1, 2, false, 11)]
    [InlineData(1, -2, false, 11)]
    [InlineData(-1, 0, false, -1)]
    [InlineData(-1, 2, false, -1)]
    [InlineData(-1, -2, false, -1)]
    [InlineData(1, 0, true, 11)]
    [InlineData(-1, 0, true, -1)]
    public void Approach_ShouldFollowRationalSupportSlopeAndPreserveVerticalResidual(
        int riseSign,
        int targetOffsetY,
        bool alongZ,
        int expectedVerticalNumerator)
    {
        Vector3d direction = alongZ ? Vector3d.Forward : Vector3d.Right;
        // A 3:4 support slope; scaling its normal avoids rounded unit-normal inputs.
        Vector3d normal = Vector3d.Up * 4 - direction * (Fixed64)(riseSign * 3);
        var agent = CreateGroundAgent(normal);
        agent.Motor.Handler.Move.MaxGroundAcceleration = Fixed64.Zero;
        agent.Motor.SetVelocity(direction * 4 + Vector3d.Up * (Fixed64)(riseSign * 3 + 1));
        TrekRequest request = CreateRequest(agent, direction);
        Fixed64 remaining = Fixed64.FromFraction(1, 32);
        Vector3d target = request.FootPosition!.Value + direction * remaining
            + Vector3d.Up * (Fixed64)targetOffsetY;

        var actual = Traverse(agent, request, target);

        // Tangent rise is +/-3/128, existing vertical residual is 1/32,
        // and gravity contributes -12/(32*32), giving 11/256 or -1/256.
        Vector3d expected = direction * remaining
            + Vector3d.Up * Fixed64.FromFraction(expectedVerticalNumerator, 256);
        agent.Motor.Handler.IsInControl.Should().BeTrue();
        actual.Move.Should().Be(expected,
            "shortening horizontal travel must shorten only its support-tangent rise, regardless of target Y");
    }

    [Theory]
    [InlineData(1, false, 4L, 3L)]
    [InlineData(-1, false, 4L, -3L)]
    [InlineData(1, true, 1L, 1L)]
    [InlineData(-1, true, 1L, -1L)]
    [InlineData(1, true, 3L, 2L)]
    [InlineData(-1, true, 3L, -2L)]
    public void Approach_ShouldPreserveRawBitEndpointOnRationalSupportSlope(
        int riseSign,
        bool unitNormal,
        long targetRaw,
        long expectedVerticalRaw)
    {
        Vector3d normal = new((Fixed64)(-riseSign * 3), (Fixed64)4, Fixed64.Zero);
        if (unitNormal)
            normal /= (Fixed64)5;
        var agent = CreateGroundAgent(normal);
        agent.Motor.Handler.Move.MaxGroundAcceleration = Fixed64.Zero;
        agent.Motor.Handler.Forces.GravityForce = Fixed64.Zero;
        agent.Motor.SetVelocity(new Vector3d((Fixed64)4, (Fixed64)(riseSign * 3), Fixed64.Zero));
        TrekRequest request = CreateRequest(agent, Vector3d.Right);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * Fixed64.FromRaw(targetRaw);

        var actual = Traverse(agent, request, target);

        actual.Move.Should().Be(new Vector3d(Fixed64.FromRaw(targetRaw), Fixed64.FromRaw(expectedVerticalRaw), Fixed64.Zero));
    }

    [Fact]
    public void Approach_ShouldRemainHorizontalWhenSupportNormalIsUnavailable()
    {
        var agent = CreateGroundAgent(Vector3d.Zero);
        agent.Motor.Handler.Move.MaxGroundAcceleration = Fixed64.Zero;
        agent.Motor.SetVelocity(new Vector3d(4, -1, 0));
        TrekRequest request = CreateRequest(agent, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 32);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining + Vector3d.Up * 2;

        var actual = Traverse(agent, request, target);

        actual.Move.Should().Be(new Vector3d(remaining, Fixed64.FromFraction(-11, 256), Fixed64.Zero));
    }

    [Fact]
    public void Approach_ShouldPreserveHorizontalAndVerticalJumpImpulseAfterBoundingWalking()
    {
        Vector3d normal = new(Fixed64.FromFraction(3, 5), Fixed64.FromFraction(4, 5), Fixed64.Zero);
        var walking = CreateGroundAgent(normal);
        var boundedWalking = CreateGroundAgent(normal);
        var jumping = CreateGroundAgent(normal);
        var boundedJumping = CreateGroundAgent(normal);
        jumping.Motor.Handler.Jump!.PerpendicularJumpAmount = Fixed64.One;
        boundedJumping.Motor.Handler.Jump!.PerpendicularJumpAmount = Fixed64.One;
        TrekRequest request = CreateRequest(walking, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining;

        Vector3d walk = Traverse(walking, request).Move;
        Vector3d boundedWalk = Traverse(boundedWalking, request, target).Move;
        request.IsRequestingJump = true;
        Vector3d jump = Traverse(jumping, request).Move;
        Vector3d boundedJump = Traverse(boundedJumping, request, target).Move;

        walk.X.Should().BeGreaterThan(remaining);
        boundedWalk.X.Should().Be(remaining);
        jumping.Motor.IsJumping.Should().BeTrue();
        boundedJumping.Motor.IsJumping.Should().BeTrue();
        (jump - walk).X.Should().BeGreaterThan(Fixed64.Zero);
        boundedJump.Y.Should().BeGreaterThan(Fixed64.Zero);
        (boundedJump - boundedWalk).Should().Be(jump - walk,
            "the action-entry limit constrains walking, not the subsequent jump impulse");
    }

    [Fact]
    public void Approach_ShouldNotConstrainUncontrolledSteepSlopeSliding()
    {
        Vector3d normal = new(Fixed64.FromFraction(4, 5), Fixed64.FromFraction(3, 5), Fixed64.Zero);
        var unbounded = CreateGroundAgent(normal);
        var bounded = CreateGroundAgent(normal);
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        bounded.Motor.Handler.IsInControl.Should().BeFalse();
        bounded.Motor.Handler.Slide!.IsSliding.Should().BeTrue();
        expected.Move.X.Should().BeGreaterThan(remaining);
        actual.Move.Should().Be(expected.Move);
    }

    [Fact]
    public void Approach_ShouldNotConstrainUncontrolledLimboMomentum()
    {
        var unbounded = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: Vector3d.Right);
        var bounded = MockMotorAgentTestFactory.CreateMockAgent(startVelocity: Vector3d.Right);
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        bounded.Motor.InLimbo.Should().BeTrue();
        bounded.Motor.Handler.IsInControl.Should().BeFalse();
        expected.Move.X.Should().BeGreaterThan(remaining);
        actual.Move.Should().Be(expected.Move);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Approach_ShouldNotAttractStationaryOrZeroDirectionAgent(bool stationaryRate)
    {
        var unbounded = CreateGroundAgent();
        var bounded = CreateGroundAgent();
        TrekRequest request = CreateRequest(bounded, stationaryRate ? Vector3d.Right : Vector3d.Zero);
        if (stationaryRate)
            request.Rate = TrekRate.Stationary;
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * Fixed64.FromFraction(1, 64);

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        actual.Move.Should().Be(expected.Move);
        actual.Move.X.Should().Be(Fixed64.Zero);
        actual.Move.Y.Should().BeLessThan(Fixed64.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    public void Approach_ShouldLeaveCoincidentDistantOrBehindTargetMovementUnchanged(int targetOffsetX)
    {
        var unbounded = CreateGroundAgent();
        var bounded = CreateGroundAgent();
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * (Fixed64)targetOffsetX;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        expected.Move.X.Should().BeGreaterThan(Fixed64.Zero);
        actual.Move.Should().Be(expected.Move);
    }

    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(2L, 2L)]
    [InlineData(3L, 2L)]
    public void Approach_ShouldDistinguishRawBitUndershootEqualityAndOvershoot(
        long movementRaw,
        long expectedRaw)
    {
        var unbounded = CreateGroundAgent();
        var bounded = CreateGroundAgent();
        Vector3d velocity = Vector3d.Right * Fixed64.FromRaw(movementRaw * 32);
        unbounded.Motor.SetVelocity(velocity);
        bounded.Motor.SetVelocity(velocity);
        unbounded.Motor.Handler.Move.MaxGroundAcceleration = Fixed64.Zero;
        bounded.Motor.Handler.Move.MaxGroundAcceleration = Fixed64.Zero;
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * Fixed64.FromRaw(2);

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        expected.Move.X.Should().Be(Fixed64.FromRaw(movementRaw));
        actual.Move.X.Should().Be(Fixed64.FromRaw(expectedRaw),
            "rounded dot products must not turn a sub-target movement into an apparent crossing");
        actual.Move.Y.Should().Be(expected.Move.Y);
    }

    [Fact]
    public void Approach_ShouldReachExactRawBitTargetAtNonBinaryTimestep()
    {
        TestWorld.Context.SetFrameRate(60);
        var agent = CreateGroundAgent();
        TrekRequest request = CreateRequest(agent, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromRaw(3);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining;

        var actual = Traverse(agent, request, target);

        actual.Move.X.Should().Be(remaining,
            "the exact displacement must not lose low bits in a velocity round trip");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Approach_ShouldBoundControlledFlightOrSwimmingIn3DAndPreserveResidualForces(
        bool flying,
        bool hasFootSnapshot)
    {
        var noEnvironment = CreateThreeDimensionalAgent(flying, Fixed64.Zero);
        var unbounded = CreateThreeDimensionalAgent(flying, (Fixed64)12);
        var bounded = CreateThreeDimensionalAgent(flying, (Fixed64)12);
        TrekRequest request = CreateRequest(bounded, (Vector3d.Right + Vector3d.Up).Normalized);
        request.IsRequestingFlight = flying;
        request.IsRequestingSwim = !flying;
        if (!hasFootSnapshot)
            request.FootPosition = null;
        Vector3d remaining = new(Fixed64.FromFraction(1, 64), Fixed64.FromFraction(1, 64), Fixed64.Zero);
        Vector3d target = (request.FootPosition ?? request.Origin) + remaining;

        Vector3d controlled = Traverse(noEnvironment, request).Move;
        Vector3d total = Traverse(unbounded, request).Move;
        Vector3d actual = Traverse(bounded, request, target).Move;

        controlled.X.Should().BeGreaterThan(remaining.X);
        controlled.Y.Should().BeGreaterThan(remaining.Y);
        bounded.Motor.IsFlying.Should().Be(flying);
        bounded.Motor.Handler.Water!.IsSwimming.Should().Be(!flying);
        Vector3d residual = total - controlled;
        residual.Y.Should().BeLessThan(Fixed64.Zero);
        actual.X.Should().Be(remaining.X);
        (actual - remaining).Should().Be(residual,
            "partial gravity compensation or buoyancy still applies after limiting controlled 3D movement");
    }

    [Fact]
    public void Approach_ShouldBoundActiveLadderClimbVerticallyWithoutRestartingAttachment()
    {
        var unbounded = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        var bounded = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas);
        int climbStarts = 0;
        foreach (MockMotorAgent agent in new[] { unbounded, bounded })
        {
            ConfigureMovement(agent);
            ClimbLocomotion climb = agent.Motor.Handler.Climb!;
            climb.ClimbResolver = new LadderResolver();
            climb.MaxClimbSpeed = (Fixed64)8;
            climb.MaxClimbAcceleration = (Fixed64)1024;
            climb.GravityCompensationWhileClimbing = Fixed64.One;
            agent.Motor.Events.OnStartClimb += _ => climbStarts++;
        }
        TrekRequest request = CreateRequest(bounded, Vector3d.Up);
        request.IsRequestingClimb = true;
        Traverse(unbounded, request).Move.Y.Should().BeGreaterThan(Fixed64.Zero);
        Traverse(bounded, request).Move.Y.Should().BeGreaterThan(Fixed64.Zero);
        climbStarts.Should().Be(2);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Up * remaining;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        expected.Move.Y.Should().BeGreaterThan(remaining);
        actual.Move.Should().Be(Vector3d.Up * remaining);
        bounded.Motor.IsClimbing.Should().BeTrue();
        bounded.Motor.Handler.Climb!.AttachmentId.Should().Be(1);
        climbStarts.Should().Be(2, "limiting a continuing climb must not reacquire its attachment");
    }

    [Fact]
    public void Approach_ShouldBoundCoreOnlyFallHorizontallyWithoutOptionalWaterModule()
    {
        Vector3d initialVelocity = new((Fixed64)8, (Fixed64)(-2), Fixed64.Zero);
        var unbounded = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: initialVelocity, profile: LocomotionProfile.CreateCoreOnly());
        var bounded = MockMotorAgentTestFactory.CreateFallingAgent(
            startVelocity: initialVelocity, profile: LocomotionProfile.CreateCoreOnly());
        ConfigureMovement(unbounded);
        ConfigureMovement(bounded);
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining + Vector3d.Up;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        bounded.Motor.Handler.Water.Should().BeNull();
        bounded.Motor.Handler.Fly.Should().BeNull();
        bounded.Motor.Handler.Climb.Should().BeNull();
        bounded.Motor.IsFalling.Should().BeTrue();
        expected.Move.X.Should().BeGreaterThan(remaining);
        expected.Move.Y.Should().BeLessThan(initialVelocity.Y * TestWorld.Context.DeltaTime);
        actual.Move.Should().Be(new Vector3d(remaining, expected.Move.Y, expected.Move.Z));
    }

    [Fact]
    public void Approach_ShouldLeavePlatformCarryAndRotationIndependentOfWalkingLimit()
    {
        var unbounded = MockMotorAgentTestFactory.CreatePlatformAgent(motionTransfer: MotionTransfer.PermaLocked);
        var bounded = MockMotorAgentTestFactory.CreatePlatformAgent(motionTransfer: MotionTransfer.PermaLocked);
        FixedQuaternion rotation = FixedQuaternion.AngleAxis((Fixed64)90, Vector3d.Up);
        Fixed4x4 movedPlatform = MockMotorAgentTestFactory.CreatePlatformTransform(Vector3d.Right * 2, rotation);
        foreach (MockMotorAgent agent in new[] { unbounded, bounded })
        {
            ConfigureMovement(agent);
            PlatformLocomotion platform = agent.Motor.Handler.Platform;
            platform.HandlePlatformMovement(agent.GetFootPosition(), agent.Rotation);
            platform.ActivePlatform = new PlatformSnapshot(1, movedPlatform);
        }
        TrekRequest request = CreateRequest(bounded, Vector3d.Right);
        Fixed64 remaining = Fixed64.FromFraction(1, 64);
        Vector3d target = request.FootPosition!.Value + Vector3d.Right * remaining;

        var expected = Traverse(unbounded, request);
        var actual = Traverse(bounded, request, target);

        expected.Move.X.Should().BeGreaterThan(remaining);
        actual.Move.X.Should().Be(remaining);
        expected.Platform.Should().Be(Vector3d.Right * 2);
        expected.Rotation.Should().NotBe(FixedQuaternion.Identity);
        actual.Platform.Should().Be(expected.Platform);
        actual.Rotation.Should().Be(expected.Rotation);
    }

    private static MockMotorAgent CreateGroundAgent(Vector3d? normal = null)
    {
        MockMotorAgent agent = normal.HasValue
            ? MockMotorAgentTestFactory.CreatePlatformAgent(surfaceNormal: normal.Value)
            : MockMotorAgentTestFactory.CreateJumpReadyAgent();
        ConfigureMovement(agent);
        return agent;
    }

    private static MockMotorAgent CreateThreeDimensionalAgent(bool flying, Fixed64 gravity)
    {
        MockMotorAgent agent = flying
            ? MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Gas)
            : MockMotorAgentTestFactory.CreateWaterAgent();
        ConfigureMovement(agent);
        agent.Motor.Handler.Forces.GravityForce = gravity;
        FlyLocomotion fly = agent.Motor.Handler.Fly!;
        fly.MaxFlySpeed = fly.MaxAscendSpeed = (Fixed64)8;
        fly.MaxFlyAcceleration = (Fixed64)1024;
        fly.GravityCompensation = Fixed64.Half;
        WaterLocomotion water = agent.Motor.Handler.Water!;
        water.MaxSwimSpeed = water.MaxSwimSidewaysSpeed = (Fixed64)8;
        water.MaxWaterAcceleration = (Fixed64)1024;
        water.SwimAccelerationModifier = Fixed64.One;
        water.BuoyancyFactor = Fixed64.Half;
        return agent;
    }

    private static void ConfigureMovement(MockMotorAgent agent)
    {
        MoveLocomotion move = agent.Motor.Handler.Move;
        move.MaxSlowSpeed = move.MaxModerateSpeed = move.MaxFastSpeed = (Fixed64)8;
        move.MaxSidewaysSpeed = move.MaxBackwardsSpeed = (Fixed64)8;
        move.MaxGroundAcceleration = move.MaxAirAcceleration = (Fixed64)1024;
        move.WaterDragFactor = Fixed64.Zero;
        agent.Motor.Handler.Forces.GravityForce = (Fixed64)12;
    }

    private static TrekRequest CreateRequest(MockMotorAgent agent, Vector3d direction) => new()
    {
        Origin = agent.Position,
        FootPosition = agent.GetFootPosition(),
        Rotation = agent.Rotation,
        Direction = direction,
        Rate = TrekRate.Fast
    };

    private static (Vector3d Move, Vector3d Platform, FixedQuaternion Rotation) Traverse(
        MockMotorAgent agent,
        TrekRequest request,
        Vector3d? approachTarget = null)
    {
        try
        {
            Vector3d movement;
            Vector3d platform;
            FixedQuaternion rotation;
            bool opened = approachTarget.HasValue
                ? agent.Motor.TryTraversal(request, approachTarget, out movement, out platform, out rotation)
                : agent.Motor.TryTraversal(request, out movement, out platform, out rotation);
            opened.Should().BeTrue();
            return (movement, platform, rotation);
        }
        finally
        {
            agent.Motor.AbortTraversalFrame();
        }
    }

    private sealed class LadderResolver : IClimbAffordanceResolver
    {
        public bool TryResolveClimbAffordance(
            TrekRequest request,
            TransitState currentState,
            out ClimbAffordanceSnapshot snapshot)
        {
            snapshot = new ClimbAffordanceSnapshot(
                ClimbAffordanceKind.Ladder,
                Vector3d.Zero,
                Vector3d.Backward,
                Vector3d.Up,
                affordanceId: 1,
                allowLateralTraverse: false);
            return true;
        }
    }
}
