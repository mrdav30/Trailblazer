using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Query;

public sealed class NavigationProfileContractsTests
{
    [Fact]
    public void KinematicBodyShape_ShouldPreserveExactDimensionsAndValueIdentity()
    {
        var first = new KinematicBodyShape(Fixed64.Zero, (Fixed64)2, Fixed64.Half);
        var same = new KinematicBodyShape(Fixed64.Zero, (Fixed64)2, Fixed64.Half);
        var different = new KinematicBodyShape(Fixed64.Half, (Fixed64)2, Fixed64.Half);

        first.Radius.Should().Be(Fixed64.Zero);
        first.Height.Should().Be((Fixed64)2);
        first.RootToFootOffsetY.Should().Be(Fixed64.Half);
        first.Should().Be(same);
        (first == same).Should().BeTrue();
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different);
        (first != different).Should().BeTrue();
        first.Equals(new KinematicBodyShape(Fixed64.Zero, (Fixed64)3, Fixed64.Half)).Should().BeFalse();
        first.Equals(new KinematicBodyShape(Fixed64.Zero, (Fixed64)2, Fixed64.One)).Should().BeFalse();
        first.Equals((object)"not a shape").Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 1, -1)]
    public void KinematicBodyShape_ShouldRejectInvalidDimensions(int radius, int height, int offset)
    {
        Action construct = () => _ = new KinematicBodyShape((Fixed64)radius, (Fixed64)height, (Fixed64)offset);

        construct.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NavigationAgentProfile_ShouldPreserveCompleteExactIdentity()
    {
        KinematicBodyShape shape = new(Fixed64.Half, (Fixed64)2, Fixed64.One);
        var first = new NavigationAgentProfile(
            shape,
            Fixed64.Half,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Jump | TraversalCapability.Swim);
        var same = new NavigationAgentProfile(
            shape,
            Fixed64.Half,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Jump | TraversalCapability.Swim);
        var different = new NavigationAgentProfile(
            shape,
            Fixed64.Half,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Swim);

        first.Shape.Should().Be(shape);
        first.MaxStepUp.Should().Be(Fixed64.Half);
        first.MaxDropDown.Should().Be(Fixed64.One);
        first.ArrivalRadius.Should().Be(Fixed64.Half);
        first.AllowedMedia.Should().Be(TraversalMedia.Solid | TraversalMedia.Liquid);
        first.Capabilities.Should().Be(TraversalCapability.Jump | TraversalCapability.Swim);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different);
        first.Equals((object)"not a profile").Should().BeFalse();

        NavigationAgentProfile[] fieldMutations =
        {
            CreateProfile(new KinematicBodyShape(Fixed64.One, (Fixed64)2, Fixed64.One), Fixed64.Half, Fixed64.One, Fixed64.Half, TraversalMedia.Solid | TraversalMedia.Liquid, TraversalCapability.Jump | TraversalCapability.Swim),
            CreateProfile(shape, Fixed64.One, Fixed64.One, Fixed64.Half, TraversalMedia.Solid | TraversalMedia.Liquid, TraversalCapability.Jump | TraversalCapability.Swim),
            CreateProfile(shape, Fixed64.Half, (Fixed64)2, Fixed64.Half, TraversalMedia.Solid | TraversalMedia.Liquid, TraversalCapability.Jump | TraversalCapability.Swim),
            CreateProfile(shape, Fixed64.Half, Fixed64.One, Fixed64.One, TraversalMedia.Solid | TraversalMedia.Liquid, TraversalCapability.Jump | TraversalCapability.Swim),
            CreateProfile(shape, Fixed64.Half, Fixed64.One, Fixed64.Half, TraversalMedia.Solid, TraversalCapability.Jump | TraversalCapability.Swim),
            CreateProfile(shape, Fixed64.Half, Fixed64.One, Fixed64.Half, TraversalMedia.Solid | TraversalMedia.Liquid, TraversalCapability.Swim)
        };

        foreach (NavigationAgentProfile mutation in fieldMutations)
            first.Equals(mutation).Should().BeFalse("every profile field is part of exact identity");
    }

    [Fact]
    public void NavigationAgentProfile_ShouldRejectInvalidDimensionsAndUnknownFlagBits()
    {
        KinematicBodyShape shape = new(Fixed64.Half, (Fixed64)2, Fixed64.One);

        Action invalidShape = () => _ = CreateProfile(default);
        Action negativeStep = () => _ = CreateProfile(shape, maxStepUp: -Fixed64.One);
        Action negativeDrop = () => _ = CreateProfile(shape, maxDropDown: -Fixed64.One);
        Action negativeArrival = () => _ = CreateProfile(shape, arrivalRadius: -Fixed64.One);
        Action unknownMedia = () => _ = CreateProfile(shape, allowedMedia: (TraversalMedia)(1 << 10));
        Action unknownCapability = () => _ = CreateProfile(shape, capabilities: (TraversalCapability)(1 << 5));

        invalidShape.Should().Throw<ArgumentException>();
        negativeStep.Should().Throw<ArgumentOutOfRangeException>();
        negativeDrop.Should().Throw<ArgumentOutOfRangeException>();
        negativeArrival.Should().Throw<ArgumentOutOfRangeException>();
        unknownMedia.Should().Throw<ArgumentOutOfRangeException>();
        unknownCapability.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static NavigationAgentProfile CreateProfile(
        KinematicBodyShape shape,
        Fixed64 maxStepUp = default,
        Fixed64 maxDropDown = default,
        Fixed64 arrivalRadius = default,
        TraversalMedia allowedMedia = TraversalMedia.Solid,
        TraversalCapability capabilities = TraversalCapability.None) =>
        new(shape, maxStepUp, maxDropDown, arrivalRadius, allowedMedia, capabilities);
}
