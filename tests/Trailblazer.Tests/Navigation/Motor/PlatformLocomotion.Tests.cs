using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class PlatformLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

        // Act
        // 1st Frame
        TrailblazerManager.Simulate();
        scout.Simulate();

        PlatformSnapshot platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        Vector3d newPlatformPoint = new(2, 0, 0);
        platformHandle.Transform.SetTranslation(newPlatformPoint);
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle;

        // 2nd Frame
        TrailblazerManager.Simulate();
        scout.Simulate();

        //scout.FrameCondition = new(
        //    TraversalMedium.Ground,
        //    Fixed64.Zero,
        //    new GroundCondition
        //    {
        //        GroundNormal = updatedMatrix.Up,
        //        Platform = new(2, updatedMatrix)
        //    }
        //);

        // Assert
        scout.Position.Should().Be(platformHandle.Transform.Translation);
    }

    [Fact]
    public void Given_HostRefreshesSamePlatformId_When_FinalizeTraversalRuns_Then_ShouldUseUpdatedTransform()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

        TrailblazerManager.Simulate();
        scout.Simulate();

        var movedTransform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: new Vector3d(2, 0, 0));
        var refreshedCondition = new TrekCondition()
        {
            Medium = TraversalMedium.Ground,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, movedTransform)
            },
            CeilingLevel = Fixed64.MAX_VALUE
        };

        TrailblazerManager.Simulate();
        scout.FrameRequest.Origin = scout.Position;
        scout.FrameRequest.FootPosition = scout.GetFootPosition();
        scout.FrameRequest.Rotation = scout.Rotation;

        if (scout.Motor.TryTraversal(scout.FrameRequest, out var velocityDelta, out var positionDelta, out var rotationDelta))
        {
            scout.LastPosition = scout.Position;
            scout.Position += positionDelta + velocityDelta;

            if (rotationDelta != FixedQuaternion.Identity)
                scout.Rotation *= rotationDelta;
        }

        scout.FrameCondition = refreshedCondition;
        scout.Motor.FinalizeTraversal(scout.Position, scout.LastPosition, scout.Rotation, scout.FrameCondition, scout.GetFootPosition());
        scout.FrameRequest.Reset();

        scout.Motor.Handler.Platform.ActivePlatform.Should().NotBeNull();
        scout.Motor.Handler.Platform.ActivePlatform?.Transform.Translation.Should().Be(movedTransform.Translation);
        scout.Motor.Handler.Platform.IsNewPlatform.Should().BeFalse();

        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(movedTransform.Translation);
    }

    [Fact]
    public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

        FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

        // Act
        TrailblazerManager.Simulate();
        scout.Simulate();

        // Apply platform rotation
        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        platformHandle.Transform.SetRotation(rotationChange);
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle; // Ensure platform state is updated

        TrailblazerManager.Simulate();
        scout.Simulate();

        // Assert
        platformHandle.Transform.Rotation.Should().Be(scout.Rotation);
    }

    [Fact]
    public void Given_ScoutOnMovingPlatform_Then_PositionShouldMatchPlatform()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: platform);

        Vector3d expectedPosition = scout.Position;

        // Act
        TrailblazerManager.Simulate();
        scout.Simulate();

        // Move platform
        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        Vector3d movementDelta = new(1, 0, 0);
        platformHandle.Transform.SetTranslation(movementDelta);
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle; // Ensure platform state is updated

        TrailblazerManager.Simulate();
        scout.Simulate();

        // Assert
        scout.Position.Should().Be(expectedPosition + movementDelta);
    }

    [Fact]
    public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritRotation()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

        FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(
            Vector3d.Up,
            Fixed64.FromRaw(0x08000000L)); // Small rotation

        // Act
        TrailblazerManager.Simulate();
        scout.Simulate();

        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        platformHandle.Transform.SetRotation(rotationChange);
        platformHandle.Transform = Fixed4x4.NormalizeRotationMatrix(platformHandle.Transform);
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle; // Ensure platform state is updated

        TrailblazerManager.Simulate();
        scout.Simulate();

        // Assert
        scout.Rotation.Should().Be(platformHandle.Transform.Rotation);
    }

    [Fact]
    public void Given_ScoutOnInertSurface_When_SurfaceMoves_Then_ShouldNotInheritPlatformMotion()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: Vector3d.Zero,
            platformMatrix: platform,
            motionTransfer: MotionTransfer.PermaLocked,
            platformInert: true);

        scout.Motor.Handler.Platform.IsActive.Should().BeFalse();
        scout.Motor.Handler.Platform.MovementTransfer.Should().Be(MotionTransfer.None);

        TrailblazerManager.Simulate();
        scout.Simulate();

        var movedTransform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: new Vector3d(3, 0, 0));
        scout.FrameCondition.GroundState = new GroundCondition
        {
            Platform = new PlatformSnapshot(1, movedTransform, inert: true),
            MotionTransferState = MotionTransfer.PermaLocked
        };

        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(Vector3d.Zero);
        scout.Rotation.Should().Be(FixedQuaternion.Identity);
        scout.Motor.Handler.Platform.IsActive.Should().BeFalse();
        scout.Motor.Handler.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
        scout.Motor.Handler.Platform.MovementTransfer.Should().Be(MotionTransfer.None);
    }

    [Fact]
    public void Given_MovingPlatformBecomesInert_When_TraversalFinalizes_Then_ShouldDetachAndClearTransferState()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(),
            motionTransfer: MotionTransfer.PermaLocked);

        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Motor.Handler.Platform.IsActive.Should().BeTrue();

        scout.FrameCondition.GroundState = new GroundCondition
        {
            Platform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform(), inert: true),
            MotionTransferState = MotionTransfer.PermaLocked
        };

        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Motor.Handler.Platform.ActivePlatform.Should().BeNull();
        scout.Motor.Handler.Platform.IsActive.Should().BeFalse();
        scout.Motor.Handler.Platform.MovementTransfer.Should().Be(MotionTransfer.None);
        scout.Motor.Handler.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void Given_ScoutJumpsBeforePlatformMoves_When_Simulated_Then_ShouldNotInheritFutureVelocity()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform();
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

        // Act 1 - Jump before platform movement
        TrailblazerManager.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Move platform afterward
        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(new Vector3d(3, 0, 0));

        // Act 2 - Simulate next frame after platform movement
        TrailblazerManager.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Assert - we didn't pick up any horizontal velocity from the platform
        scout.Motor.Handler.Move.FrameVelocity.x.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutWithInitTransfer_When_Jumping_Then_ShouldInheritPlatformVelocity()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

        scout.Motor.Handler.Platform.PlatformVelocity = new Vector3d(1, 0, 0);

        TrailblazerManager.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        scout.Motor.Handler.Move.FrameVelocity.x.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutWithPermaLocked_When_PlatformMoves_Then_ScoutFollowsPlatform()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        scout.Motor.Handler.Platform.MovementTransfer = MotionTransfer.PermaLocked;

        TrailblazerManager.Simulate();
        scout.Simulate();

        var moveDelta = new Vector3d(2, 0, 0);
        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(moveDelta);
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle; // Ensure platform state is updated

        TrailblazerManager.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(moveDelta);
    }

    [Fact]
    public void Given_ScoutHoldingOldPlatform_When_HoldTimesOut_Then_ShouldDetach()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platform = scout.Motor.Handler.Platform.ActivePlatform;

        var newMatrix = MockMotorAgentTestFactory.CreatePlatformTransform();
        var platformHandle = new PlatformSnapshot(2, newMatrix);

        scout.Motor.Handler.Platform.SetHoldPlatform(platformHandle);

        bool release = false;
        // Simulate exceeding the max hold frame count
        for (int i = 0; i < PlatformLocomotion.MaxHoldPlatformFrames + 1; i++)
        {
            release = scout.Motor.Handler.Platform.TickHoldOnPlatform();
            if (release)
                break;
        }

        release.Should().BeTrue();
    }

    [Fact]
    public void Given_PlatformLocomotionDisabled_When_Cleared_Then_ShouldResetState()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();

        (scout.Motor.Handler.Platform.ActivePlatform?.Active ?? false).Should().BeTrue();

        scout.Motor.Handler.Platform.IsEnabled = false;

        (scout.Motor.Handler.Platform.ActivePlatform?.Active ?? false).Should().BeFalse();
        scout.Motor.Handler.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void Given_ScoutOnPlatform_When_JumpsWithInitTransfer_Then_InertiaShouldNotDoubleApply()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);

        var initialPlatformVelocity = new Vector3d(1, 0, 0);
        scout.Motor.Handler.Platform.PlatformVelocity = initialPlatformVelocity;

        TrailblazerManager.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        Vector3d velocityAfterJump = scout.Motor.Handler.Move.FrameVelocity;

        velocityAfterJump.x.Should().Be(initialPlatformVelocity.x);
    }

    [Fact]
    public void Given_JumpingScout_When_PlatformIsMoving_Then_ShouldInheritVelocity()
    {
        // Arrange
        var transform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: transform, motionTransfer: MotionTransfer.InitTransfer);

        // Act 1 - Set initial state
        TrailblazerManager.Simulate();
        scout.Simulate();

        // Arrange - Move platform
        var platformHandle = scout.Motor.Handler.Platform.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(new Vector3d(2, 0, 0));
        scout.Motor.Handler.Platform.ActivePlatform = platformHandle; // Ensure platform state is updated

        // Act 2 - Jump from moving platform
        TrailblazerManager.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Assert
        scout.Motor.Handler.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
        scout.Motor.Handler.Move.FrameVelocity.x.Should().Be(scout.Motor.Handler.Platform.PlatformVelocity.x);
    }

    [Fact]
    public void Given_ScoutUsesPlatformInertia_When_Lands_Then_FrameVelocityShouldBeCleared()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform();
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(platformMatrix: platform);
        scout.Motor.Handler.Platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

        scout.Simulate();

        scout.FrameCondition.Medium = TraversalMedium.Ground;

        scout.Motor.FinalizeTraversal(scout.Position, scout.LastPosition, scout.Rotation, scout.FrameCondition, scout.GetFootPosition());
        scout.Motor.Handler.Platform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
    }

}
