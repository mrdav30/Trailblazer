using Xunit;
using FluentAssertions;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class FallLocomotionTests
    {
        [Fact]
        public void Given_FallingScout_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingAgent();

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.Navigator.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.Controller.Traverse(Vector3d.Zero, TrekRate.Stationary, isRequestingJump: true);
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_FallingScout_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateFallingAgent();

            // Act - First Frame (Falling)
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.IsInAir.Should().BeTrue();

            // Simulate hitting the ground before the next frame
            scout.SetTraversalCondition(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundCondition
                {
                    BaseObject = null,
                    GroundMatrix = Fixed4x4.Identity,
                }
            );

            // 2nd Frame
            TrailblazerManager.Simulate();

            // Act - Second Frame (After Ground Contact)
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Assert
            scout.Controller.IsGrounded.Should().BeTrue();
        }

        [Fact]
        public void Given_AirborneScout_When_SimulatedOverMultipleFrames_Then_VelocityShouldMatchGravity()
        {
            // Arrange
            var scout = IScoutTestFactory.CreateMockAgent(
                startPosition: new Vector3d(0, 100, 0),
                startingMedium: TraversalMedium.Air
            );
            Vector3d expectedVelocity = Vector3d.Zero;  // store impulse-based velocity change per frame

            // Act - Simulate falling for 5 frames
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();

                // Calculate expected velocity update from gravity impulse
                expectedVelocity.y += -scout.Controller.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;
            }

            // Assert
            scout.Controller.Locomotions.Move.CurrentVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutInAir_When_NoMovement_Then_ShouldFallNaturally()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: initialPosition, startingMedium: TraversalMedium.Air);

            for (int i = 0; i < 20; i++) // Simulate multiple frames
            {
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Position.y.Should().BeLessThan(initialPosition.y); // Should be falling
        }

        [Fact]
        public void Given_ScoutInAir_When_MovesForward_Then_ShouldStillBeAffectedByGravity()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var scout = IScoutTestFactory.CreateFallingAgent(startPosition: initialPosition);

            for (int i = 0; i < 20; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(new Vector3d(1, 0, 0), TrekRate.Moderate);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Position.y.Should().BeLessThan(initialPosition.y); // Gravity should still apply
            scout.Position.x.Should().BeGreaterThan(Fixed64.Zero); // Should also move forward
        }

        [Fact]
        public void Given_ScoutFallsFar_When_Lands_Then_ShouldTriggerMaxFallHeightEvent()
        {
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 10, 0), startingMedium: TraversalMedium.Air);
            scout.Controller.Locomotions.Fall.MaxFallHeight = Fixed64.One;

            bool eventCalled = false;
            scout.Events.OnMaxFallHeightReached += () => eventCalled = true;

            while (!scout.Controller.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            eventCalled.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutFallsAndLands_When_FallHeightIsValid_Then_ShouldCallOnStopFallWithHeight()
        {
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 10, 0), startingMedium: TraversalMedium.Air);

            Fixed64 fallHeight = Fixed64.Zero;
            scout.Events.OnStopFall += (height) => fallHeight = height;

            while (!scout.Controller.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            fallHeight.Should().NotBeNull();
            fallHeight.Should().BeGreaterThan(Fixed64.One);
        }

        [Fact]
        public void Given_ScoutSlidesDownhill_When_SlopeIsShallow_Then_ShouldNotStartFalling()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)10);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var scout = IScoutTestFactory.CreatePlatformAgent(
                startPosition: new Vector3d(0, 0, 0), platformMatrix: platform);

            scout.Controller.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.Controller.Locomotions.Fall.IsFalling.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutSlidesDownhill_When_SlopeIsSteep_Then_ShouldStartFalling()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = IScoutTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var scout = IScoutTestFactory.CreatePlatformAgent(
                startPosition: new Vector3d(0, 0, 0), platformMatrix: platform);

            scout.Controller.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            for (int i = 0; i < 2; i++)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            scout.Controller.Locomotions.Slide.IsSliding.Should().BeTrue();
            scout.Controller.Locomotions.Fall.IsFalling.Should().BeTrue();
        }

        [Fact]
        public void Given_ScoutStartsFallingMidJump_When_StillRising_Then_ShouldNotTriggerFallStart()
        {
            var scout = IScoutTestFactory.CreateJumpReadyAgent();

            bool fallTriggered = false;
            scout.Events.OnStartFall += () => fallTriggered = true;

            // Start jump
            scout.SetTravelRequest(Vector3d.Zero, TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            // Simulate a few frames of upward motion
            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.SetTravelRequest(Vector3d.Zero, TrekRate.Stationary, isRequestingJump: true);
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            fallTriggered.Should().BeFalse();
        }

        [Fact]
        public void Given_ScoutFallsZeroDistance_When_Lands_Then_FallHeightShouldBeZero()
        {
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 0, 0), startingMedium: TraversalMedium.Air);
            scout.SetTraversalCondition(TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            TrailblazerManager.Simulate();
            scout.StartTraversal();
            scout.FinalizeTraversal();

            scout.Controller.Locomotions.Fall.FallHeight.Should().Be(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutFalls_When_Lands_Then_FallStartShouldBeGreaterThanFallEnd()
        {
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 20, 0), startingMedium: TraversalMedium.Air);

            while (!scout.Controller.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.StartTraversal();
                scout.FinalizeTraversal();
            }

            var fallLocomotion = scout.Controller.Locomotions.Fall;
            fallLocomotion.FallStart.Should().BeGreaterThan(fallLocomotion.FallEnd);
            fallLocomotion.FallHeight.Should().Be(fallLocomotion.FallStart - fallLocomotion.FallEnd);
        }

        [Fact]
        public void Given_ScoutFalls_When_Disabled_Then_FallStateShouldReset()
        {
            var scout = IScoutTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 10, 0), startingMedium: TraversalMedium.Air);

            scout.Controller.Locomotions.Fall.IsFalling = true;
            scout.Controller.Locomotions.Fall.FallStart = (Fixed64)10;

            scout.Controller.Locomotions.Fall.IsEnabled = false;

            scout.Controller.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.Controller.Locomotions.Fall.FallStart.Should().Be(Fixed64.Zero);
        }
    }
}
