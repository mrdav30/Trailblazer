using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationGraphValueSemanticsTests
{
    [Fact]
    public void TransitionPage_ShouldLookupOutgoingAndUseCanonicalTransitionOrder()
    {
        var sourcePage = new NavigationTransitionPageAddress("map", 0);
        var destinationPage = new NavigationTransitionPageAddress("target", 0);
        TraversalTransitionDefinition Definition(
            string id,
            TraversalMedium sourceMedium,
            TraversalTransitionType type) => new(
            id,
            type,
            default,
            sourceMedium,
            new NavigationCellAddress("target", default),
            TraversalMedium.Gas);
        var baseline = new NavigationPublishedTransition(
            "map",
            Definition("base", TraversalMedium.Solid, TraversalTransitionType.Jump),
            sourcePage,
            destinationPage);
        var sourceMediumVariant = new NavigationPublishedTransition(
            "map",
            Definition("source-medium", TraversalMedium.Gas, TraversalTransitionType.Jump),
            sourcePage,
            destinationPage);
        var typeVariant = new NavigationPublishedTransition(
            "map",
            Definition("type", TraversalMedium.Solid, TraversalTransitionType.Climb),
            sourcePage,
            destinationPage);
        var idVariant = new NavigationPublishedTransition(
            "map",
            Definition("later-id", TraversalMedium.Solid, TraversalTransitionType.Jump),
            sourcePage,
            destinationPage);
        var page = new NavigationTransitionPage(
            sourcePage,
            version: 7,
            new[] { baseline },
            new[] { new NavigationIncomingTransitionRef(baseline) });

        page.TryGetOutgoing(baseline.Owner, out NavigationPublishedTransition found)
            .Should().BeTrue();
        found.Should().Be(baseline);
        page.TryGetOutgoing(
                new NavigationTransitionOwnerKey("map", "missing"),
                out NavigationPublishedTransition missing)
            .Should().BeFalse();
        missing.Should().Be(default(NavigationPublishedTransition));
        NavigationTransitionPage.CompareIncoming(
                new NavigationIncomingTransitionRef(baseline),
                new NavigationIncomingTransitionRef(sourceMediumVariant))
            .Should().BeLessThan(0);
        NavigationTransitionPage.CompareIncoming(
                new NavigationIncomingTransitionRef(baseline),
                new NavigationIncomingTransitionRef(typeVariant))
            .Should().BeLessThan(0);
        NavigationTransitionPage.CompareOutgoing(baseline, typeVariant)
            .Should().BeLessThan(0,
                "transition type precedes the final id tie-breaker in canonical outgoing order");
        NavigationTransitionPage.CompareOutgoing(baseline, idVariant)
            .Should().BeLessThan(0,
                "transition id is the final deterministic outgoing tie-breaker");
        NavigationTransitionPage.CompareIncoming(
                new NavigationIncomingTransitionRef(baseline),
                new NavigationIncomingTransitionRef(idVariant))
            .Should().BeLessThan(0,
                "transition id is the final deterministic incoming tie-breaker");
    }

    [Fact]
    public void TransitionPage_ShouldReportExactRetainedAccountingForEachOwnedArray()
    {
        var sourcePage = new NavigationTransitionPageAddress("map", 0);
        var destinationPage = new NavigationTransitionPageAddress("target", 0);
        var definition = new TraversalTransitionDefinition(
            "base",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("target", default),
            TraversalMedium.Gas);
        var baseline = new NavigationPublishedTransition(
            "map",
            definition,
            sourcePage,
            destinationPage);
        var complete = new NavigationTransitionPage(
            sourcePage,
            version: 7,
            new[] { baseline },
            new[] { new NavigationIncomingTransitionRef(baseline) });

        complete.PersistentPageCount.Should().Be(3,
            "the page plus both non-empty record arrays are retained independently");
        complete.IsEmpty.Should().BeFalse();

        var empty = new NavigationTransitionPage(
            sourcePage,
            version: 8,
            System.Array.Empty<NavigationPublishedTransition>(),
            System.Array.Empty<NavigationIncomingTransitionRef>());
        empty.IsEmpty.Should().BeTrue();
        empty.PersistentPageCount.Should().Be(1);
        empty.RetainedBytes.Should().Be(NavigationTransitionPage.BaseRetainedBytes);

        var outgoingOnly = new NavigationTransitionPage(
            sourcePage,
            version: 9,
            new[] { baseline },
            System.Array.Empty<NavigationIncomingTransitionRef>());
        outgoingOnly.PersistentPageCount.Should().Be(2);
        outgoingOnly.RetainedBytes.Should().Be(
            NavigationTransitionPage.BaseRetainedBytes
            + NavigationTransitionPage.GetArrayBytes(
                1,
                NavigationTransitionPage.OutgoingRecordBytes));

        var incomingOnly = new NavigationTransitionPage(
            destinationPage,
            version: 10,
            System.Array.Empty<NavigationPublishedTransition>(),
            new[] { new NavigationIncomingTransitionRef(baseline) });
        incomingOnly.PersistentPageCount.Should().Be(2);
        incomingOnly.RetainedBytes.Should().Be(
            NavigationTransitionPage.BaseRetainedBytes
            + NavigationTransitionPage.GetArrayBytes(
                1,
                NavigationTransitionPage.IncomingRecordBytes));
    }

    [Fact]
    public void GraphKeysAndRefs_ShouldUseCanonicalOrderAndRejectInvalidReferences()
    {
        var address = new NavigationCellAddress("map", default);
        var solid = new NavigationSurfaceComponentKey(address, TraversalMedium.Solid);
        var gas = new NavigationSurfaceComponentKey(address, TraversalMedium.Gas);
        solid.CompareTo(solid).Should().Be(0);
        solid.CompareTo(gas).Should().BeLessThan(0);
        gas.CompareTo(solid).Should().BeGreaterThan(0);
        solid.CompareTo(new NavigationSurfaceComponentKey(
                new NavigationCellAddress("other", default),
                TraversalMedium.Solid))
            .Should().BeLessThan(0);

        var baseOwner = new NavigationConnectionOwnerKey("map", "bridge");
        baseOwner.CompareTo(baseOwner).Should().Be(0);
        baseOwner.CompareTo(new NavigationConnectionOwnerKey("map", "ladder"))
            .Should().BeLessThan(0);
        baseOwner.CompareTo(new NavigationConnectionOwnerKey("other", "bridge"))
            .Should().BeLessThan(0);

        var node = new NavigationNodeRef(1, 2);
        var solidState = new NavigationMediumStateRef(node, TraversalMedium.Solid);
        solidState.CompareTo(solidState).Should().Be(0);
        new NavigationMediumStateRef(node, TraversalMedium.Gas)
            .CompareTo(solidState).Should().BeGreaterThan(0);

        new NavigationNodeRef(-1, 0).IsValid.Should().BeFalse();
        new NavigationNodeRef(0, -1).IsValid.Should().BeFalse();
        new NavigationNodeRef(int.MaxValue, 0).IsValid.Should().BeFalse();
        new NavigationNodeRef(0, int.MaxValue).IsValid.Should().BeFalse();
        node.IsValid.Should().BeTrue();

        new NavigationSelectedEdgeRef(address, TraversalMedium.Solid, 0)
            .IsValid.Should().BeTrue();
        new NavigationSelectedEdgeRef(default, TraversalMedium.Solid, 0)
            .IsValid.Should().BeFalse();
        new NavigationSelectedEdgeRef(address, TraversalMedium.Solid, -1)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Dependencies_ShouldDeduplicateOnlyExactLogicalIdentity()
    {
        var representative = new NavigationCellAddress("map", default);
        var componentKey = new NavigationSurfaceComponentKey(
            representative,
            TraversalMedium.Solid);
        AssertDeduplicates(
            new GraphComponentDependency(componentKey, version: 7),
            new GraphComponentDependency(componentKey, version: 7),
            new GraphComponentDependency(componentKey, version: 8));
        new GraphComponentDependency(componentKey, version: 7)
            .Equals(new GraphComponentDependency(
                new NavigationSurfaceComponentKey(representative, TraversalMedium.Gas),
                version: 7))
            .Should().BeFalse("the component key is part of dependency identity");

        AssertDeduplicates(
            new GraphPageDependency("map", 1, 2, 3, 4, 5, 6),
            new GraphPageDependency("map", 1, 2, 3, 4, 5, 6),
            new GraphPageDependency("map", 1, 2, 3, 4, 5, 7));
        var defaultDependency = default(GraphPageDependency);
        var defaults = new HashSet<GraphPageDependency>
        {
            defaultDependency,
            defaultDependency
        };
        defaults.Should().ContainSingle(
            "the default dependency sentinel must remain safe in deterministic sets");
    }

    [Fact]
    public void OwnersAndScopes_ShouldDeduplicateOnlyExactLogicalIdentity()
    {
        AssertDeduplicates(
            new NavigationConnectionOwnerKey("map", "bridge"),
            new NavigationConnectionOwnerKey("map", "bridge"),
            new NavigationConnectionOwnerKey("map", "ladder"));
        new NavigationConnectionOwnerKey("map", "bridge")
            .Equals(new NavigationConnectionOwnerKey("other", "bridge"))
            .Should().BeFalse("the owner map is part of connection identity");

        GridConfigurationKey firstConfiguration = ConfigurationKey(0);
        AssertDeduplicates(
            new NavigationGridChangeScope(firstConfiguration, 1, 2, 3),
            new NavigationGridChangeScope(firstConfiguration, 9, 8, 7),
            new NavigationGridChangeScope(ConfigurationKey(10), 1, 2, 3));

        AssertDeduplicates(
            new NavigationTransitionOwnerKey("map", "jump"),
            new NavigationTransitionOwnerKey("map", "jump"),
            new NavigationTransitionOwnerKey("map", "climb"));
        new NavigationTransitionOwnerKey("map", "jump")
            .Equals(new NavigationTransitionOwnerKey("other", "jump"))
            .Should().BeFalse("the owner map is part of transition identity");
        AssertDeduplicates(
            new NavigationTransitionPageAddress("map", 3),
            new NavigationTransitionPageAddress("map", 3),
            new NavigationTransitionPageAddress("map", 4));
    }

    [Fact]
    public void GraphReferences_ShouldDeduplicateOnlyExactLogicalIdentity()
    {
        var solidState = new NavigationMediumStateRef(
            new NavigationNodeRef(mapOrdinal: 1, cellSlot: 2),
            TraversalMedium.Solid);
        var sameSolidState = new NavigationMediumStateRef(
            new NavigationNodeRef(mapOrdinal: 1, cellSlot: 2),
            TraversalMedium.Solid);
        var gasState = new NavigationMediumStateRef(
            new NavigationNodeRef(mapOrdinal: 1, cellSlot: 2),
            TraversalMedium.Gas);
        AssertDeduplicates(solidState, sameSolidState, gasState);
        solidState.Equals(sameSolidState).Should().BeTrue();
        solidState.Equals(gasState).Should().BeFalse();
        solidState.CompareTo(new NavigationMediumStateRef(
                new NavigationNodeRef(mapOrdinal: 2, cellSlot: 0),
                TraversalMedium.Solid))
            .Should().BeLessThan(0);
        solidState.CompareTo(new NavigationMediumStateRef(
                new NavigationNodeRef(mapOrdinal: 1, cellSlot: 3),
                TraversalMedium.Solid))
            .Should().BeLessThan(0);
        solidState.CompareTo(gasState).Should().BeLessThan(0,
            "canonical state ordering is map, then cell slot, then exact medium");

        TraversalTransitionDefinition definition = new(
            "jump",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("target", default),
            TraversalMedium.Solid);
        var sourcePage = new NavigationTransitionPageAddress("map", 0);
        var destinationPage = new NavigationTransitionPageAddress("target", 0);
        var published = new NavigationPublishedTransition(
            "map",
            definition,
            sourcePage,
            destinationPage);
        var samePublished = new NavigationPublishedTransition(
            "map",
            definition,
            sourcePage,
            destinationPage);
        var differentPublished = new NavigationPublishedTransition(
            "map",
            definition,
            new NavigationTransitionPageAddress("map", 1),
            destinationPage);
        AssertDeduplicates(published, samePublished, differentPublished);
        AssertDeduplicates(
            new NavigationIncomingTransitionRef(published),
            new NavigationIncomingTransitionRef(samePublished),
            new NavigationIncomingTransitionRef(differentPublished));

        var selected = new NavigationSelectedEdgeRef(
            new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
            TraversalMedium.Solid,
            canonicalOutgoingOrdinal: 2);
        var sameSelected = new NavigationSelectedEdgeRef(
            new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
            TraversalMedium.Solid,
            canonicalOutgoingOrdinal: 2);
        var differentSelected = new NavigationSelectedEdgeRef(
            new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
            TraversalMedium.Solid,
            canonicalOutgoingOrdinal: 3);
        AssertDeduplicates(selected, sameSelected, differentSelected);
        selected.Equals(sameSelected).Should().BeTrue();
        selected.Equals(differentSelected).Should().BeFalse();
        selected.Equals(new NavigationSelectedEdgeRef(
                new NavigationCellAddress("other", new VoxelIndex(1, 0, 0)),
                TraversalMedium.Solid,
                canonicalOutgoingOrdinal: 2))
            .Should().BeFalse("the durable target is part of selected-edge identity");
        selected.Equals(new NavigationSelectedEdgeRef(
                selected.Target,
                TraversalMedium.Gas,
                canonicalOutgoingOrdinal: 2))
            .Should().BeFalse("the exact target medium is part of selected-edge identity");
    }

    [Fact]
    public void BudgetsAndStructuralLinks_ShouldUseExactValueIdentity()
    {
        MaintenanceWorkBudget budget = TrailblazerWorldContextSettings.Default.MaintenanceBudget;
        MaintenanceWorkBudget sameBudget = budget;
        var differentBudget = new MaintenanceWorkBudget(
            budget.MaxConsumedEnvelopes,
            budget.MaxBaselineAddresses + 1,
            budget.MaxOverlaySlots,
            budget.MaxComponentNodes,
            budget.MaxSeamCandidateProbes,
            budget.MaxExplicitEdges,
            budget.MaxDependencyEntries,
            budget.MaxSurfaceComponentEdges);
        AssertDeduplicates(budget, sameBudget, differentBudget);
        (budget == sameBudget).Should().BeTrue();
        (budget != differentBudget).Should().BeTrue();
        budget.Equals("not a maintenance budget").Should().BeFalse(
            "boxed value equality must reject a different runtime type");
        var differentEnvelopeBudget = new MaintenanceWorkBudget(
            budget.MaxConsumedEnvelopes + 1,
            budget.MaxBaselineAddresses,
            budget.MaxOverlaySlots,
            budget.MaxComponentNodes,
            budget.MaxSeamCandidateProbes,
            budget.MaxExplicitEdges,
            budget.MaxDependencyEntries,
            budget.MaxSurfaceComponentEdges);
        budget.Equals(differentEnvelopeBudget).Should().BeFalse(
            "the envelope limit is part of exact deterministic budget identity");
        var differentSurfaceEdgeBudget = new MaintenanceWorkBudget(
            budget.MaxConsumedEnvelopes,
            budget.MaxBaselineAddresses,
            budget.MaxOverlaySlots,
            budget.MaxComponentNodes,
            budget.MaxSeamCandidateProbes,
            budget.MaxExplicitEdges,
            budget.MaxDependencyEntries,
            budget.MaxSurfaceComponentEdges + 1);
        budget.Equals(differentSurfaceEdgeBudget).Should().BeFalse(
            "the final surface-edge limit is part of exact deterministic budget identity");

        var link = new NavigationStructuralLink("target", count: 2, uncertifiedCount: 1);
        link.Equals(new NavigationStructuralLink("target", 2, 1)).Should().BeTrue();
        link.Equals(new NavigationStructuralLink("target", 3, 1)).Should().BeFalse();
    }

    private static void AssertDeduplicates<T>(T first, T equal, T different)
        where T : notnull, System.IEquatable<T>
    {
        first.Equals(equal).Should().BeTrue();
        first.Equals(different).Should().BeFalse();
        first.GetHashCode().Should().Be(equal.GetHashCode());
        var values = new HashSet<T>();
        values.Add(first).Should().BeTrue();
        values.Add(equal).Should().BeFalse();
        values.Add(different).Should().BeTrue();
        values.Should().HaveCount(2);
    }

    private static GridConfigurationKey ConfigurationKey(int x)
    {
        var position = new Vector3d(x, 0, 0);
        var configuration = new GridConfiguration(position, position);
        configuration.TryNormalize(out NormalizedGridConfiguration normalized)
            .Should().BeTrue();
        return normalized.Key;
    }
}
