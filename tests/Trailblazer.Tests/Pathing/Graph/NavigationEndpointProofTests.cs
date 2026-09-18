using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationEndpointProofTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MultiPageCapture_SortMustRespectTotalAndSliceBudgetWithoutLosingReadSet(int budget)
    {
        using var context = NavigationAStarConnectorTests.CreateLine(TraversalMedium.Solid, out var query);
        var store = context.Pathing.NavigationGraphStore;
        store.Current.AreaCatalog.TryGet(query.AreaPolicy, out var policy).Should().BeTrue();
        var workspace = new NavigationEndpointWorkspace(1, 2, 2);
        workspace.TryRecordPage("connector", 1).Should().BeTrue();
        workspace.TryRecordPage("connector", 0).Should().BeTrue();
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(budget, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        using var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(store.TryAcquire()!, query, default, default, policy, TraversalMedium.Solid,
            TraversalMedia.Solid, meter, context.World.ChangeSequence, false);
        var work = new NavigationEndpointProofWork(workspace, resolved,
            new GraphDependencyStamp(policy.Key, Array.Empty<GraphComponentDependency>(), Array.Empty<GraphPageDependency>()));
        var zero = new NavigationAcquisitionSlice(0);
        work.AdvanceCapture(ref zero).Should().Be(budget == 0
            ? NavigationQueryAdmissionStatus.BudgetExceeded : NavigationQueryAdmissionStatus.Pending);
        meter.LookupProbes.Should().Be(0);
        if (budget != 0)
        {
            var credit = new NavigationAcquisitionSlice(budget);
            work.AdvanceCapture(ref credit).Should().Be(NavigationQueryAdmissionStatus.Success);
            meter.LookupProbes.Should().Be(budget);
            work.Proof!.Pages.Should().HaveCount(2);
            work.Proof.Pages[0].PageIndex.Should().Be(0);
            work.Proof.Pages[1].PageIndex.Should().Be(1);
            store.Current.IsDependencyCurrent(work.Proof).Should().BeTrue();
        }
    }

    [Fact]
    public void SharedFlowAdmission_ReleasedPinMustFailStaleWithoutStartingBuild()
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        long bytes = fixture.Far.RetainedBytes;
        using var cache = new NavigationFlowFieldPayloadCache(fixture.World, 1, bytes, bytes, bytes, 2, 1,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(bytes, out NavigationFlowFieldReservation reservation).Should().BeTrue();
        cache.TryPublishOrPromote(fixture.Store, fixture.Far, fixture.FarOrigin, ref reservation, out var pin)
            .Should().Be(NavigationFlowFieldStatus.Success);
        using var source = new NavigationCohortSource { Flow = pin, HasFlow = true };
        var workspace = new NavigationFlowFieldWorkspace(2, 4, 4, 8, 64, 64);
        using var work = new NavigationFlowQueryWork(fixture.Store, workspace, cache);
        work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!, sharedSource: source);
        source.Dispose();
        for (int i = 0; i < 4096 && !work.IsPrepared; i++) work.PrepareSearchOrCheckout(64, 64);
        work.IsReadyToPublish.Should().BeTrue();
        work.Publish().Should().Be(NavigationFlowQueryStatus.Stale);
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        fixture.Store.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(10, true, false)]
    public void UncertifiedCapture_ShouldHonorTotalBudgetAndRejectMissingDependency(
        int budget, bool missingPage, bool succeeds)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        GraphDependencyStamp original = fixture.Far.Dependencies;
        var workspace = new NavigationEndpointWorkspace(1, 1, 1);
        workspace.TryRecordComponent(original.Components[0].Key).Should().BeTrue();
        workspace.TryRecordPage(missingPage ? "missing" : original.Pages[0].MapId,
            original.Pages[0].PageIndex).Should().BeTrue();
        fixture.Graph.AreaCatalog.TryGet(original.AreaPolicy, out NavigationAreaPolicy policy).Should().BeTrue();
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(budget, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        using var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(fixture.Store.TryAcquire()!, fixture.FarQuery, default, default, policy,
            TraversalMedium.Solid, TraversalMedia.Solid, meter, fixture.World.ChangeSequence, false);
        var unrelatedProof = new GraphDependencyStamp(new NavigationAreaPolicyKey("other", 1),
            Array.Empty<GraphComponentDependency>(), Array.Empty<GraphPageDependency>());
        var work = new NavigationEndpointProofWork(workspace, resolved, unrelatedProof);
        var slice = new NavigationAcquisitionSlice(10);
        work.AdvanceCertification(ref slice).Should().Be(NavigationQueryAdmissionStatus.Success);
        work.IsCertified.Should().BeFalse();
        work.AdvanceCapture(ref slice).Should().Be(succeeds ? NavigationQueryAdmissionStatus.Success
            : missingPage ? NavigationQueryAdmissionStatus.Stale : NavigationQueryAdmissionStatus.BudgetExceeded);
        meter.LookupProbes.Should().Be(succeeds || missingPage ? 2 : budget);
        if (succeeds)
        {
            work.Proof!.Components.Should().Equal(original.Components);
            work.Proof.Pages.Should().Equal(original.Pages);
            fixture.Graph.IsDependencyCurrent(work.Proof).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("aaa-before")]
    [InlineData("zzz-after")]
    public void Certification_ShouldSearchBothSidesWithoutCertifyingAnAbsentPage(string absentMap)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        GraphDependencyStamp original = fixture.Far.Dependencies;
        var workspace = new NavigationEndpointWorkspace(1, 1, 1);
        workspace.TryRecordPage(absentMap, 0).Should().BeTrue();
        fixture.Graph.AreaCatalog.TryGet(original.AreaPolicy, out NavigationAreaPolicy policy).Should().BeTrue();
        using var resolved = new NavigationResolvedPathQuery();
        var meter = new NavigationWorkMeter(fixture.FarQuery.Budget);
        resolved.Bind(fixture.Store.TryAcquire()!, fixture.FarQuery, default, default, policy,
            TraversalMedium.Solid, TraversalMedia.Solid, meter, fixture.World.ChangeSequence, false);
        var work = new NavigationEndpointProofWork(workspace, resolved, original);
        var slice = new NavigationAcquisitionSlice(1);
        work.AdvanceCertification(ref slice).Should().Be(NavigationQueryAdmissionStatus.Success);
        work.IsCertified.Should().BeFalse();
        meter.LookupProbes.Should().Be(1, "the one payload page is compared once and cannot certify another map");
        slice.LookupRemaining.Should().Be(0);
    }

    [Fact]
    public void CertifiedOrdinaryHit_CancellationDuringComparison_ShouldReleaseCheckoutAndGraph()
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        long bytes = fixture.Far.RetainedBytes;
        using var cache = new NavigationFlowFieldPayloadCache(fixture.World, 1, bytes, bytes, bytes, 2, 1,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(bytes, out NavigationFlowFieldReservation reservation).Should().BeTrue();
        cache.TryPublishOrPromote(fixture.Store, fixture.Far, fixture.FarOrigin, ref reservation,
            out NavigationFlowFieldPayloadLease pin).Should().Be(NavigationFlowFieldStatus.Success);
        using (pin)
        {
            var workspace = new NavigationFlowFieldWorkspace(2, 4, 4, 8, 64, 64);
            using var work = new NavigationFlowQueryWork(fixture.Store, workspace, cache);
            work.Begin(fixture.FarQuery, fixture.Store.TryAcquire()!);
            for (int i = 0; i < 4096 && cache.ActiveLeaseCount == 1; i++) work.PrepareSearchOrCheckout(1, 1);
            cache.ActiveLeaseCount.Should().Be(2, "checkout precedes resumable certificate comparison");
            work.IsPrepared.Should().BeFalse("one lookup cannot finish the complete certificate");
            cache.ReservedPayloadBytes.Should().Be(0, "covered admission needs no retained proof");
            fixture.Store.ActiveLeaseCount.Should().Be(1);
            work.Dispose();
            cache.ActiveLeaseCount.Should().Be(1);
            cache.LeasedBytes.Should().Be(bytes);
            cache.ReservedPayloadBytes.Should().Be(0);
            fixture.Store.ActiveLeaseCount.Should().Be(0);
        }
        cache.LeasedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Certification_ShouldChargeEveryComparisonAndVersionRead(int budget)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        GraphDependencyStamp proof = fixture.Far.Dependencies;
        proof.Components.Should().ContainSingle();
        proof.Pages.Should().ContainSingle();
        var workspace = new NavigationEndpointWorkspace(1, 1, 1);
        workspace.TryRecordComponent(proof.Components[0].Key).Should().BeTrue();
        workspace.TryRecordPage(proof.Pages[0].MapId, proof.Pages[0].PageIndex).Should().BeTrue();
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(budget, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        fixture.Graph.AreaCatalog.TryGet(proof.AreaPolicy, out NavigationAreaPolicy policy).Should().BeTrue();
        using var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(fixture.Store.TryAcquire()!, fixture.FarQuery, default, default, policy,
            TraversalMedium.Solid, TraversalMedia.Solid, meter, fixture.World.ChangeSequence, false);
        var work = new NavigationEndpointProofWork(workspace, resolved, proof);
        var zero = new NavigationAcquisitionSlice(0);
        work.AdvanceCertification(ref zero).Should().Be(NavigationQueryAdmissionStatus.Pending);
        meter.LookupProbes.Should().Be(0);
        for (int step = 0; step < 3; step++)
        {
            var slice = new NavigationAcquisitionSlice(1);
            work.AdvanceCertification(ref slice).Should().Be(budget == 3 && step == 2
                ? NavigationQueryAdmissionStatus.BudgetExceeded : NavigationQueryAdmissionStatus.Pending);
            slice.LookupRemaining.Should().Be(0);
            meter.LookupProbes.Should().Be(step + 1);
        }
        var last = new NavigationAcquisitionSlice(1);
        work.AdvanceCertification(ref last).Should().Be(budget == 4
            ? NavigationQueryAdmissionStatus.Success : NavigationQueryAdmissionStatus.BudgetExceeded);
        work.IsCertified.Should().Be(budget == 4);
        meter.LookupProbes.Should().Be(budget);
        if (work.IsCertified) work.Proof.Should().BeNull("a certified hit retains no new proof");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Certification_ShouldRejectSameAddressWithDifferentProof(int changed)
    {
        using var fixture = NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        GraphDependencyStamp original = fixture.Far.Dependencies;
        var components = (GraphComponentDependency[])original.Components.Clone();
        var pages = (GraphPageDependency[])original.Pages.Clone();
        if (changed == 0) components[0] = new GraphComponentDependency(components[0].Key, components[0].Version + 1);
        GraphPageDependency page = pages[0];
        if (changed == 1) pages[0] = new GraphPageDependency(page.MapId, page.BakeVersion,
            page.DynamicSlotGeneration, page.PageIndex, page.SemanticVersion + 1, page.PhysicalVersion, page.TransitionVersion);
        var proof = new GraphDependencyStamp(changed == 2 ? new NavigationAreaPolicyKey("other", 1) : original.AreaPolicy,
            components, pages, hasTransitionRuleDependency: changed == 4,
            transitionRuleVersion: fixture.Graph.TransitionRules.Version + 1);
        var workspace = new NavigationEndpointWorkspace(1, 1, 1);
        workspace.TryRecordComponent(original.Components[0].Key).Should().BeTrue();
        workspace.TryRecordPage(page.MapId, page.PageIndex).Should().BeTrue();
        if (changed is 3 or 4) workspace.Dependencies.RecordTransitionDependency();
        fixture.Graph.AreaCatalog.TryGet(original.AreaPolicy, out NavigationAreaPolicy policy).Should().BeTrue();
        using var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(fixture.Store.TryAcquire()!, fixture.FarQuery, default, default, policy,
            TraversalMedium.Solid, TraversalMedia.Solid, new NavigationWorkMeter(fixture.FarQuery.Budget),
            fixture.World.ChangeSequence, false);
        var work = new NavigationEndpointProofWork(workspace, resolved, proof);
        var slice = new NavigationAcquisitionSlice(100);
        work.AdvanceCertification(ref slice).Should().Be(NavigationQueryAdmissionStatus.Success);
        work.IsCertified.Should().BeFalse();
    }
}
