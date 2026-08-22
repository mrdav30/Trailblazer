using System;
using FixedMathSharp;
using FluentAssertions;
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
    public void TraversalIntent_ShouldPreserveExactMediumSelection()
    {
        var explicitIntent = new TraversalIntent(
            TraversalMedium.Liquid,
            TraversalMedia.Liquid | TraversalMedia.Gas);
        var same = new TraversalIntent(
            TraversalMedium.Liquid,
            TraversalMedia.Liquid | TraversalMedia.Gas);
        var different = new TraversalIntent(
            TraversalMedium.Gas,
            TraversalMedia.Liquid | TraversalMedia.Gas);

        explicitIntent.StartMedium.Should().Be(TraversalMedium.Liquid);
        explicitIntent.TargetMedia.Should().Be(TraversalMedia.Liquid | TraversalMedia.Gas);
        explicitIntent.Should().Be(same);
        explicitIntent.GetHashCode().Should().Be(same.GetHashCode());
        explicitIntent.Should().NotBe(different);
    }

    [Fact]
    public void TraversalIntent_ShouldRejectUnknownStartAndInvalidTargetMedia()
    {
        Action unknownStart = () => _ = new TraversalIntent(
            TraversalMedium.Unknown,
            TraversalMedia.Solid);
        Action outOfRangeStart = () => _ = new TraversalIntent(
            (TraversalMedium)99,
            TraversalMedia.Solid);
        Action emptyTarget = () => _ = new TraversalIntent(
            TraversalMedium.Solid,
            TraversalMedia.None);
        Action unknownTarget = () => _ = new TraversalIntent(
            TraversalMedium.Solid,
            (TraversalMedia)(1 << 20));

        unknownStart.Should().Throw<ArgumentOutOfRangeException>();
        outOfRangeStart.Should().Throw<ArgumentOutOfRangeException>();
        emptyTarget.Should().Throw<ArgumentException>();
        unknownTarget.Should().Throw<ArgumentException>();
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
            valid.AreaPolicy,
            valid.Traversal,
            PathAlgorithm.AStar,
            valid.Budget,
            allowTransitions: false);
        Action unknownAlgorithm = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            valid.Agent,
            valid.AreaPolicy,
            valid.Traversal,
            (PathAlgorithm)99,
            valid.Budget,
            allowTransitions: false);
        Action flowOptionsOnAStar = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            valid.Agent,
            valid.AreaPolicy,
            valid.Traversal,
            PathAlgorithm.AStar,
            valid.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.One));
        Action targetOutsideAgent = () => _ = new PathQuery(
            valid.Start,
            valid.End,
            valid.Agent,
            valid.AreaPolicy,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            valid.Budget,
            allowTransitions: true);

        invalidAgent.Should().Throw<ArgumentException>();
        unknownAlgorithm.Should().Throw<ArgumentOutOfRangeException>();
        flowOptionsOnAStar.Should().Throw<ArgumentException>();
        targetOutsideAgent.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void PathQueryRecord_ShouldRoundTripExactIntent(bool useMemoryPack)
    {
        PathQuery query = CreateQuery();
        var source = new PathQueryRecord(query);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = new PathQueryRecord();

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.Query.Should().Be(query);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void PathQueryRecord_ShouldRejectOldOrMissingSchemaWithoutChangingQuery(
        bool useMemoryPack,
        bool removeSchema)
    {
        var source = new PathQueryRecord(CreateQuery());
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = removeSchema
            ? SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "SchemaVersion")
            : SerializationUtility.SetPayloadValue(payload, useMemoryPack, 0, "SchemaVersion");
        PathQuery shellQuery = CreateQuery(allowTransitions: false);
        var target = new PathQueryRecord(shellQuery);

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        target.Query.Should().Be(shellQuery);
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
            new NavigationAreaPolicyKey("default", 1),
            new TraversalIntent(
                TraversalMedium.Solid,
                TraversalMedia.Solid | TraversalMedia.Liquid),
            PathAlgorithm.FlowField,
            budget,
            allowTransitions,
            new FlowFieldQueryOptions(Fixed64.Half));
    }
}
