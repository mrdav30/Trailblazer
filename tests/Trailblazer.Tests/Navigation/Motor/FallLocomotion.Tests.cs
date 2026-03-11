using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor
{
    [Collection("TrailblazerCollection")]
    public class FallLocomotionTests
    {
        [Fact]
        public void Given_FallingAgent_When_JumpIsTriggered_Then_ShouldNotJump()
        {
            // Arrange
            var agent = MockMotorAgentTestFactory.CreateFallingAgent();

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -agent.Motor.Locomotions.Move.GravityForce
                * TrailblazerManager.DeltaTime;

            agent.FrameRequest.IsRequestingJump = true;

            // Act
            agent.Simulate();

            // Assert
            agent.Motor.Locomotions.Move.FrameVelocity.Should()
                .BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_FallingAgent_When_GroundIsDetected_Then_ShouldTransitionToGrounded()
        {
            // Arrange
            var agent = MockMotorAgentTestFactory.CreateFallingAgent();

            // Act - First Frame (Falling)
            TrailblazerManager.Simulate();
            agent.Simulate();

            // Assert
            agent.Motor.IsInAir.Should().BeTrue();

            // Simulate hitting the ground before the next frame
            agent.FrameCondition = new(
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
            agent.Simulate();

            // Assert
            agent.Motor.IsGrounded.Should().BeTrue();
        }

        [Fact]
        public void Given_AirborneAgent_When_SimulatedOverMultipleFrames_Then_VelocityShouldMatchGravity()
        {
            // Arrange
            var agent = MockMotorAgentTestFactory.CreateMockAgent(
                startPosition: new Vector3d(0, 100, 0),
                startingMedium: TraversalMedium.Air
            );
            Vector3d expectedVelocity = Vector3d.Zero;  // store impulse-based velocity change per frame

            // Act - Simulate falling for 5 frames
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();

                // Calculate expected velocity update from gravity impulse
                expectedVelocity.y += -agent.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;
            }

            // Assert
            agent.Motor.Locomotions.Move.FrameVelocity.Should().BeApproximately(
                expectedVelocity,
                Fixed64.Epsilon);
        }

        [Fact]
        public void Given_AgentInAir_When_NoMovement_Then_ShouldFallNaturally()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var agent = MockMotorAgentTestFactory.CreateMockAgent(startPosition: initialPosition, startingMedium: TraversalMedium.Air);

            for (int i = 0; i < 20; i++) // Simulate multiple frames
            {
                agent.Simulate();
            }

            agent.Position.y.Should().BeLessThan(initialPosition.y); // Should be falling
        }

        [Fact]
        public void Given_AgentInAir_When_MovesForward_Then_ShouldStillBeAffectedByGravity()
        {
            var initialPosition = new Vector3d(0, 10, 0);
            var agent = MockMotorAgentTestFactory.CreateFallingAgent(startPosition: initialPosition);

            for (int i = 0; i < 20; i++)
            {
                TrailblazerManager.Simulate();
                agent.FrameRequest.Direction = Vector3d.Right;
                agent.FrameRequest.Rate = TrekRate.Moderate;
                agent.Simulate();
            }

            agent.Position.y.Should().BeLessThan(initialPosition.y); // Gravity should still apply
            agent.Position.x.Should().BeGreaterThan(Fixed64.Zero); // Should also move forward
        }

        [Fact]
        public void Given_AgentFallsFar_When_Lands_Then_ShouldTriggerMaxFallHeightEvent()
        {
            var agent = MockMotorAgentTestFactory.CreateMockAgent(
                startPosition: new Vector3d(0, 10, 0),
                startingMedium: TraversalMedium.Air);
            agent.Motor.Locomotions.Fall.MaxFallHeight = Fixed64.One;

            bool eventCalled = false;
            agent.Motor.Events.OnMaxFallHeightReached += () => eventCalled = true;

            while (!agent.Motor.IsGrounded)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
            }

            eventCalled.Should().BeTrue();
        }

        [Fact]
        public void Given_AgentFallsAndLands_When_FallHeightIsValid_Then_ShouldCallOnStopFallWithHeight()
        {
            var agent = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(0, 10, 0), startingMedium: TraversalMedium.Air);

            Fixed64 fallHeight = Fixed64.Zero;
            agent.Motor.Events.OnStopFall += (height) => fallHeight = height;

            while (!agent.Motor.IsGrounded)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
            }

            fallHeight.Should().NotBeNull();
            fallHeight.Should().BeGreaterThan(Fixed64.One);
        }

        [Fact]
        public void Given_AgentSlidesDownhill_When_SlopeIsShallow_Then_ShouldNotStartFalling()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)10);
            var platform = MockMotorAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
                startPosition: new Vector3d(0, 0, 0), platformMatrix: platform);

            agent.Motor.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            TrailblazerManager.Simulate();
            agent.Simulate();

            agent.Motor.Locomotions.Fall.IsFalling.Should().BeFalse();
        }

        [Fact]
        public void Given_AgentSlidesDownhill_When_SlopeIsSteep_Then_ShouldStartFalling()
        {
            var slopeAngle = FixedMath.DegToRad((Fixed64)60);
            var platform = MockMotorAgentTestFactory.CreatePlatform(
                platformRotation: FixedQuaternion.FromEulerAngles(slopeAngle, Fixed64.Zero, Fixed64.Zero));

            var agent = MockMotorAgentTestFactory.CreatePlatformAgent(
                startPosition: new Vector3d(0, 0, 0), platformMatrix: platform);

            agent.Motor.Locomotions.Slide.SlopeLimit = (Fixed64)45;

            for (int i = 0; i < 2; i++)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
            }

            agent.Motor.Locomotions.Slide.IsSliding.Should().BeTrue();
            agent.Motor.Locomotions.Fall.IsFalling.Should().BeTrue();
        }

        [Fact]
        public void Given_AgentStartsFallingMidJump_When_StillRising_Then_ShouldNotTriggerFallStart()
        {
            var agent = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            bool fallTriggered = false;
            agent.Motor.Events.OnStartFall += () => fallTriggered = true;

            // Start jump

            agent.FrameRequest.IsRequestingJump = true;

            TrailblazerManager.Simulate();
            agent.Simulate();

            // Simulate a few frames of upward motion
            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
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
                startingMedium: TraversalMedium.Air);

            agent.FrameCondition.Medium = TraversalMedium.Ground;
            agent.FrameCondition.SurfaceLevel = Fixed64.Zero;

            TrailblazerManager.Simulate();
            agent.Simulate();

            agent.Motor.Locomotions.Fall.FallHeight.Should().Be(Fixed64.Zero);
        }

        [Fact]
        public void Given_AgentFalls_When_Lands_Then_FallStartShouldBeGreaterThanFallEnd()
        {
            var agent = MockMotorAgentTestFactory.CreateFallingAgent(new(0, 20, 0));

            while (!agent.Motor.IsGrounded)
            {
                TrailblazerManager.Simulate();
                agent.Simulate();
            }

            var fallLocomotion = agent.Motor.Locomotions.Fall;
            fallLocomotion.FallStart.Should().BeGreaterThan(fallLocomotion.FallEnd);
            fallLocomotion.FallHeight.Should().Be(fallLocomotion.FallStart - fallLocomotion.FallEnd);
        }

        [Fact]
        public void Given_AgentFalls_When_Disabled_Then_FallStateShouldReset()
        {
            var agent = MockMotorAgentTestFactory.CreateMockAgent(
                new(0, 10, 0),
                startingMedium: TraversalMedium.Air);

            agent.Motor.Locomotions.Fall.IsFalling = true;
            agent.Motor.Locomotions.Fall.FallStart = (Fixed64)10;

            agent.Motor.Locomotions.Fall.IsEnabled = false;

            agent.Motor.Locomotions.Fall.IsFalling.Should().BeFalse();
            agent.Motor.Locomotions.Fall.FallStart.Should().Be(Fixed64.Zero);
        }

        [Fact]
        public void Given_ScoutFalling_When_CalculatingMaxSpeed_Then_ControlMultiplierShouldApply()
        {
            var scout = MockMotorAgentTestFactory.CreateFallingAgent();

            scout.Motor.Locomotions.Move.MaxSidewaysSpeed = (Fixed64)4;
            scout.Motor.Locomotions.Fall.FallControlMultiplier = (Fixed64)0.25;

            var speed = scout.Motor.MaxHoritzontalSpeedInDirection(Vector3d.Right, TrekRate.Moderate);

            speed.Should().BeApproximately((Fixed64)1, Fixed64.Epsilon);
        }
    }
}
