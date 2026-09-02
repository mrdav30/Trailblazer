using System;
using System.Linq;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class LocomotionHandlerCompositionTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Replace_ShouldSupportEveryBuiltInLocomotionType()
    {
        var handler = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());
        var move = new MoveLocomotion();
        var fall = new FallLocomotion();
        var platform = new PlatformLocomotion();
        var jump = new JumpLocomotion();
        var slide = new SlideLocomotion();
        var water = new WaterLocomotion();
        var fly = new FlyLocomotion();
        var climb = new ClimbLocomotion();

        handler.Replace(move);
        handler.Replace(fall);
        handler.Replace(platform);
        handler.Replace(jump);
        handler.Replace(slide);
        handler.Replace(water);
        handler.Replace(fly);
        handler.Replace(climb);

        handler.Move.Should().BeSameAs(move);
        handler.Fall.Should().BeSameAs(fall);
        handler.Platform.Should().BeSameAs(platform);
        handler.Jump.Should().BeSameAs(jump);
        handler.Slide.Should().BeSameAs(slide);
        handler.Water.Should().BeSameAs(water);
        handler.Fly.Should().BeSameAs(fly);
        handler.Climb.Should().BeSameAs(climb);
        handler.InstalledKinds.Should().Be(LocomotionKind.All);
    }

    [Fact]
    public void ConstructorAndApplyProfile_ShouldThrow_WhenProfileIsNull()
    {
        var handler = new LocomotionHandler();

        Action construct = () => _ = new LocomotionHandler(null!);
        Action apply = () => handler.ApplyProfile(null!);

        construct.Should().Throw<ArgumentNullException>()
            .WithParameterName("profile");
        apply.Should().Throw<ArgumentNullException>()
            .WithParameterName("profile");
    }

    [Fact]
    public void CompositionQueries_ShouldReflectInstalledAndUnsupportedTypes()
    {
        var handler = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());

        handler.Has(LocomotionKind.Core).Should().BeTrue();
        handler.Has(LocomotionKind.Jump).Should().BeFalse();
        handler.Has<MoveLocomotion>().Should().BeTrue();
        handler.Has<JumpLocomotion>().Should().BeFalse();
        handler.Has<CustomLocomotion>().Should().BeFalse();
        handler.TryGet(out MoveLocomotion? move).Should().BeTrue();
        handler.TryGet(out JumpLocomotion? jump).Should().BeFalse();
        handler.TryGet(out CustomLocomotion? custom).Should().BeFalse();
        handler.Require<MoveLocomotion>().Should().BeSameAs(move);

        Action requireMissing = () => handler.Require<JumpLocomotion>();
        Action requireUnsupported = () => handler.Require<CustomLocomotion>();
        Action replaceNull = () => handler.Replace<MoveLocomotion>(null!);
        Action replaceUnsupported = () => handler.Replace(new CustomLocomotion());

        requireMissing.Should().Throw<InvalidOperationException>();
        requireUnsupported.Should().Throw<InvalidOperationException>();
        replaceNull.Should().Throw<ArgumentNullException>();
        replaceUnsupported.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Remove_ShouldRejectCoreTypes_ClearOptionalState_AndUpdateKinds()
    {
        var handler = TestWorld.Bind(new LocomotionHandler());
        handler.Jump!.RegisterJump();

        handler.Remove<MoveLocomotion>().Should().BeFalse();
        handler.Remove<PlatformLocomotion>().Should().BeFalse();
        handler.Remove<FallLocomotion>().Should().BeFalse();
        handler.Remove<JumpLocomotion>().Should().BeTrue();
        handler.Remove<JumpLocomotion>().Should().BeFalse();

        handler.Jump.Should().BeNull();
        handler.InstalledKinds.Should().Be(LocomotionKind.All & ~LocomotionKind.Jump);
    }

    [Fact]
    public void InstallAndToProfile_ShouldReflectCurrentComposition()
    {
        var handler = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());
        var jump = new JumpLocomotion();
        var water = new WaterLocomotion();
        var climb = new ClimbLocomotion();

        handler.Install(jump);
        handler.Install(water);
        handler.Install(climb);

        var profile = handler.ToProfile();

        handler.Jump.Should().BeSameAs(jump);
        handler.Water.Should().BeSameAs(water);
        profile.Move.Should().BeSameAs(handler.Move);
        profile.Fall.Should().BeSameAs(handler.Fall);
        profile.Jump.Should().BeSameAs(jump);
        profile.Water.Should().BeSameAs(water);
        profile.Climb.Should().BeSameAs(climb);
        profile.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Jump | LocomotionKind.Water | LocomotionKind.Climb);
    }

    [Fact]
    public void ApplyProfile_ShouldClearReplacedTransientState_AndPreserveSharedInstances()
    {
        var handler = TestWorld.Bind(new LocomotionHandler());
        var originalMove = handler.Move;
        var originalFall = handler.Fall;
        var originalPlatform = handler.Platform!;
        var originalJump = handler.Jump!;
        var originalSlide = handler.Slide!;
        var originalWater = handler.Water!;
        var originalFly = handler.Fly!;
        var originalClimb = handler.Climb!;

        originalMove.FrameVelocity = Vector3d.Forward;
        originalFall.IsFalling = true;
        originalPlatform.ActivePlatform = new PlatformSnapshot(11, Fixed4x4.Identity);
        originalJump.RegisterJump();
        originalSlide.IsSliding = true;
        originalWater.IsSwimming = true;
        originalWater.IsDiving = true;
        originalWater.UnderwaterTimer = Fixed64.One;
        originalFly.IsFlying = true;
        originalClimb.IsClimbing = true;
        originalClimb.IsMantling = true;

        var sharedMove = new MoveLocomotion { FrameVelocity = Vector3d.Right };
        var sharedFall = new FallLocomotion { IsFalling = true };
        var sharedPlatform = new PlatformLocomotion();
        var sharedJump = new JumpLocomotion();

        handler.ApplyProfile(new LocomotionProfile(
            sharedMove,
            sharedFall,
            platform: sharedPlatform,
            jump: sharedJump));

        handler.Move.Should().BeSameAs(sharedMove);
        handler.Fall.Should().BeSameAs(sharedFall);
        handler.Platform.Should().BeSameAs(sharedPlatform);
        handler.Jump.Should().BeSameAs(sharedJump);
        handler.Slide.Should().BeNull();
        handler.Water.Should().BeNull();
        handler.Fly.Should().BeNull();
        handler.Climb.Should().BeNull();
        handler.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Jump);

        originalMove.FrameVelocity.Should().Be(Vector3d.Zero);
        originalFall.IsFalling.Should().BeFalse();
        originalPlatform.ActivePlatform.Should().BeNull();
        originalJump.IsJumping.Should().BeFalse();
        originalSlide.IsSliding.Should().BeFalse();
        originalWater.IsSwimming.Should().BeFalse();
        originalWater.IsDiving.Should().BeFalse();
        originalWater.UnderwaterTimer.Should().Be(Fixed64.Zero);
        originalFly.IsFlying.Should().BeFalse();
        originalClimb.IsClimbing.Should().BeFalse();
        originalClimb.IsMantling.Should().BeFalse();
        sharedMove.FrameVelocity.Should().Be(Vector3d.Right);
        sharedFall.IsFalling.Should().BeTrue();
    }

    [Fact]
    public void ConfigureInstalledKinds_ShouldAlwaysRetainCoreAndRebuildInstalledProfile()
    {
        var handler = new LocomotionHandler();

        handler.ConfigureInstalledKinds(LocomotionKind.None);
        handler.InstalledKinds.Should().Be(LocomotionKind.Core);
        Assert.NotNull(handler.Platform);
        handler.Jump.Should().BeNull();
        handler.Slide.Should().BeNull();
        handler.Water.Should().BeNull();
        handler.Fly.Should().BeNull();
        handler.Climb.Should().BeNull();

        handler.ConfigureInstalledKinds(LocomotionKind.Platform | LocomotionKind.Fly);
        handler.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Fly);
        Assert.NotNull(handler.Platform);
        Assert.NotNull(handler.Fly);

        handler.ConfigureInstalledKinds(LocomotionKind.Climb);
        handler.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Climb);
        Assert.NotNull(handler.Climb);
    }

    [Fact]
    public void GetLocomotions_ShouldEnumerateInstalledLocomotionsInStableOrder()
    {
        var handler = new LocomotionHandler();

        handler.GetLocomotions()
            .Select(locomotion => locomotion.GetType())
            .Should()
            .ContainInOrder(
                typeof(MoveLocomotion),
                typeof(PlatformLocomotion),
                typeof(JumpLocomotion),
                typeof(FallLocomotion),
                typeof(WaterLocomotion),
                typeof(FlyLocomotion),
                typeof(ClimbLocomotion),
                typeof(SlideLocomotion));
    }

    [Fact]
    public void GetLocomotions_ShouldEnumerateOnlyCoreModulesForCoreOnlyProfile()
    {
        var handler = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());

        handler.GetLocomotions()
            .Select(locomotion => locomotion.GetType())
            .Should()
            .Equal(
                typeof(MoveLocomotion),
                typeof(PlatformLocomotion),
                typeof(FallLocomotion));
    }

    [Fact]
    public void SyncTransientState_ShouldCopyEnabledInstalledLocomotionsOnly()
    {
        var target = TestWorld.Bind(new LocomotionHandler());
        var source = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());

        target.IsInControl = false;
        target.Move.FrameVelocity = Vector3d.Zero;
        target.Fall.IsFalling = false;
        target.Jump!.RegisterJump();
        target.Jump.IsEnabled = false;
        target.Fly!.IsFlying = true;

        source.IsInControl = true;
        source.Move.FrameVelocity = Vector3d.Forward;
        source.Fall.IsFalling = true;

        target.SyncTransientState(source);

        target.IsInControl.Should().BeTrue();
        target.Move.FrameVelocity.Should().Be(Vector3d.Forward);
        target.Fall.IsFalling.Should().BeTrue();
        Assert.NotNull(target.Jump);
        target.Jump.IsJumping.Should().BeFalse();
        Assert.NotNull(target.Fly);
        target.Fly.IsFlying.Should().BeTrue();
    }

    [Fact]
    public void SyncTransientState_ShouldIgnoreNullSource()
    {
        var handler = new LocomotionHandler();
        handler.Move.FrameVelocity = Vector3d.Left;

        handler.SyncTransientState(null!);

        handler.Move.FrameVelocity.Should().Be(Vector3d.Left);
    }

    [Fact]
    public void ClearTransientStateMethods_ShouldResetEnabledModules()
    {
        var handler = new LocomotionHandler();
        handler.Move.FrameVelocity = Vector3d.Forward;
        handler.Fall.IsFalling = true;
        handler.Water!.IsSwimming = true;
        handler.Water.UnderwaterTimer = Fixed64.One;

        handler.ClearTransientState<MoveLocomotion>();
        handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);

        handler.ClearAllTransientState();

        handler.Fall.IsFalling.Should().BeFalse();
        handler.Water.IsSwimming.Should().BeFalse();
        handler.Water.UnderwaterTimer.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void ClearTransientState_ShouldIgnoreAbsentAndDisabledModules()
    {
        var coreOnly = new LocomotionHandler(LocomotionProfile.CreateCoreOnly());
        coreOnly.Invoking(handler => handler.ClearTransientState<WaterLocomotion>())
            .Should().NotThrow();

        coreOnly.Move.IsEnabled = false;
        coreOnly.Move.FrameVelocity = Vector3d.Right;

        coreOnly.ClearTransientState<MoveLocomotion>();

        coreOnly.Move.FrameVelocity.Should().Be(Vector3d.Right);
    }

    private sealed class CustomLocomotion : ILocomotion
    {
        public bool IsEnabled { get; set; } = true;

        public void RecordData(IChronicler chronicler)
        {
        }
    }
}
