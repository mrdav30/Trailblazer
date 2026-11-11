using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData("mode")]
    [InlineData("radius")]
    [InlineData("axis")]
    [InlineData("height")]
    [InlineData("step")]
    [InlineData("drop")]
    [InlineData("arrival")]
    [InlineData("media")]
    [InlineData("unknown-media")]
    [InlineData("capabilities")]
    [InlineData("normal-low")]
    [InlineData("normal-high")]
    public void ProfileRejectsInvalidGeometryAndPermissions(string field)
    {
        Action create = () => new NavigationAgentProfile2D(
            field == "mode" ? (NavigationMode2D)0 : NavigationMode2D.SideView,
            field == "radius" ? Fixed64.Zero : field == "height" ? Fixed64.MaxValue : Fixed64.Quarter,
            field == "axis" ? -Fixed64.One : Fixed64.One,
            field == "step" ? -Fixed64.One : Fixed64.Zero,
            field == "drop" ? -Fixed64.One : Fixed64.Zero,
            field == "normal-low" ? Fixed64.Zero : field == "normal-high" ? Fixed64.Two : Fixed64.Half,
            field == "arrival" ? -Fixed64.One : Fixed64.Zero,
            field == "media" ? TraversalMedia.None : field == "unknown-media" ? (TraversalMedia)128 : TraversalMedia.Solid,
            field == "capabilities" ? (TraversalCapability)128 : TraversalCapability.None);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProfileEqualityIncludesEveryGeometryAndPermissionField()
    {
        var same = new NavigationAgentProfile2D(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp,
            Profile.MaxDropDown, Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities);
        same.Equals(Profile).Should().BeTrue();
        same.Equals((object)Profile).Should().BeTrue();
        same.Equals(new object()).Should().BeFalse();
        (same == Profile).Should().BeTrue();
        (same != Profile).Should().BeFalse();
        same.GetHashCode().Should().Be(Profile.GetHashCode());
        NavigationAgentProfile2D[] different =
        [
            new(NavigationMode2D.TopDown, Profile.Radius, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
                Fixed64.Zero, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Fixed64.Half, Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Fixed64.Zero, Profile.MaxStepUp, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Fixed64.Zero, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp, Fixed64.Zero,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown,
                Fixed64.One, Profile.ArrivalRadius, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Fixed64.One, Profile.AllowedMedia, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, TraversalMedia.Gas, Profile.Capabilities),
            new(Profile.Mode, Profile.Radius, Profile.AxisLength, Profile.MaxStepUp, Profile.MaxDropDown,
                Profile.MinimumSupportNormalDot, Profile.ArrivalRadius, Profile.AllowedMedia, TraversalCapability.None)
        ];
        foreach (var profile in different)
        {
            (Profile != profile).Should().BeTrue();
            Profile.Equals((object)profile).Should().BeFalse();
            profile.GetHashCode().Should().NotBe(Profile.GetHashCode());
        }
    }

    [Fact]
    public void DefaultAndMismatchedValuesRejectAtTheHostBoundary()
    {
        using var context = Scene();
        Action create = () => new Host(context, default(NavigationAgentProfile2D));
        create.Should().Throw<ArgumentException>();
        create = () => new Host(context, settings: default(NavigationMotorSettings2D));
        create.Should().Throw<ArgumentException>();
        create = () => new Host(context, pose: new NavigationPose2D(Vector2d.Zero, Fixed64.One));
        create.Should().Throw<ArgumentException>();
        create = () => new Host(context, medium: TraversalMedium.Gas);
        create.Should().Throw<ArgumentException>();
        create = () => new Host(context, medium: (TraversalMedium)99);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationSupport2D(default, default, Fixed64.One);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationSupport2D(default, new Vector2d(Fixed64.MaxValue, Fixed64.One), Fixed64.One);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationSupport2D(default, Vector2d.Forward, -Fixed64.One);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationCarrier2D(0, 1, default, default);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationCarrier2D(1, 0, default, default);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationSupport2D(default, Vector2d.Forward, Fixed64.One, default(NavigationCarrier2D));
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationMotionResult2D(default, NavigationSupportStatus2D.Found, default(NavigationSupport2D));
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationMotionResult2D(default, NavigationSupportStatus2D.Found);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationMotionResult2D(default, NavigationSupportStatus2D.NoSupport, Ground);
        create.Should().Throw<ArgumentException>();
        create = () => new NavigationMotionResult2D(default, (NavigationSupportStatus2D)99);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SupportRefreshChecksFrameLifetimeAndDoesNotPublishMotion()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(Ground);
        host.OnRead = context.Reset;
        host.Invoking(h => h.RefreshSupport()).Should().Throw<InvalidOperationException>();
        host.SupportStatus.Should().Be(NavigationSupportStatus2D.Unavailable);
        host.Pose.Center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void CarrierCallbackCannotSubmitAfterItChangesTheFrame()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, default, Vector2d.Right)));
        host.RefreshSupport();
        host.OnCarrier = context.Reset;
        context.Simulate();
        host.Invoking(h => h.PrepareFrameMotion(Vector2d.Zero)).Should().Throw<InvalidOperationException>();
        host.Request.Frame.Should().Be(0);
    }

    [Fact]
    public void TopDownTurningCommitsAcceptedRotationAndRequiresInapplicableSupport()
    {
        using var context = Scene(NavigationMode2D.TopDown);
        var profile = new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Quarter, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        using var host = new Host(context, profile, new NavigationMotorSettings2D((Fixed64)4, (Fixed64)4,
            Fixed64.Zero, Fixed64.Zero));
        context.Simulate();
        host.PrepareFrameMotion(new NavigationInput2D(Vector2d.Right, false, Vector2d.Forward));
        host.Request.Target.RotationRadians.Should().Be(Fixed64.Quarter);
        host.Pose.RotationRadians.Should().Be(Fixed64.Zero);
        host.Result = new NavigationMotionResult2D(new NavigationPose2D(host.Request.Target.Center, Fixed64.FromFraction(1, 8)),
            NavigationSupportStatus2D.NotApplicable);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Facing.Should().Be(Vector2d.Rotate(Vector2d.Right, Fixed64.FromFraction(1, 8)));
        host.SupportStatus.Should().Be(NavigationSupportStatus2D.NotApplicable);
        context.Simulate();
        host.PrepareFrameMotion(Vector2d.Zero);
        host.Accept(host.Pose.Center, Ground);
        host.Invoking(h => h.CommitFrameMotion()).Should().Throw<ArgumentException>();
        Action create = () => new Host(context, profile, new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero,
            Fixed64.One, Fixed64.Zero));
        create.Should().Throw<ArgumentException>();
        create = () => new Host(context, profile, new NavigationMotorSettings2D(Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.One));
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MotorSettingsRejectNegativeValues(int field)
    {
        Action create = () => new NavigationMotorSettings2D(field == 0 ? -Fixed64.One : Fixed64.Zero,
            field == 1 ? -Fixed64.One : Fixed64.Zero, field == 2 ? -Fixed64.One : Fixed64.Zero,
            field == 3 ? -Fixed64.One : Fixed64.Zero);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SideViewCannotAcquireATopDownGuide()
    {
        using var context = Scene();
        using var host = new Host(context);
        host.ApplyGuidedTrekRequest(new NavigationCellAddress("side", default), default, PathAlgorithm.AStar, default)
            .Should().Be(NavigationGuideStatus.InvalidProfile);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TopDownProfileCannotContainSideViewGeometry(int field)
    {
        Action create = () => new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.One,
            field == 0 ? Fixed64.One : Fixed64.Zero, field == 1 ? Fixed64.One : Fixed64.Zero,
            field == 2 ? Fixed64.One : Fixed64.Zero, field == 3 ? Fixed64.One : Fixed64.Zero,
            Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ModeSpecificMotorSettingsAndZeroFacingReject()
    {
        using var context = Scene();
        Action create = () => new Host(context, settings: new NavigationMotorSettings2D(Fixed64.One, Fixed64.One,
            Fixed64.One, Fixed64.Zero));
        create.Should().Throw<ArgumentException>();
        using var host = new Host(context);
        context.Simulate();
        host.Invoking(h => h.PrepareFrameMotion(new NavigationInput2D(null, false, Vector2d.Zero)))
            .Should().Throw<ArgumentException>();
        host.Invoking(h => h.PrepareFrameMotion(new Vector2d(2, 0))).Should().Throw<ArgumentException>();
    }
}
