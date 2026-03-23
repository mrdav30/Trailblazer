using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Animation;
using Xunit;

namespace Trailblazer.Tests.Navigation.Animation;

public class NavAnimationUpdaterTests
{
    private static readonly Fixed64 MinAxis = Fixed64.FromRaw(0x40000000L);
    private static readonly Fixed64 MaxAxisVertical = Fixed64.FromRaw(0x80000000L);
    private static readonly Fixed64 DampTime = (Fixed64)0.2f;

    [Fact]
    public void UpdateAnimationParameters_ShouldSetSprintState()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            Vector3d.Zero,
            FixedQuaternion.Identity,
            isLockedOn: false,
            isSprinting: true,
            dampTime: DampTime);

        handler.LastIsSprinting.Should().BeTrue();
        handler.LastDampTime.Should().Be(DampTime);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldUseForwardOnlyBlend_WhenNotLockedOn()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            new Vector3d((Fixed64)0.3f, Fixed64.Zero, Fixed64.Zero),
            FixedQuaternion.Identity,
            isLockedOn: false,
            isSprinting: false,
            dampTime: DampTime);

        handler.LastForward.Should().Be(Fixed64.Half);
        handler.LastSideways.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldNotActivateForwardAxis_AtMinimumLockedOnThreshold()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            new Vector3d(Fixed64.Zero, Fixed64.Zero, MinAxis),
            FixedQuaternion.Identity,
            isLockedOn: true,
            isSprinting: false,
            dampTime: DampTime);

        handler.LastForward.Should().Be(Fixed64.Zero);
        handler.LastSideways.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldKeepHalfForward_AtMaximumLockedOnThreshold()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            new Vector3d(Fixed64.Zero, Fixed64.Zero, MaxAxisVertical),
            FixedQuaternion.Identity,
            isLockedOn: true,
            isSprinting: false,
            dampTime: DampTime);

        handler.LastForward.Should().Be(Fixed64.Half);
        handler.LastSideways.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldUseHalfSideways_WhenLockedOnAndWithinSideBand()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            new Vector3d((Fixed64)0.5f, Fixed64.Zero, Fixed64.Zero),
            FixedQuaternion.Identity,
            isLockedOn: true,
            isSprinting: false,
            dampTime: DampTime);

        handler.LastForward.Should().Be(Fixed64.Zero);
        handler.LastSideways.Should().Be(Fixed64.Half);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldUseSignedLocalAxes_WhenLockedOn()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            Vector3d.Left,
            FixedQuaternion.FromDirection(Vector3d.Right),
            isLockedOn: true,
            isSprinting: false,
            dampTime: DampTime);

        handler.LastForward.Should().Be(-Fixed64.One);
        handler.LastSideways.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void UpdateAnimationParameters_ShouldUseNonLockedOnBlend_WhenSprintingWhileLockedOn()
    {
        var handler = new TestAnimationHandler();

        NavAnimationUpdater.UpdateAnimationParameters(
            handler,
            Vector3d.Right,
            FixedQuaternion.Identity,
            isLockedOn: true,
            isSprinting: true,
            dampTime: DampTime);

        handler.LastForward.Should().Be(Fixed64.One);
        handler.LastSideways.Should().Be(Fixed64.Zero);
        handler.LastIsSprinting.Should().BeTrue();
    }

    private sealed class TestAnimationHandler : INavAnimationHandler
    {
        public Fixed64 LastForward { get; private set; }

        public Fixed64 LastSideways { get; private set; }

        public Fixed64 LastDampTime { get; private set; }

        public bool LastIsSprinting { get; private set; }

        public void SetDirectionalInput(Fixed64 forward, Fixed64 sideways, Fixed64 dampTime)
        {
            LastForward = forward;
            LastSideways = sideways;
            LastDampTime = dampTime;
        }

        public void SetIsSprinting(bool isSprinting)
        {
            LastIsSprinting = isSprinting;
        }

        public void ApplyRootMotion(Vector3d deltaPosition, Fixed64 forceMultiplier)
        {
        }
    }
}
