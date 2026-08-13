using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Query;

public sealed class PathQueryContractsTests
{
    [Fact]
    public void NavigationEndpoint_ShouldPreserveExactSelectionIntent()
    {
        Vector3d position = new(Fixed64.One, (Fixed64)2, (Fixed64)3);
        var first = new NavigationEndpoint(position, "Caves", EndpointResolutionPolicy.NearestNavigable, (Fixed64)4);
        var same = new NavigationEndpoint(position, "Caves", EndpointResolutionPolicy.NearestNavigable, (Fixed64)4);
        var different = new NavigationEndpoint(position, "caves", EndpointResolutionPolicy.NearestNavigable, (Fixed64)4);

        first.Position.Should().Be(position);
        first.MapId.Should().Be("Caves");
        first.Resolution.Should().Be(EndpointResolutionPolicy.NearestNavigable);
        first.MaxResolutionDistance.Should().Be((Fixed64)4);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different, "map identity is ordinal and case-sensitive");
    }

    [Fact]
    public void NavigationEndpoint_ShouldRejectInvalidSelectorsPoliciesAndDistances()
    {
        Action emptyMap = () => _ = new NavigationEndpoint(default, "  ");
        Action unknownPolicy = () => _ = new NavigationEndpoint(default, resolution: (EndpointResolutionPolicy)99);
        Action negativeDistance = () => _ = new NavigationEndpoint(default, maxResolutionDistance: -Fixed64.One);

        emptyMap.Should().Throw<ArgumentException>();
        unknownPolicy.Should().Throw<ArgumentOutOfRangeException>();
        negativeDistance.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TraversalIntent_ShouldPreserveExplicitOrAutomaticSelection()
    {
        var explicitIntent = new TraversalIntent(
            TraversalDomain.Volume,
            TraversalMedium.Liquid,
            TraversalDomain.Surface);
        var same = new TraversalIntent(
            TraversalDomain.Volume,
            TraversalMedium.Liquid,
            TraversalDomain.Surface);
        var automatic = new TraversalIntent(
            TraversalDomain.Automatic,
            TraversalMedium.Unknown,
            TraversalDomain.Automatic);

        explicitIntent.StartDomain.Should().Be(TraversalDomain.Volume);
        explicitIntent.CurrentMedium.Should().Be(TraversalMedium.Liquid);
        explicitIntent.TargetDomain.Should().Be(TraversalDomain.Surface);
        explicitIntent.Should().Be(same);
        explicitIntent.GetHashCode().Should().Be(same.GetHashCode());
        explicitIntent.Should().NotBe(automatic);
    }

    [Fact]
    public void TraversalIntent_ShouldRejectUnknownAndConflictingValues()
    {
        Action unknownStart = () => _ = new TraversalIntent((TraversalDomain)99, TraversalMedium.Unknown, TraversalDomain.Surface);
        Action unknownTarget = () => _ = new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Solid, (TraversalDomain)99);
        Action unknownMedium = () => _ = new TraversalIntent(TraversalDomain.Automatic, (TraversalMedium)99, TraversalDomain.Surface);
        Action surfaceVolumeMedium = () => _ = new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Liquid, TraversalDomain.Surface);
        Action volumeSolidMedium = () => _ = new TraversalIntent(TraversalDomain.Volume, TraversalMedium.Solid, TraversalDomain.Volume);

        unknownStart.Should().Throw<ArgumentOutOfRangeException>();
        unknownTarget.Should().Throw<ArgumentOutOfRangeException>();
        unknownMedium.Should().Throw<ArgumentOutOfRangeException>();
        surfaceVolumeMedium.Should().Throw<ArgumentException>();
        volumeSolidMedium.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FlowFieldQueryOptions_ShouldRequireNonNegativeCostAndUseExactValueIdentity()
    {
        var first = new FlowFieldQueryOptions(Fixed64.Half);
        var same = new FlowFieldQueryOptions(Fixed64.Half);

        first.ExtraIntegrationCost.Should().Be(Fixed64.Half);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        ((Action)(() => _ = new FlowFieldQueryOptions(-Fixed64.One)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PathQuery_ShouldPreserveCompleteExactIntent()
    {
        PathQuery first = CreateQuery();
        PathQuery same = CreateQuery();
        PathQuery different = CreateQuery(allowTransitions: false);

        first.Algorithm.Should().Be(PathAlgorithm.FlowField);
        first.AllowTransitions.Should().BeTrue();
        first.FlowField.ExtraIntegrationCost.Should().Be(Fixed64.Half);
        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(different);
    }

    [Fact]
    public void PathQuery_ShouldRejectInvalidNestedValuesAndAlgorithmOptions()
    {
        PathQuery valid = CreateQuery();

        Action invalidAgent = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            default,
            valid.Traversal,
            PathAlgorithm.AStar,
            valid.Budget,
            allowTransitions: false);
        Action unknownAlgorithm = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            valid.Agent,
            valid.Traversal,
            (PathAlgorithm)99,
            valid.Budget,
            allowTransitions: false);
        Action flowOptionsOnAStar = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            valid.Agent,
            valid.Traversal,
            PathAlgorithm.AStar,
            valid.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.One));

        invalidAgent.Should().Throw<ArgumentException>();
        unknownAlgorithm.Should().Throw<ArgumentOutOfRangeException>();
        flowOptionsOnAStar.Should().Throw<ArgumentException>();
    }

    private static PathQuery CreateQuery(bool allowTransitions = true)
    {
        KinematicBodyShape shape = new(Fixed64.Half, (Fixed64)2, Fixed64.One);
        NavigationAgentProfile agent = new(
            shape,
            Fixed64.Half,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Jump | TraversalCapability.Swim);
        NavigationWorkBudget budget = new(10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

        return new PathQuery(
            new NavigationEndpoint(default, "Surface"),
            new NavigationEndpoint(new Vector3d((Fixed64)4, Fixed64.Zero, (Fixed64)5), "Caves", EndpointResolutionPolicy.NearestNavigable, (Fixed64)6),
            agent,
            new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Solid, TraversalDomain.Volume),
            PathAlgorithm.FlowField,
            budget,
            allowTransitions,
            new FlowFieldQueryOptions(Fixed64.Half));
    }
}
