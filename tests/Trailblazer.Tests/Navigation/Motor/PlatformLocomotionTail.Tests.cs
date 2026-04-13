using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class PlatformLocomotionTailTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PropertiesAndHoldTick_ShouldCoverDisabledAndSamePlatformBranches()
    {
        var locomotion = new PlatformLocomotion();
        PlatformSnapshot activePlatform = new(1, MockMotorAgentTestFactory.CreatePlatformTransform());

        locomotion.IsActive.Should().BeFalse();
        locomotion.IsHoldingPlatform.Should().BeFalse();
        locomotion.InteriaApplied.Should().BeFalse();

        locomotion.ActivePlatform = activePlatform;
        locomotion.MovementTransfer = MotionTransfer.InitTransfer;
        locomotion.IsActive.Should().BeTrue();
        locomotion.InteriaApplied.Should().BeTrue();

        locomotion.MovementTransfer = MotionTransfer.PermaTransfer;
        locomotion.InteriaApplied.Should().BeTrue();

        locomotion.SetHoldPlatform(activePlatform);
        locomotion.IsHoldingPlatform.Should().BeTrue();
        locomotion.TickHoldOnPlatform().Should().BeFalse();
        locomotion.TickHoldOnPlatform().Should().BeFalse();
        locomotion.HoldPlatformFrames.Should().Be(0);

        locomotion.IsEnabled = false;
        locomotion.IsActive.Should().BeFalse();
        locomotion.IsHoldingPlatform.Should().BeFalse();
        locomotion.InteriaApplied.Should().BeFalse();
    }

    [Fact]
    public void UpdatePlatformVelocity_ShouldRespectDisabledAndInactiveSnapshots()
    {
        var locomotion = new PlatformLocomotion
        {
            PlatformVelocity = Vector3d.Right
        };

        locomotion.IsEnabled = false;
        locomotion.UpdatePlatformVelocity();
        locomotion.PlatformVelocity.Should().Be(Vector3d.Zero);
        locomotion.ActivePlatform.Should().BeNull();

        locomotion.IsEnabled = true;
        locomotion.ActivePlatform = new PlatformSnapshot(2, MockMotorAgentTestFactory.CreatePlatformTransform(), inert: true);
        locomotion.UpdatePlatformVelocity();
        locomotion.PlatformVelocity.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void UpdatePlatformVelocity_AndInfluence_ShouldUseTransformDeltaAndRotationBranches()
    {
        var locomotion = new PlatformLocomotion
        {
            HeightAdjust = Fixed64.Zero,
            ActivePlatform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(2, 0, 0))),
            PreviousPlatform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform(Vector3d.Zero)),
            ScoutLocalPoint = Vector3d.Zero
        };

        locomotion.UpdatePlatformVelocity();

        locomotion.PlatformVelocity.x.Should().Be((Fixed64)2 * TrailblazerManager.InvDeltaTime);

        locomotion.GetPlatformInfluence(
            Vector3d.Zero,
            FixedQuaternion.Identity,
            out Vector3d positionDelta,
            out FixedQuaternion rotationDelta);

        positionDelta.Should().Be(new Vector3d(2, 0, 0));
        rotationDelta.Should().Be(FixedQuaternion.Identity);

        FixedQuaternion turn = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        locomotion.ActivePlatform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform(Vector3d.Zero, turn));

        locomotion.GetPlatformInfluence(
            Vector3d.Zero,
            FixedQuaternion.Identity,
            out _,
            out FixedQuaternion rotatedDelta);

        Fixed64 tolerance = Fixed64.FromRaw(0x00020000);
        (rotatedDelta.x - turn.x).Abs().Should().BeLessThan(tolerance);
        (rotatedDelta.y - turn.y).Abs().Should().BeLessThan(tolerance);
        (rotatedDelta.z - turn.z).Abs().Should().BeLessThan(tolerance);
        (rotatedDelta.w - turn.w).Abs().Should().BeLessThan(tolerance);
    }

    [Fact]
    public void HandlePlatformChange_ShouldRefreshSamePlatformTransform_AndPreservePreviousAttachment()
    {
        Fixed4x4 originalTransform = MockMotorAgentTestFactory.CreatePlatformTransform(Vector3d.Zero);
        Fixed4x4 refreshedTransform = MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 0));
        var locomotion = new PlatformLocomotion
        {
            HeightAdjust = Fixed64.Zero,
            ActivePlatform = new PlatformSnapshot(1, originalTransform),
            PreviousPlatform = new PlatformSnapshot(1, originalTransform)
        };

        locomotion.HandlePlatformChange(new GroundCondition
        {
            Platform = new PlatformSnapshot(1, refreshedTransform),
            MotionTransferState = MotionTransfer.PermaLocked
        });

        locomotion.MovementTransfer.Should().Be(MotionTransfer.PermaLocked);
        locomotion.ActivePlatform.Should().NotBeNull();
        locomotion.ActivePlatform!.Value.Transform.Translation.Should().Be(refreshedTransform.Translation);
        locomotion.PreviousPlatform.Should().NotBeNull();
        locomotion.PreviousPlatform!.Value.Transform.Translation.Should().Be(originalTransform.Translation);
        locomotion.IsNewPlatform.Should().BeFalse();

        locomotion.HandlePlatformMovement(Vector3d.Zero, FixedQuaternion.Identity);
        locomotion.ScoutLocalPoint.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void HandlePlatformChange_ShouldClearHoldAndTransfer_WhenSurfaceStopsSupportingKinematicMotion()
    {
        var locomotion = new PlatformLocomotion
        {
            ActivePlatform = new PlatformSnapshot(1, MockMotorAgentTestFactory.CreatePlatformTransform()),
            MovementTransfer = MotionTransfer.PermaTransfer
        };
        locomotion.SetHoldPlatform(new PlatformSnapshot(2, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 0))));

        locomotion.HandlePlatformChange(new GroundCondition
        {
            Platform = new PlatformSnapshot(3, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(5, 0, 0)), inert: true),
            MotionTransferState = MotionTransfer.PermaTransfer
        });

        locomotion.ActivePlatform.Should().BeNull();
        locomotion.HoldPlatform.Should().BeNull();
        locomotion.MovementTransfer.Should().Be(MotionTransfer.None);
        locomotion.HoldPlatformFrames.Should().Be(0);
    }
}
