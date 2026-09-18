using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using System;
using Trailblazer.Navigation.Turning;
using Xunit;

namespace Trailblazer.Tests.Navigation.Turning;

public class NavTurningTests
{
    [Fact]
    public void Construction_ShouldRejectUnusableContexts()
    {
        Action missing = () => _ = new NavTurning(null!, Fixed64.One);

        TrailblazerWorldContext disposedContext = TrailblazerWorldContext.CreateOwned();
        disposedContext.Dispose();
        Action disposed = () => _ = new NavTurning(disposedContext, Fixed64.One);

        var inactiveWorld = new GridWorld();
        using TrailblazerWorldContext inactiveContext = TrailblazerWorldContext.Attach(inactiveWorld);
        inactiveWorld.Dispose();
        Action inactive = () => _ = new NavTurning(inactiveContext, Fixed64.One);

        missing.Should().Throw<ArgumentNullException>();
        disposed.Should().Throw<ObjectDisposedException>();
        inactive.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SimulateTurn_Should_BufferAutoTurn_After_Collision_And_Movement()
    {
        NavTurning turn = new(TestWorld.Context, Fixed64.One);
        turn.OnInitialize(radius: (Fixed64)1);
        MockTurnAgent mockNav = new()
        {
            Position = new Vector3d(2, 0, 0),
            LastPosition = Vector3d.Zero,
            Forward = Vector3d.Forward,
            Rotation = FixedQuaternion.Identity
        };

        turn.CanTurnOnCollision = () => true;

        // signal collision
        turn.NotifyCollision();

        // first call: buffers but does not apply (TargetReached still true)
        turn.TrySimulateTurn(mockNav.Position, mockNav.LastPosition, mockNav.Forward, mockNav.Rotation, out _);
        turn.TargetReached.Should().BeTrue();

        // call again: now consumes buffer, TargetReached → false
        turn.TrySimulateTurn(mockNav.Position, mockNav.LastPosition, mockNav.Forward, mockNav.Rotation, out _);
        turn.TargetReached.Should().BeFalse();
    }

    [Fact]
    public void NeedsTurn_ShouldReturnFalse_When_DirectionsAreIdentical()
    {
        var fwd = Vector3d.Right;

        // identical forward ⇒ angle = 0 ≤ min threshold
        NavTurning.NeedsTurn(fwd, fwd).Should().BeFalse();
    }

    [Fact]
    public void NeedsTurn_ShouldReturnTrue_When_AngleIsLarge()
    {
        // 90° turn definitely > min threshold
        NavTurning.NeedsTurn(Vector3d.Forward, Vector3d.Right).Should().BeTrue();
    }

    [Fact]
    public void NeedsTurn_ShouldCompareAgainstFullPhysicalQuaternionAngle()
    {
        Fixed64 physicalAngle = (Fixed64)8;
        Vector3d targetDirection = FixedQuaternion.AngleAxis(physicalAngle, Vector3d.Up)
            .Rotate(Vector3d.Forward);

        NavTurning.NeedsTurn(
                Vector3d.Forward,
                targetDirection,
                minAngle: (Fixed64)5)
            .Should().BeTrue("the v7 quaternion angle is the full 8-degree physical rotation, not its 4-degree half-angle");
    }

    [Fact]
    public void SimulateTurn_ShouldBufferCollisionTurn_AfterMovingPastThreshold()
    {
        // Arrange
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent
        {
            // We’re initially facing +X
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity,
            LastPosition = Vector3d.Zero,
            // Move along +Y so the collision‐turn delta = (0,1,0) != forward
            Position = Vector3d.FromDouble(0, 0.1, 0)
        };

        // Act — signal a collision
        turning.NotifyCollision();

        // First simulate: buffers the turn but does NOT consume it
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached
            .Should().BeTrue("we buffered a turn but haven't applied it yet");

        // Second simulate: consumes the buffer and kicks off the turn
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached
            .Should().BeFalse("after consuming the buffer, we should be mid‐turn");
    }

    [Fact]
    public void SimulateTurn_ShouldNotBuffer_When_CanTurnOnCollisionVetoes()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        turning.CanTurnOnCollision = () => false;

        var nav = new MockTurnAgent
        {
            LastPosition = Vector3d.Zero,
            Position = Vector3d.FromDouble(0.1, 0, 0),
            Forward = Vector3d.Right
        };

        turning.NotifyCollision();

        // Try twice—never buffers because veto is in place
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeTrue();

        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeTrue();
    }

    [Fact]
    public void SimulateTurn_ShouldKeepCollisionArmedUntilMovementPassesThreshold()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        turning.OnInitialize(Fixed64.One);
        turning.NotifyCollision();
        Vector3d belowThreshold = Vector3d.Right * Fixed64.FromFraction(1, 240);

        turning.TrySimulateTurn(
                belowThreshold,
                Vector3d.Zero,
                Vector3d.Forward,
                FixedQuaternion.Identity,
                out _)
            .Should().BeFalse();
        turning.TargetReached.Should().BeTrue();

