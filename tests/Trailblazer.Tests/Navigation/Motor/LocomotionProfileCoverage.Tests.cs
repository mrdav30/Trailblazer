using FluentAssertions;
using System;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

[Collection("TrailblazerCollection")]
public sealed class LocomotionProfileCoverageTests : IDisposable
{
    public void Dispose()
    {
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMoveIsNull()
    {
        Action act = () => new LocomotionProfile(
            null!,
            new FallLocomotion());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("move");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFallIsNull()
    {
        Action act = () => new LocomotionProfile(
            new MoveLocomotion(),
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fall");
    }

    [Fact]
    public void InstalledKinds_ShouldReflectInstalledModules()
    {
        var coreOnly = LocomotionProfile.CreateCoreOnly();
        var full = LocomotionProfile.CreateDefault();
        var mixed = new LocomotionProfile(
            new MoveLocomotion(),
            new FallLocomotion(),
            jump: new JumpLocomotion(),
            water: new WaterLocomotion());

        coreOnly.InstalledKinds.Should().Be(LocomotionKind.Core);
        full.InstalledKinds.Should().Be(LocomotionKind.All);
        mixed.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Jump | LocomotionKind.Water);
        Assert.NotNull(mixed.Platform);
        mixed.Water.Should().NotBeNull();
    }

    [Fact]
    public void CreateBuilder_ShouldSeedDefaultOptionalModules()
    {
        var builder = LocomotionProfile.CreateBuilder();
        var profile = builder.Build();

        profile.InstalledKinds.Should().Be(LocomotionKind.All);
        Assert.NotNull(profile.Platform);
        Assert.NotNull(profile.Jump);
        Assert.NotNull(profile.Slide);
        Assert.NotNull(profile.Water);
        Assert.NotNull(profile.Fly);
        Assert.NotNull(profile.Climb);
    }

    [Fact]
    public void Builder_ShouldRespectWithAndWithoutCompositionChanges()
    {
        var move = new MoveLocomotion();
        var fall = new FallLocomotion();
        var platform = new PlatformLocomotion();
        var jump = new JumpLocomotion();
        var fly = new FlyLocomotion();
        var climb = new ClimbLocomotion();

        var profile = new LocomotionProfileBuilder(includeOptionalLocomotions: false)
            .WithMove(move)
            .WithFall(fall)
            .WithPlatform(platform)
            .WithJump(jump)
            .WithoutJump()
            .WithJump(jump)
            .WithSlide()
            .WithoutSlide()
            .WithWater()
            .WithoutWater()
            .WithFly(fly)
            .WithoutFly()
            .WithFly(fly)
            .WithClimb(climb)
            .WithoutClimb()
            .WithClimb(climb)
            .Build();

        profile.Move.Should().BeSameAs(move);
        profile.Fall.Should().BeSameAs(fall);
        Assert.NotNull(profile.Platform);
        profile.Jump.Should().BeSameAs(jump);
        profile.Slide.Should().BeNull();
        profile.Water.Should().BeNull();
        profile.Fly.Should().BeSameAs(fly);
        profile.Climb.Should().BeSameAs(climb);
        profile.InstalledKinds.Should().Be(LocomotionKind.Core | LocomotionKind.Jump | LocomotionKind.Fly | LocomotionKind.Climb);
    }

    [Fact]
    public void Builder_ShouldThrow_WhenCoreLocomotionsAreNull()
    {
        var builder = new LocomotionProfileBuilder(includeOptionalLocomotions: false);

        Action withMove = () => builder.WithMove(null!);
        Action withFall = () => builder.WithFall(null!);

        withMove.Should().Throw<ArgumentNullException>()
            .WithParameterName("move");
        withFall.Should().Throw<ArgumentNullException>()
            .WithParameterName("fall");
    }

    [Fact]
    public void CreateBuilderFromHandler_ShouldPreserveInstalledInstances()
    {
        var handler = new LocomotionHandler();
        handler.Remove<SlideLocomotion>().Should().BeTrue();
        handler.Remove<WaterLocomotion>().Should().BeTrue();

        var builder = LocomotionProfile.CreateBuilder(handler);
        var profile = builder.Build();

        profile.Move.Should().BeSameAs(handler.Move);
        profile.Fall.Should().BeSameAs(handler.Fall);
        profile.Platform.Should().BeSameAs(handler.Platform);
        profile.Jump.Should().BeSameAs(handler.Jump);
        profile.Slide.Should().BeNull();
        profile.Water.Should().BeNull();
        profile.Fly.Should().BeSameAs(handler.Fly);
        profile.Climb.Should().BeSameAs(handler.Climb);
    }
}
