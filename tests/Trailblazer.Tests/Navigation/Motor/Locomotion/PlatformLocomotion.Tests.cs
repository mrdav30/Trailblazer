using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public class PlatformLocomotionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PlatformSnapshot_ValueIdentityShouldUseStablePlatformIdOnly()
    {
        PlatformSnapshot first = new(7, Fixed4x4.Identity);
        PlatformSnapshot samePlatform = new(
            7,
            MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 0)),
            inert: true);
        PlatformSnapshot otherPlatform = new(8, Fixed4x4.Identity);

        first.Should().Be(samePlatform);
        (first == samePlatform).Should().BeTrue();
        first.GetHashCode().Should().Be(samePlatform.GetHashCode());
        (first != otherPlatform).Should().BeTrue();
        first.Equals((object?)null).Should().BeFalse();
        first.Equals("7").Should().BeFalse();
        first.Equals((object)otherPlatform).Should().BeFalse();
    }

    [Fact]
    public void Given_ScoutOnMovingPlatform_When_SimulateRuns_Then_PositionShouldMatchPlatform()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        // Act
        // 1st Frame
        TestWorld.Context.Simulate();
        scout.Simulate();

        PlatformSnapshot platformHandle = platformLocomotion.ActivePlatform ?? default;
        Vector3d newPlatformPoint = new(2, 0, 0);
        platformHandle.Transform.SetTranslation(newPlatformPoint);
        platformLocomotion.ActivePlatform = platformHandle;

        // 2nd Frame
        TestWorld.Context.Simulate();
        scout.Simulate();

        //scout.FrameCondition = new(
        //    TraversalMedium.Solid,
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
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        TestWorld.Context.Simulate();
        scout.Simulate();

        var movedTransform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: new Vector3d(2, 0, 0));
        var refreshedCondition = new TrekCondition()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition
            {
                Platform = new PlatformSnapshot(1, movedTransform)
            },
            CeilingLevel = Fixed64.MaxValue
        };

        TestWorld.Context.Simulate();
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

        PlatformSnapshot activePlatform = TestRequire.NotNull(platformLocomotion.ActivePlatform);
        activePlatform.Transform.Translation.Should().Be(movedTransform.Translation);
        platformLocomotion.IsNewPlatform.Should().BeFalse();

        TestWorld.Context.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(movedTransform.Translation);
    }

    [Fact]
    public void Given_ScoutOnRotatingPlatform_When_PlatformRotates_Then_ScoutShouldMatchRotation()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)); // Small rotation

        // Act
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Apply platform rotation
        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        platformHandle.Transform.SetRotation(rotationChange);
        platformLocomotion.ActivePlatform = platformHandle; // Ensure platform state is updated

        TestWorld.Context.Simulate();
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
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        Vector3d expectedPosition = scout.Position;

        // Act
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Move platform
        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        Vector3d movementDelta = new(1, 0, 0);
        platformHandle.Transform.SetTranslation(movementDelta);
        platformLocomotion.ActivePlatform = platformHandle; // Ensure platform state is updated

        TestWorld.Context.Simulate();
        scout.Simulate();

        // Assert
        scout.Position.Should().Be(expectedPosition + movementDelta);
    }

    [Fact]
    public void Given_ScoutOnRotatingPlatform_When_Simulated_Then_ShouldInheritRotation()
    {
        // Arrange
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        FixedQuaternion rotationChange = FixedQuaternion.FromAxisAngle(
            Vector3d.Up,
            Fixed64.FromRaw(0x08000000L)); // Small rotation

        // Act
        TestWorld.Context.Simulate();
        scout.Simulate();

        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        platformHandle.Transform.SetRotation(rotationChange);
        platformHandle.Transform = Fixed4x4.NormalizeRotationMatrix(platformHandle.Transform);
        platformLocomotion.ActivePlatform = platformHandle; // Ensure platform state is updated

        TestWorld.Context.Simulate();
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
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        platformLocomotion.IsActive.Should().BeFalse();
        platformLocomotion.MovementTransfer.Should().Be(MotionTransfer.None);

        TestWorld.Context.Simulate();
        scout.Simulate();

        var movedTransform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: new Vector3d(3, 0, 0));
        scout.FrameCondition.GroundState = new GroundCondition
        {
            Platform = new PlatformSnapshot(1, movedTransform, inert: true),
            MotionTransferState = MotionTransfer.PermaLocked
        };

        TestWorld.Context.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(Vector3d.Zero);
        scout.Rotation.Should().Be(FixedQuaternion.Identity);
        platformLocomotion.IsActive.Should().BeFalse();
        platformLocomotion.PlatformVelocity.Should().Be(Vector3d.Zero);
        platformLocomotion.MovementTransfer.Should().Be(MotionTransfer.None);
    }

    [Fact]
    public void Given_MovingPlatformBecomesInert_When_TraversalFinalizes_Then_ShouldDetachAndClearTransferState()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(),
            motionTransfer: MotionTransfer.PermaLocked);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        TestWorld.Context.Simulate();
        scout.Simulate();

        platformLocomotion.IsActive.Should().BeTrue();

        scout.FrameCondition.GroundState = new GroundCondition
        {
            Platform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform(), inert: true),
            MotionTransferState = MotionTransfer.PermaLocked
        };

        TestWorld.Context.Simulate();
        scout.Simulate();

        platformLocomotion.ActivePlatform.Should().BeNull();
        platformLocomotion.IsActive.Should().BeFalse();
        platformLocomotion.MovementTransfer.Should().Be(MotionTransfer.None);
        platformLocomotion.PlatformVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void Given_ScoutJumpsBeforePlatformMoves_When_Simulated_Then_ShouldNotInheritFutureVelocity()
    {
        // Arrange
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform();
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        // Act 1 - Jump before platform movement
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Move platform afterward
        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(new Vector3d(3, 0, 0));

        // Act 2 - Simulate next frame after platform movement
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Assert - we didn't pick up any horizontal velocity from the platform
        scout.Motor.Handler.Move.FrameVelocity.X.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutWithInitTransfer_When_Jumping_Then_ShouldInheritPlatformVelocity()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        platformLocomotion.PlatformVelocity = new Vector3d(1, 0, 0);

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        scout.Motor.Handler.Move.FrameVelocity.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void Given_ScoutWithPermaLocked_When_PlatformMoves_Then_ScoutFollowsPlatform()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);
        platformLocomotion.MovementTransfer = MotionTransfer.PermaLocked;

        TestWorld.Context.Simulate();
        scout.Simulate();

        var moveDelta = new Vector3d(2, 0, 0);
        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(moveDelta);
        platformLocomotion.ActivePlatform = platformHandle; // Ensure platform state is updated

        TestWorld.Context.Simulate();
        scout.Simulate();

        scout.Position.Should().Be(moveDelta);
    }

    [Fact]
    public void Given_ScoutHoldingOldPlatform_When_HoldTimesOut_Then_ShouldDetach()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);
        var platform = platformLocomotion.ActivePlatform;

        var newMatrix = MockMotorAgentTestFactory.CreatePlatformTransform();
        var platformHandle = new PlatformSnapshot(2, newMatrix);

        platformLocomotion.SetHoldPlatform(platformHandle);

        bool release = false;
        // Simulate exceeding the max hold frame count
        for (int i = 0; i < PlatformLocomotion.MaxHoldPlatformFrames + 1; i++)
        {
            release = platformLocomotion.TickHoldOnPlatform();
            if (release)
                break;
        }

        release.Should().BeTrue();
    }

    [Fact]
    public void Given_PlatformLocomotionDisabled_When_Cleared_Then_ShouldResetState()
    {
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent();
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        (platformLocomotion.ActivePlatform?.Active ?? false).Should().BeTrue();

        platformLocomotion.IsEnabled = false;

        (platformLocomotion.ActivePlatform?.Active ?? false).Should().BeFalse();
        platformLocomotion.PlatformVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void Given_ScoutOnPlatform_When_JumpsWithInitTransfer_Then_InertiaShouldNotDoubleApply()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(platformMatrix: platform, motionTransfer: MotionTransfer.InitTransfer);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        var initialPlatformVelocity = new Vector3d(1, 0, 0);
        platformLocomotion.PlatformVelocity = initialPlatformVelocity;

        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        Vector3d velocityAfterJump = scout.Motor.Handler.Move.FrameVelocity;

        velocityAfterJump.X.Should().Be(initialPlatformVelocity.X);
    }

    [Fact]
    public void Given_JumpingScout_When_PlatformIsMoving_Then_ShouldInheritVelocity()
    {
        // Arrange
        var transform = MockMotorAgentTestFactory.CreatePlatformTransform(startPosition: Vector3d.Zero);
        var scout = MockMotorAgentTestFactory.CreatePlatformAgent(startPosition: Vector3d.Zero, platformMatrix: transform, motionTransfer: MotionTransfer.InitTransfer);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);

        // Act 1 - Set initial state
        TestWorld.Context.Simulate();
        scout.Simulate();

        // Arrange - Move platform
        var platformHandle = platformLocomotion.ActivePlatform ?? default;
        platformHandle.Transform.SetTranslation(new Vector3d(2, 0, 0));
        platformLocomotion.ActivePlatform = platformHandle; // Ensure platform state is updated

        // Act 2 - Jump from moving platform
        TestWorld.Context.Simulate();
        scout.FrameRequest.IsRequestingJump = true;
        scout.Simulate();

        // Assert
        scout.Motor.Handler.Move.FrameVelocity.Should().NotBe(Vector3d.Zero);
        scout.Motor.Handler.Move.FrameVelocity.X.Should().Be(platformLocomotion.PlatformVelocity.X);
    }

    [Fact]
    public void Given_ScoutUsesPlatformInertia_When_Lands_Then_FrameVelocityShouldBeCleared()
    {
        var platform = MockMotorAgentTestFactory.CreatePlatformTransform();
        var scout = MockMotorAgentTestFactory.CreateFallingAgent(platformMatrix: platform);
        var platformLocomotion = TestRequire.NotNull(TestRequire.NotNull(scout.Motor).Handler.Platform);
        platformLocomotion.FramePlatformVelocity = new Vector3d(2, 0, 0);

        scout.Simulate();

        scout.FrameCondition.Medium = TraversalMedium.Solid;

        scout.Motor.FinalizeTraversal(scout.Position, scout.LastPosition, scout.Rotation, scout.FrameCondition, scout.GetFootPosition());
        platformLocomotion.FramePlatformVelocity.Should().Be(Vector3d.Zero);
    }

}
