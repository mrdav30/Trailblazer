using FixedMathSharp;
using FluentAssertions;
using Moq;
using System;
using Trailblazer.Navigation.Turning;
using Xunit;

namespace Trailblazer.Tests.Navigation.Turning
{
    public class NavTurningTests
    {
        [Fact]
        public void SimulateTurn_Should_BufferAutoTurn_After_Collision_And_Movement()
        {
            NavTurning turn = new();
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
            turn.SimulateTurn(mockNav);
            turn.TargetReached.Should().BeTrue();

            // call again: now consumes buffer, TargetReached → false
            turn.SimulateTurn(mockNav);
            turn.TargetReached.Should().BeFalse();
        }

        [Fact]
        public void NeedsTurn_ShouldReturnFalse_When_DirectionsAreIdentical()
        {
            var turning = new NavTurning(Fixed64.One);
            var fwd = Vector3d.Right;

            // identical forward ⇒ angle = 0 ≤ min threshold
            turning.NeedsTurn(fwd, fwd).Should().BeFalse();
        }

        [Fact]
        public void NeedsTurn_ShouldReturnTrue_When_AngleIsLarge()
        {
            var turning = new NavTurning(Fixed64.One);

            // 90° turn definitely > min threshold
            turning.NeedsTurn(Vector3d.Forward, Vector3d.Right).Should().BeTrue();
        }

        [Fact]
        public void SimulateTurn_ShouldBufferCollisionTurn_AfterMovingPastThreshold()
        {
            // Arrange
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                // We’re initially facing +X
                Forward = Vector3d.Right,
                LastPosition = Vector3d.Zero,
                // Move along +Y so the collision‐turn delta = (0,1,0) != forward
                Position = new Vector3d(0, 0.1, 0)
            };

            // Act — signal a collision
            turning.NotifyCollision();

            // First simulate: buffers the turn but does NOT consume it
            turning.SimulateTurn(nav);
            turning.TargetReached
                .Should().BeTrue("we buffered a turn but haven't applied it yet");

            // Second simulate: consumes the buffer and kicks off the turn
            turning.SimulateTurn(nav);
            turning.TargetReached
                .Should().BeFalse("after consuming the buffer, we should be mid‐turn");
        }

        [Fact]
        public void SimulateTurn_ShouldNotBuffer_When_CanTurnOnCollisionVetoes()
        {
            var turning = new NavTurning(Fixed64.One);
            turning.CanTurnOnCollision = () => false;

            var nav = new MockTurnAgent
            {
                LastPosition = Vector3d.Zero,
                Position = new Vector3d(0.1, 0, 0),
                Forward = Vector3d.Right
            };

            turning.NotifyCollision();

            // Try twice—never buffers because veto is in place
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeTrue();

            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeTrue();
        }

        [Fact]
        public void SimulateTurn_ShouldCompleteTurn_When_TargetRotationEqualsCurrentRotation()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                Rotation = FixedQuaternion.Identity,
                LastPosition = Vector3d.Zero,
                Position = Vector3d.Zero,
                Forward = Vector3d.Right
            };

            // Request a “turn” to the exact same direction → immediate arrival
            turning.RequestTurnDirection(nav.Forward, nav.Forward);
            turning.SimulateTurn(nav);

            turning.TargetReached.Should().BeTrue("we were already facing the target, so we snap immediately");
            nav.Rotation.Should().Be(FixedQuaternion.Identity);
        }

        [Fact]
        public void StopTurn_ShouldSet_TargetReached_True()
        {
            var turning = new NavTurning(Fixed64.One);

            // force into mid-turn
            turning.RequestTurnDirection(Vector3d.Right, Vector3d.Forward);
            // StopTurn is a public API
            turning.StopTurn();

            turning.TargetReached.Should().BeTrue();
        }


        [Fact]
        public void SimulateTurn_Throws_If_OnInitialize_NotCalled()
        {
            var turning = new NavTurning();
            var nav = new MockTurnAgent();
            Action act = () => turning.SimulateTurn(nav);
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*must be called before SimulateTurn()*");
        }

        [Fact]
        public void SimulateTurn_DoesNothing_When_CanTurnIsFalse()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                Position = Vector3d.Zero,
                LastPosition = Vector3d.Zero,
                Forward = Vector3d.Right,
                Rotation = FixedQuaternion.Identity
            };
            turning.CanTurn = false;

            turning.RequestTurnDirection(Vector3d.Right, Vector3d.Forward);
            turning.SimulateTurn(nav);

            // Should never leave the arrived state nor change rotation
            turning.TargetReached.Should().BeTrue();
            nav.Rotation.Should().Be(FixedQuaternion.Identity);
        }

        [Fact]
        public void RequestTurnDirection_Ignores_SmallAngles()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent()
            {
                Forward = Vector3d.Right
            };
            // forward and target differ by less than _minTurnRequiredAngle
            var tinyOffset = new Vector3d(1, 0.00001, 0).Normal;

            turning.RequestTurnDirection(nav.Forward, tinyOffset);
            turning.TargetReached.Should().BeTrue("no turn requested for angles below threshold");

            // And SimulateTurn should immediately return (no buffering)
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeTrue();
        }

        [Fact]
        public void SimulateTurn_WithMaxInterpolation_JumpsDirectlyToTarget()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                Position = Vector3d.Zero,
                LastPosition = Vector3d.Zero,
                Forward = Vector3d.Right,
                Rotation = FixedQuaternion.Identity
            };

            // Request a 90° turn with interpolation = 1 ⇒ instant snap
            turning.RequestTurnDirection(nav.Forward, Vector3d.Forward, interpolation: Fixed64.One);
            turning.SimulateTurn(nav);

            // After the one call, we should have TargetReached = true and rotation == target
            turning.TargetReached.Should().BeTrue();
            nav.Rotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Forward));
        }

        [Fact]
        public void SimulateTurn_AfterArrival_RemainsIdle()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                Position = Vector3d.Zero,
                LastPosition = Vector3d.Zero,
                Forward = Vector3d.Right,
                Rotation = FixedQuaternion.Identity
            };

            // Force into arrived state
            turning.RequestTurnDirection(nav.Forward, nav.Forward);
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeTrue();

            // Multiple subsequent calls should leave rotation unchanged
            nav.Rotation = FixedQuaternion.FromDirection(Vector3d.Forward);
            for (int i = 0; i < 3; i++)
                turning.SimulateTurn(nav);

            nav.Rotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Forward));
        }

        [Fact]
        public void MultipleCollisions_OnlyFirst_IsBuffered()
        {
            var turning = new NavTurning(Fixed64.One);
            var nav = new MockTurnAgent
            {
                LastPosition = Vector3d.Zero,
                Position = new Vector3d(0, 0.2, 0),
                Forward = Vector3d.Right
            };
            turning.CanTurnOnCollision = () => true;

            // Signal two collisions before buffer is consumed
            turning.NotifyCollision();
            turning.NotifyCollision();

            // First simulate: buffer only one turn
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeTrue();

            // Consume buffer
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeFalse();

            // No further buffers queued:
            turning.SimulateTurn(nav);
            turning.TargetReached.Should().BeFalse();
        }
    }
}
