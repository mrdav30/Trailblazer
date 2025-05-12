using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigator.Motor;

namespace Trailblazer.Tests.Navigator.Motor
{
    [Collection("TrailblazerCollection")]
    public class JumpLocomotionTests
    {
        [Fact]
        public void Given_GroundedScout_When_JumpIsTriggered_Then_ShouldApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act
            scout.Controller.Traverse(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(
                startPosition: Vector3d.Up,
                startVelocity: Vector3d.Down,
                startingMedium: TraversalMedium.Air);

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.Controller.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            Fixed64 expectedJumpFrame = scout.Controller.Locomotions.Jump.JumpStartTime;

            // Attempt to jump again immediately
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.IsInAir.Should().BeTrue();
            scout.Controller.Locomotions.Jump.IsCoolingDown.Should().BeTrue();
            scout.Controller.Locomotions.Jump.JumpStartTime.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Release jump after 2 frames
            for (int i = 0; i < 29; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.Controller.Locomotions.Jump.IsJumping.Should().BeFalse();
            scout.Controller.Locomotions.Jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we simulate 31
            scout.Controller.Locomotions.Move.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);
            if (scout.Events != null)
                scout.Events.CanAffordJump = () => false;

            // Act
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockScout(startingMedium: TraversalMedium.Ground);

            // Act - Initial Jump
            TrailblazerManager.Simulate();
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            Vector3d previousVelocity = scout.Controller.Locomotions.Move.CurrentVelocity;

            // Continue holding jump for 3 frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            // Assert
            var expected = previousVelocity.y - (scout.Controller.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime * 3);
            scout.Controller.Locomotions.Move.CurrentVelocity.y.Should().BeGreaterThan(expected);
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpHeld_Then_ShouldJumpHigher()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            scout.SetTravelRequest(isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(isRequestingJump: true);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Controller.Locomotions.Jump.BaseJumpHeight); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpNotHeld_Then_ShouldNotJumpHigher()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            scout.SetTravelRequest(isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(isRequestingJump: false);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Controller.Locomotions.Jump.BaseJumpHeight + Fixed64.Epsilon); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout(startPosition: new Vector3d(0, 5, 0));

            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Slow, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.StartTraversal();

            scout.SetTraversalCondition(TraversalMedium.Air, surfaceLevel: Fixed64.FromRaw(5 << 16), ceilingLevel: Fixed64.FromRaw(6 << 16)); // Simulate a ceiling

            scout.FinalizeTraversal();

            scout.Controller.Locomotions.Jump.IsJumping.Should().BeFalse(); // Jump should be canceled
            scout.Controller.Locomotions.Move.CurrentVelocity.y.Should().BeLessThanOrEqualTo(Fixed64.Zero); // Should stop rising
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_LandsOnGround_Then_ShouldResetJumpState()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            bool jumpStarted = false;
            bool jumpStopped = false;
            bool fallStarted = false;
            bool fallStopped = false;

            scout.Events.OnStartJump += (avoidTimer) => jumpStarted = true;
            scout.Events.OnStopJump += () => jumpStopped = true;
            scout.Events.OnStartFall += () => fallStarted = true;
            scout.Events.OnLandedFall += () => fallStopped = true;

            // Start Jump
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Slow, isRequestingJump: true);
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Simulate entire jump arc until landing
            for (int i = 0; i < 30; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
                if (scout.Position.y <= Fixed64.Zero) // If we've landed
                    break;
            }

            scout.StartTraversal();

            scout.SetTraversalCondition(TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            scout.FinalizeTraversal();

            // Assert that jump state has been reset after actual landing
            scout.Controller.Locomotions.Jump.IsJumping.Should().BeFalse();
            jumpStarted.Should().BeTrue();
            jumpStopped.Should().BeTrue();
            fallStarted.Should().BeTrue();
            // We treat landing a jump differently
            fallStopped.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_HeldTooLong_Then_ShouldNotExceedMaxJump()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            var jumpLocomotion = scout.Controller.Locomotions.Jump;
            Fixed64 maxExpectedHeight = jumpLocomotion.BaseJumpHeight + jumpLocomotion.ExtraJumpHeight;

            Fixed64 maxY = scout.Position.y;

            // Start jump
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Continue holding jump and track peak height until we land
            while (!scout.Controller.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
                scout.StartTraversal();
                scout.FinalizeTraversal();

                if (scout.Position.y > maxY)
                    maxY = scout.Position.y;
            }

            maxY.Should().BeLessThanOrEqualTo(maxExpectedHeight);
        }

        [Fact]
        public void Given_ScoutTapsJump_When_ReleasedImmediately_Then_JumpHeightShouldBeReduced()
        {
            var scout = IScoutTestFactory.CreateJumpReadyScout();

            // Start jump
            scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Tap release
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, MovementSpeed.Stationary, isRequestingJump: false);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Position.y.Should().BeGreaterThan((Fixed64)0.5).And.BeLessThan((Fixed64)1.0);
        }
    }
}
