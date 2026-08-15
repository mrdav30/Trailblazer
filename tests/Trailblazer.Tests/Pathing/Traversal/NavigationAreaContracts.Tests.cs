using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Traversal;

public sealed class NavigationAreaContractsTests
{
    [Fact]
    public void ContextCatalog_ShouldRequireExactConfiguredAreaRuleCount()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            navigationAreaCount: 2,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        var undersized = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("undersized", 1),
            new[] { default(NavigationAreaRule) });
        var operation = new NavigationAreaPolicyCommitOperation(undersized, 1, 1);

        context.Pathing.Admit(operation).Should().BeFalse();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
    }

    [Fact]
    public void NavigationAreaPolicy_ShouldCopyRulesAndResolveByStableAreaId()
    {
        var source = new[]
        {
            default(NavigationAreaRule),
            new NavigationAreaRule(isAllowed: true, additionalEnterCost: (Fixed64)3),
            new NavigationAreaRule(isAllowed: false, additionalEnterCost: Fixed64.Zero)
        };
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("safe-route", revision: 4),
            source);

        source[1] = default;

        policy.Key.Should().Be(new NavigationAreaPolicyKey("safe-route", 4));
        policy.RuleCount.Should().Be(3);
        policy.TryGetRule(new NavigationAreaId(1), out NavigationAreaRule snow).Should().BeTrue();
        snow.IsAllowed.Should().BeTrue();
        snow.AdditionalEnterCost.Should().Be((Fixed64)3);
        policy.TryGetRule(new NavigationAreaId(2), out NavigationAreaRule lava).Should().BeTrue();
        lava.IsAllowed.Should().BeFalse();
        policy.TryGetRule(new NavigationAreaId(3), out _).Should().BeFalse();
    }

    [Fact]
    public void NavigationAreaContracts_ShouldRejectInvalidIdentityCostAndEmptyPolicy()
    {
        Action emptyId = () => _ = new NavigationAreaPolicyKey(" ", revision: 1);
        Action invalidRevision = () => _ = new NavigationAreaPolicyKey("fastest", revision: 0);
        Action negativeCost = () => _ = new NavigationAreaRule(true, -Fixed64.One);
        Action emptyRules = () => _ = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("fastest", 1),
            Array.Empty<NavigationAreaRule>());

        emptyId.Should().Throw<ArgumentException>();
        invalidRevision.Should().Throw<ArgumentOutOfRangeException>();
        negativeCost.Should().Throw<ArgumentOutOfRangeException>();
        emptyRules.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CellAndQueryIdentity_ShouldIncludeAreaAndPolicy()
    {
        NavigationCell asphalt = CreateCell(new NavigationAreaId(1));
        NavigationCell snow = CreateCell(new NavigationAreaId(2));
        PathQuery fastest = CreateQuery(new NavigationAreaPolicyKey("fastest", 1));
        PathQuery safest = CreateQuery(new NavigationAreaPolicyKey("safest", 1));

        asphalt.Should().NotBe(snow);
        fastest.Should().NotBe(safest);
        fastest.AreaPolicy.Should().Be(new NavigationAreaPolicyKey("fastest", 1));
    }

    [Fact]
    public void ContextCatalog_ShouldPublishIdempotentlyAndRejectConflictingOrStaleRevisions()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var first = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("ground", 1),
            new[] { new NavigationAreaRule(true, Fixed64.One) });
        var publishFirst = new NavigationAreaPolicyCommitOperation(first, 1, context.FrameCount + 1);

        context.Pathing.Admit(publishFirst).Should().BeTrue();
        context.Simulate();

        publishFirst.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryResolveNavigationAreaPolicy(first.Key, out NavigationAreaPolicy? resolved)
            .Should().BeTrue();
        resolved.Should().BeSameAs(first);

        var sameContent = new NavigationAreaPolicy(
            first.Key,
            new[] { new NavigationAreaRule(true, Fixed64.One) });
        var idempotent = new NavigationAreaPolicyCommitOperation(sameContent, 2, context.FrameCount + 1);
        context.Pathing.Admit(idempotent).Should().BeTrue();
        context.Simulate();
        idempotent.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var conflict = new NavigationAreaPolicy(
            first.Key,
            new[] { new NavigationAreaRule(false, Fixed64.One) });
        var conflicting = new NavigationAreaPolicyCommitOperation(conflict, 3, context.FrameCount + 1);
        context.Pathing.Admit(conflicting).Should().BeTrue();
        context.Simulate();
        conflicting.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);

        var second = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("ground", 2),
            new[] { new NavigationAreaRule(true, (Fixed64)2) });
        var publishSecond = new NavigationAreaPolicyCommitOperation(second, 4, context.FrameCount + 1);
        context.Pathing.Admit(publishSecond).Should().BeTrue();
        context.Simulate();
        context.Pathing.TryResolveNavigationAreaPolicy(first.Key, out _).Should().BeFalse();
        context.Pathing.TryResolveNavigationAreaPolicy(second.Key, out resolved).Should().BeTrue();
        resolved.Should().BeSameAs(second);

        var stale = new NavigationAreaPolicyCommitOperation(first, 5, context.FrameCount + 1);
        context.Pathing.Admit(stale).Should().BeTrue();
        context.Simulate();
        stale.Receipt.Rejection.Should().Be(NavigationOperationRejection.Stale);
    }

    private static NavigationCell CreateCell(NavigationAreaId area) =>
        new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            area,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);

    private static PathQuery CreateQuery(NavigationAreaPolicyKey areaPolicy)
    {
        var shape = new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero);
        var profile = new NavigationAgentProfile(
            shape,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);

        return new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.One),
            profile,
            areaPolicy,
            new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Solid, TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            allowTransitions: false);
    }
}