        Vector3d beyondThreshold = Vector3d.Right * Fixed64.Quarter;
        turning.TrySimulateTurn(
                beyondThreshold,
                Vector3d.Zero,
                Vector3d.Forward,
                FixedQuaternion.Identity,
                out _)
            .Should().BeFalse();
        turning.TrySimulateTurn(
                beyondThreshold,
                Vector3d.Zero,
                Vector3d.Forward,
                FixedQuaternion.Identity,
                out _)
            .Should().BeTrue();
        turning.TargetReached.Should().BeFalse();
    }

    [Fact]
    public void SimulateTurn_ShouldCompleteTurn_When_TargetRotationEqualsCurrentRotation()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent
        {
            Rotation = FixedQuaternion.Identity,
            LastPosition = Vector3d.Zero,
            Position = Vector3d.Zero,
            Forward = Vector3d.Right
        };

        // Request a “turn” to the exact same direction → immediate arrival
        turning.RequestTurnDirection(nav.Forward, nav.Forward);
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);

        turning.TargetReached.Should().BeTrue("we were already facing the target, so we snap immediately");
        nav.Rotation.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void StopTurn_ShouldSet_TargetReached_True()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);

        // force into mid-turn
        turning.RequestTurnDirection(Vector3d.Right, Vector3d.Forward);
        // StopTurn is a public API
        turning.StopTurn();

        turning.TargetReached.Should().BeTrue();
    }


    [Fact]
    public void SimulateTurn_DoesNothing_When_CanTurnIsFalse()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent
        {
            Position = Vector3d.Zero,
            LastPosition = Vector3d.Zero,
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity
        };
        turning.CanTurn = false;

        turning.RequestTurnDirection(Vector3d.Right, Vector3d.Forward);
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);

        // Should never leave the arrived state nor change rotation
        turning.TargetReached.Should().BeTrue();
        nav.Rotation.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void RequestTurnDirection_Ignores_SmallAngles()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent()
        {
            Forward = Vector3d.Right
        };
        // forward and target differ by less than _minTurnRequiredAngle
        var tinyOffset = Vector3d.FromDouble(1, 0.00001, 0).Normalized;

        turning.RequestTurnDirection(nav.Forward, tinyOffset);
        turning.TargetReached.Should().BeTrue("no turn requested for angles below threshold");

        // And SimulateTurn should immediately return (no buffering)
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void SimulateTurn_WithMaxInterpolation_ReturnsCanonicalForwardRotation(int targetScale)
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        // A half-turn faces -Z. Zero and positive scaled +Z targets resolve to identity.
        var rotation = new FixedQuaternion(Fixed64.Zero, Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        var target = new Vector3d(0, 0, targetScale);

        turning.RequestTurnDirection(Vector3d.Backward, target, interpolation: Fixed64.One);

        turning.TrySimulateTurn(
                Vector3d.Zero, Vector3d.Zero, Vector3d.Backward, rotation, out FixedQuaternion applied)
            .Should().BeTrue();
        applied.Should().Be(FixedQuaternion.Identity);
        turning.TargetRotation.Should().Be(FixedQuaternion.Identity);
        turning.TargetReached.Should().BeTrue();
    }

    [Fact]
    public void RequestTurnDirection_RejectedRequestPreservesBufferedTargetAndInterpolation()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var rotation = new FixedQuaternion(Fixed64.Zero, Fixed64.One, Fixed64.Zero, Fixed64.Zero);

        turning.RequestTurnDirection(Vector3d.Backward, Vector3d.Forward, interpolation: Fixed64.One);
        turning.RequestTurnDirection(Vector3d.Backward, Vector3d.Backward, interpolation: Fixed64.Half);

        turning.TrySimulateTurn(
                Vector3d.Zero, Vector3d.Zero, Vector3d.Backward, rotation, out FixedQuaternion applied)
            .Should().BeTrue();
        applied.Should().Be(FixedQuaternion.Identity,
            "the rejected request must preserve both the pending target and its full interpolation");
        turning.TargetReached.Should().BeTrue();
    }

    [Fact]
    public void SimulateTurn_AfterArrival_RemainsIdle()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent
        {
            Position = Vector3d.Zero,
            LastPosition = Vector3d.Zero,
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity
        };

        // Force into arrived state
        turning.RequestTurnDirection(nav.Forward, nav.Forward);
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeTrue();

        // Multiple subsequent calls should leave rotation unchanged
        nav.Rotation = FixedQuaternion.FromDirection(Vector3d.Forward);
        for (int i = 0; i < 3; i++)
            turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);

        nav.Rotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Forward));
    }

    [Fact]
    public void MultipleCollisions_OnlyFirst_IsBuffered()
    {
        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var nav = new MockTurnAgent
        {
            LastPosition = Vector3d.Zero,
            Position = Vector3d.FromDouble(0, 0.2, 0),
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity
        };
        turning.CanTurnOnCollision = () => true;

        // Signal two collisions before buffer is consumed
        turning.NotifyCollision();
        turning.NotifyCollision();

        // First simulate: buffer only one turn
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeTrue();

        // Consume buffer
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeFalse();

        // No further buffers queued:
        turning.TrySimulateTurn(nav.Position, nav.LastPosition, nav.Forward, nav.Rotation, out _);
        turning.TargetReached.Should().BeFalse();
    }
}
