//=======================================================================
// NavigationFlowFieldCacheTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationFlowFieldCacheTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void DependencySubset_ShouldRequireEveryConsumedTransitionRuleProof(
        bool shorterHasTransitionDependency,
        bool longerHasTransitionDependency,
        bool expected)
    {
        var shorter = new GraphDependencyStamp(
            default,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>(),
            shorterHasTransitionDependency,
            transitionRuleVersion: 1);
        var longer = new GraphDependencyStamp(
            default,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>(),
            longerHasTransitionDependency,
            transitionRuleVersion: 1);

        NavigationFlowFieldPayloadCache.DependenciesAreSubset(shorter, longer)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData((int)NavigationFlowFieldStatus.Stale, false, false)]
    [InlineData((int)NavigationFlowFieldStatus.Stale, true, false)]
    [InlineData((int)NavigationFlowFieldStatus.Success, false, false)]
    [InlineData((int)NavigationFlowFieldStatus.Success, true, true)]
    public void BoundGuideCurrentness_ShouldRequireLeaseAndPayloadOwnership(
        int leaseStatusValue,
        bool payloadCurrent,
        bool expected)
    {
        NavigationFlowFieldPayloadCache.IsBoundGuideCurrent(
                (NavigationFlowFieldStatus)leaseStatusValue,
                payloadCurrent)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoundGuideValidation_ShouldRetainOrReleaseExactPayloadOwnership(
        bool current)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(
                extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 1,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);

        NavigationFlowFieldPayloadCache.ResolveBoundGuideValidation(
                current,
                ref guide)
            .Should().Be(current
                ? NavigationGuideStatus.Success
                : NavigationGuideStatus.Stale);

        cache.ActiveLeaseCount.Should().Be(current ? 1 : 0);
        cache.LeasedBytes.Should().Be(current ? fixture.Far.RetainedBytes : 0);
        guide.Status.Should().Be(current
            ? NavigationGuideStatus.Success
            : NavigationGuideStatus.Stale);
        guide.Dispose();
    }

    [Theory]
    [InlineData((int)NavigationFlowFieldStatus.Success, true)]
    [InlineData((int)NavigationFlowFieldStatus.NoPath, true)]
    [InlineData((int)NavigationFlowFieldStatus.CostOverflow, true)]
    [InlineData((int)NavigationFlowFieldStatus.Stale, false)]
    public void ValidatedProof_ShouldBeRetainedUnlessFinalValidationIsStale(
        int statusValue,
        bool expectedRetained)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(
                extraIntegrationCost: Fixed64.Zero);

        NavigationFlowFieldPayload? proof =
            NavigationFlowFieldPayloadCache.ResolveValidatedProof(
                (NavigationFlowFieldStatus)statusValue,
                fixture.Far);

        if (expectedRetained)
            proof.Should().BeSameAs(fixture.Far);
        else
            proof.Should().BeNull();
    }

    [Fact]
    public void DefaultPayloadLease_ShouldFailClosedWithoutReturningAPayload()
    {
        NavigationFlowFieldPayloadLease lease = default;

        lease.TryGetPayload(out NavigationFlowFieldPayload payload)
            .Should().Be(NavigationFlowFieldStatus.Stale);
        payload.Should().BeNull();
        lease.Dispose();
    }

    [Fact]
    public void DisposedCache_ShouldKeepResetAndDisposeIdempotent()
    {
        using var world = new GridWorld();
        var cache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: 0,
            maxActivePayloadBytes: 0,
            maxActiveLeases: 0,
            guideMapCapacity: 0,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        cache.Dispose();

        cache.Reset();
        cache.Dispose();
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PayloadCache_ShouldRejectNegativeByteCeilings(int ceiling)
    {
        using var world = new GridWorld();
        long maxReusableBytes = ceiling == 0 ? -1 : 0;
        long maxSinglePayloadBytes = ceiling == 1 ? -1 : 0;
        long maxActivePayloadBytes = ceiling == 2 ? -1 : 0;

        Action construct = () => _ = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 0,
            maxReusableBytes,
            maxSinglePayloadBytes,
            maxActivePayloadBytes,
            maxActiveLeases: 0,
            guideMapCapacity: 0,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        construct.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PayloadReservation_ShouldRejectNegativeByteCeiling()
    {
        using var world = new GridWorld();
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: 0,
            maxActivePayloadBytes: 0,
            maxActiveLeases: 0,
            guideMapCapacity: 0,
            NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        Action reserve = () => cache.TryReservePayload(-1, out _);

        reserve.Should().ThrowExactly<ArgumentOutOfRangeException>();
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void GuideCreation_MapTableOneBelowRequiredCapacity_ShouldReleasePayloadLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);

        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.CapacityExceeded);

        guide.Should().Be(default(NavigationFlowFieldLease));
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void GuideCreation_OriginOutsidePublishedPrefix_ShouldFailStaleAndReleaseLease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 1,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        var uncoveredOrigin = new NavigationCellAddress(
            fixture.Far.Key.DestinationAddress.MapId,
            new VoxelIndex(4, 0, 0));
        fixture.Graph.TryGetNodeRef(uncoveredOrigin, out _).Should().BeTrue();
        fixture.Far.TryGetNode(uncoveredOrigin, TraversalMedium.Solid, out _)
            .Should().BeFalse();

        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(uncoveredOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Stale);

        guide.Should().Be(default(NavigationFlowFieldLease));
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void GuideCreation_DependencyChangedAfterPayloadCheckout_ShouldFailStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 1,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        NavigationWorldGraph changed = fixture.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(fixture.Graph.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);

        cache.TryCreateGuide(
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Stale);

        guide.Should().Be(default(NavigationFlowFieldLease));
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void Checkout_ShouldRejectNewerPayloadForOlderLeasedGraphWithoutEviction()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        NavigationWorldGraph newerBase = fixture.Graph.WithGraphVersion(
            fixture.Graph.GraphVersion + 1);
        NavigationWorldGraph newer = newerBase.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(newerBase));
        using NavigationWorldGraphStore newerStore =
            NavigationAStarExitTestHarness.CreateStore(newer);
        NavigationFlowFieldPayload newerPayload =
            NavigationFlowFieldCacheTestHarness.RunFlow(
                fixture.World,
                newerStore,
                newer,
                fixture.FarQuery,
                fixture.FarOrigin,
                fixture.Far.Key.DestinationAddress,
                NavigationFlowFieldStatus.Success);
        using NavigationWorldGraphLease olderLease = fixture.Store.TryAcquire()!;
        fixture.Store.TryPublish(newer)
            .Should().Be(NavigationCandidatePublication.Published);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: newerPayload.RetainedBytes,
            maxSinglePayloadBytes: newerPayload.RetainedBytes,
            maxActivePayloadBytes: newerPayload.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                newerPayload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                newerPayload,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease published)
            .Should().Be(NavigationFlowFieldStatus.Success);
        published.Dispose();

        cache.TryCheckout(
                fixture.Store,
                olderLease.Graph,
                newerPayload.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease rejected,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        rejected.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(1,
            "an older in-flight query cannot evict a payload valid for the current graph");

        cache.TryCheckout(
                fixture.Store,
                newer,
                newerPayload.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease current,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        current.TryGetPayload(out NavigationFlowFieldPayload retained)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retained.Should().BeSameAs(newerPayload);
        current.Dispose();
    }

    [Fact]
    public void Publication_ShouldReplaceAStaleIncumbentWithTheCurrentProof()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        NavigationWorldGraph newerBase = fixture.Graph.WithGraphVersion(
            fixture.Graph.GraphVersion + 1);
        NavigationWorldGraph newer = newerBase.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(newerBase));
        using NavigationWorldGraphStore newerStore =
            NavigationAStarExitTestHarness.CreateStore(newer);
        NavigationFlowFieldPayload newerPayload =
            NavigationFlowFieldCacheTestHarness.RunFlow(
                fixture.World,
                newerStore,
                newer,
                fixture.FarQuery,
                fixture.FarOrigin,
                fixture.Far.Key.DestinationAddress,
                NavigationFlowFieldStatus.Success);
        long maximumBytes = Math.Max(fixture.Far.RetainedBytes, newerPayload.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: maximumBytes,
            maxSinglePayloadBytes: maximumBytes,
            maxActivePayloadBytes: maximumBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        fixture.Store.TryPublish(newer)
            .Should().Be(NavigationCandidatePublication.Published);
        cache.TryReservePayload(
                newerPayload.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                fixture.Store,
                newerPayload,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);

        lease.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(newerPayload,
            "a dependency-stale incumbent must not suppress a current proof with the same key");
        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(newerPayload.RetainedBytes);
        lease.Dispose();
    }

    [Fact]
    public void NearThenFar_ShouldPromoteAndDetachTheActiveSmallerPrefix()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long activeBytes = checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 4,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        NavigationFlowFieldPayloadLease nearLease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        cache.CachedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.LeasedBytes.Should().Be(fixture.Near.RetainedBytes);

        NavigationFlowFieldPayloadLease farLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);

        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.LeasedBytes.Should().Be(activeBytes);
        cache.DetachedBytes.Should().Be(fixture.Near.RetainedBytes);
        nearLease.TryGetPayload(out NavigationFlowFieldPayload nearPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        nearPayload.Should().BeSameAs(fixture.Near);
        farLease.TryGetPayload(out NavigationFlowFieldPayload farPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        farPayload.Should().BeSameAs(fixture.Far);

        nearLease.Dispose();
        cache.DetachedBytes.Should().Be(0);
        farLease.Dispose();
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void ExistingLongerPrefix_ShouldNotActivateAgainstAShorterReservation()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload other =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                1);
        long activeBytes = checked(other.RetainedBytes + fixture.Near.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 2,
            maxReusableBytes: checked(fixture.Far.RetainedBytes + other.RetainedBytes),
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        NavigationFlowFieldPayloadLease otherLease = Publish(
            cache,
            fixture,
            other,
            fixture.FarOrigin);
        cache.TryReservePayload(
                fixture.Near.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Near,
                fixture.NearOrigin,
                ref reservation,
                out _)
            .Should().Be(NavigationFlowFieldStatus.CapacityExceeded,
                "the longer canonical payload, not the shorter candidate, owns the activation bytes");

        reservation.Should().NotBe(default(NavigationFlowFieldReservation));
        cache.ActiveLeaseCount.Should().Be(1);
        cache.LeasedBytes.Should().Be(other.RetainedBytes);
        cache.ReservedPayloadBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.ReleasePayloadReservation(ref reservation);
        otherLease.Dispose();
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease canonical,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void Promotion_WhenIncumbentIsLru_ShouldEvictTheUnrelatedEntry()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload unrelated =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                1);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 2,
            maxReusableBytes: checked(fixture.Near.RetainedBytes + unrelated.RetainedBytes),
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, fixture.Near, fixture.NearOrigin).Dispose();
        Publish(cache, fixture, unrelated, fixture.FarOrigin).Dispose();

        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();

        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                unrelated.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending,
                "promotion replaces the incumbent in place and evicts the unrelated LRU entry");
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease promoted,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        promoted.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(fixture.Far);
        promoted.Dispose();
    }

    [Fact]
    public void RemoveExact_ShouldInvalidateOnlyTheExactPayloadAcrossPromotion()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long activeBytes = checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 4,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease nearLease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        NavigationFlowFieldPayloadLease farLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);

        cache.RemoveExact(fixture.Near);

        nearLease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        farLease.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(fixture.Far);
        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        nearLease.Dispose();
        cache.DetachedBytes.Should().Be(0);

        cache.RemoveExact(fixture.Far);

        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        farLease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.DetachedBytes.Should().Be(fixture.Far.RetainedBytes);
        farLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void Checkout_WhenOriginExistsButExtraMarginIsMissing_ShouldRemainAMiss()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease lease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        lease.Dispose();

        fixture.Near.TryGetNode(
                fixture.MarginOrigin,
                TraversalMedium.Solid,
                out _)
            .Should().BeTrue(
            "the cache must check the requested extra-cost margin, not just node presence");
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Near.Key,
                fixture.MarginOrigin,
                out NavigationFlowFieldPayloadLease checkout,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        checkout.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ReusedLeaseSlot_ShouldRejectStaleCopiesAndDoubleDisposal()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease first = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        NavigationFlowFieldPayloadLease staleCopy = first;
        first.Dispose();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease rebound,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        staleCopy.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        staleCopy.Dispose();
        staleCopy.Dispose();
        cache.ActiveLeaseCount.Should().Be(1);
        rebound.TryGetPayload(out NavigationFlowFieldPayload payload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        payload.Should().BeSameAs(fixture.Far);
        rebound.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FarPrefix_ShouldServeNearAndRepeatedOriginsWithoutPromotion()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease published = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        published.Dispose();

        for (int i = 0; i < 3; i++)
        {
            cache.TryCheckout(
                    fixture.Store,
                    fixture.Store.Current,
                    fixture.Far.Key,
                    fixture.NearOrigin,
                    out NavigationFlowFieldPayloadLease lease,
                    out _)
                .Should().Be(NavigationFlowFieldStatus.Success);
            lease.TryGetPayload(out NavigationFlowFieldPayload payload)
                .Should().Be(NavigationFlowFieldStatus.Success);
            payload.Should().BeSameAs(fixture.Far);
            lease.Dispose();
        }
        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
    }

    [Fact]
    public void CompleteFieldMissingOrigin_ShouldCacheReusableNoPathWithoutALease()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        fixture.Complete.IsComplete.Should().BeTrue();
        var unreachable = new NavigationCellAddress(
            fixture.FarOrigin.MapId,
            new VoxelIndex(99, 0, 0));
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Complete.RetainedBytes,
            maxSinglePayloadBytes: fixture.Complete.RetainedBytes,
            maxActivePayloadBytes: fixture.Complete.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                fixture.Complete.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Complete,
                unreachable,
                ref reservation,
                out NavigationFlowFieldPayloadLease published)
            .Should().Be(NavigationFlowFieldStatus.NoPath);

        published.Should().Be(default(NavigationFlowFieldPayloadLease));
        reservation.Should().Be(default(NavigationFlowFieldReservation));
        cache.Count.Should().Be(1);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Complete.Key,
                unreachable,
                out NavigationFlowFieldPayloadLease checkout,
                out NavigationFlowFieldPayload? proof)
            .Should().Be(NavigationFlowFieldStatus.NoPath);
        checkout.Should().Be(default(NavigationFlowFieldPayloadLease));
        proof.Should().BeSameAs(fixture.Complete);
        cache.IsExactProofCurrent(
                fixture.Store,
                proof!,
                unreachable,
                NavigationFlowFieldStatus.NoPath)
            .Should().BeTrue(
                "the canonical complete field remains the exact reusable negative proof");
        cache.Count.Should().Be(1);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void DuplicateCompleteNoPathPublication_ShouldReleaseTheSecondReservation()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        var unreachable = new NavigationCellAddress(
            fixture.FarOrigin.MapId,
            new VoxelIndex(99, 0, 0));
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Complete.RetainedBytes,
            maxSinglePayloadBytes: fixture.Complete.RetainedBytes,
            maxActivePayloadBytes: fixture.Complete.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        for (int publication = 0; publication < 2; publication++)
        {
            cache.TryReservePayload(
                    fixture.Complete.RetainedBytes,
                    out NavigationFlowFieldReservation reservation)
                .Should().BeTrue();

            cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Complete,
                    unreachable,
                    ref reservation,
                    out NavigationFlowFieldPayloadLease lease)
                .Should().Be(NavigationFlowFieldStatus.NoPath);

            lease.Should().Be(default(NavigationFlowFieldPayloadLease));
            reservation.Should().Be(default(NavigationFlowFieldReservation));
            cache.ReservedLeaseCount.Should().Be(0);
            cache.ReservedPayloadBytes.Should().Be(0);
        }

        cache.Count.Should().Be(1,
            "the second proof must reuse rather than replace the canonical complete field");
        cache.ActiveLeaseCount.Should().Be(0);
        cache.CachedBytes.Should().Be(fixture.Complete.RetainedBytes);
    }

    [Fact]
    public void CompleteFieldMissingOrigin_ShouldReleaseANonReusableProofImmediately()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        var unreachable = new NavigationCellAddress(
            fixture.FarOrigin.MapId,
            new VoxelIndex(99, 0, 0));
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: fixture.Complete.RetainedBytes,
            maxActivePayloadBytes: fixture.Complete.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                fixture.Complete.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Complete,
                unreachable,
                ref reservation,
                out NavigationFlowFieldPayloadLease published)
            .Should().Be(NavigationFlowFieldStatus.NoPath);

        published.Should().Be(default(NavigationFlowFieldPayloadLease));
        reservation.Should().Be(default(NavigationFlowFieldReservation));
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void CompleteCachedField_ShouldReportOverflowForAFartherCoveredOrigin()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        NavigationCellAddress destination = fixture.Complete.Key.DestinationAddress;
        PathQuery seedQuery = new(
            fixture.FarQuery.End,
            fixture.FarQuery.End,
            fixture.FarQuery.Agent,
            fixture.FarQuery.AreaPolicy,
            fixture.FarQuery.Traversal,
            PathAlgorithm.FlowField,
            fixture.FarQuery.Budget,
            fixture.FarQuery.AllowTransitions,
            new FlowFieldQueryOptions(Fixed64.MaxValue));
        NavigationFlowFieldPayload complete = NavigationFlowFieldCacheTestHarness.RunFlow(
            fixture.World,
            fixture.Store,
            fixture.Graph,
            seedQuery,
            destination,
            destination,
            NavigationFlowFieldStatus.Success);
        complete.IsComplete.Should().BeTrue();
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: complete.RetainedBytes,
            maxSinglePayloadBytes: complete.RetainedBytes,
            maxActivePayloadBytes: complete.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                complete.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                complete,
                destination,
                ref reservation,
                out NavigationFlowFieldPayloadLease published)
            .Should().Be(NavigationFlowFieldStatus.Success);
        published.Dispose();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                complete.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease checkout,
                out NavigationFlowFieldPayload? proof)
            .Should().Be(NavigationFlowFieldStatus.CostOverflow);

        checkout.Should().Be(default(NavigationFlowFieldPayloadLease));
        proof.Should().BeSameAs(complete);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void DuplicatePublication_ShouldReturnTheIncumbentWithoutDoubleAccounting()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long activeBytes = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation firstReservation)
            .Should().BeTrue();
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation secondReservation)
            .Should().BeTrue();
        NavigationFlowFieldPayload duplicate =
            NavigationFlowFieldCacheTestHarness.Clone(fixture.Far, fixture.Far.Key);

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref firstReservation,
                out NavigationFlowFieldPayloadLease first)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryPublishOrPromote(
                fixture.Store,
                duplicate,
                fixture.FarOrigin,
                ref secondReservation,
                out NavigationFlowFieldPayloadLease second)
            .Should().Be(NavigationFlowFieldStatus.Success);

        second.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(fixture.Far);
        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.LeasedBytes.Should().Be(fixture.Far.RetainedBytes);
        second.Dispose();
        first.Dispose();
    }

    [Fact]
    public void SameKeyNonPrefixPublication_ShouldFailWithoutMutatingTheCache()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease nearLease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        nearLease.Dispose();
        NavigationFlowFieldNode[] malformedNodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        NavigationFlowFieldNode first = malformedNodes[0];
        malformedNodes[0] = new NavigationFlowFieldNode(
            first.Address,
            first.Medium,
            Fixed64.One,
            first.SelectedEdge,
            first.TransitionInstructionOrdinal);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            malformedNodes,
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                malformed,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictPrefixWithNonIncreasingFirstExtraCost_ShouldFailTheInvariant(
        bool useLowerCost)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Near, fixture.NearOrigin).Dispose();
        NavigationFlowFieldNode[] malformedNodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        int firstExtraOrdinal = fixture.Near.Nodes.Length;
        NavigationFlowFieldNode firstExtra = malformedNodes[firstExtraOrdinal];
        malformedNodes[firstExtraOrdinal] = new NavigationFlowFieldNode(
            firstExtra.Address,
            firstExtra.Medium,
            useLowerCost ? Fixed64.Zero : fixture.Near.LastSettledCost,
            firstExtra.SelectedEdge,
            firstExtra.TransitionInstructionOrdinal);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            malformedNodes,
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        cache.TryReservePayload(
                malformed.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                malformed,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void SameKeyPrefixWithDifferentNodeMedium_ShouldFailTheInvariant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        NavigationFlowFieldNode[] malformedNodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        NavigationFlowFieldNode first = malformedNodes[0];
        malformedNodes[0] = new NavigationFlowFieldNode(
            first.Address,
            TraversalMedium.Gas,
            first.IntegrationCost,
            first.SelectedEdge,
            first.TransitionInstructionOrdinal);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            malformedNodes,
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        cache.TryReservePayload(
                malformed.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                malformed,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void SameKeyPrefixWithDifferentTransitionInstruction_ShouldFailTheInvariant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldNode[] nodes =
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone();
        int nodeOrdinal = -1;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].SelectedEdge.IsValid)
            {
                nodeOrdinal = i;
                break;
            }
        }
        nodeOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationFlowFieldNode selected = nodes[nodeOrdinal];
        nodes[nodeOrdinal] = new NavigationFlowFieldNode(
            selected.Address,
            selected.Medium,
            selected.IntegrationCost,
            selected.SelectedEdge,
            transitionInstructionOrdinal: 0);
        NavigationTransitionInstruction Instruction(string id) => new(
            NavigationTransitionIdentityKind.Definition,
            selected.Address.MapId,
            id,
            TraversalTransitionType.Jump,
            selected.Address,
            selected.SelectedEdge.Target,
            selected.Medium,
            selected.SelectedEdge.TargetMedium,
            Vector3d.Zero,
            Vector3d.Zero,
            TraversalTransitionLocomotionHints.None);
        NavigationFlowFieldPayload Payload(string id) => new(
            fixture.Far.Key,
            (NavigationFlowFieldNode[])nodes.Clone(),
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            new[] { Instruction(id) },
            fixture.Far.Dependencies,
            fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        NavigationFlowFieldPayload first = Payload("first");
        NavigationFlowFieldPayload different = Payload("different");
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: first.RetainedBytes,
            maxSinglePayloadBytes: first.RetainedBytes,
            maxActivePayloadBytes: first.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                first.RetainedBytes,
                out NavigationFlowFieldReservation firstReservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                first,
                fixture.FarOrigin,
                ref firstReservation,
                out NavigationFlowFieldPayloadLease firstLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        firstLease.Dispose();
        cache.TryReservePayload(
                different.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                different,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void WorldStampedPayload_ShouldBeRejectedAfterGridWorldMutation()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        var stamped = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone(),
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete,
            fixture.World.ChangeSequence);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, stamped, fixture.FarOrigin).Dispose();
        GridConfiguration added = new(
            new Vector3d(100, 0, 0),
            Vector3d.One,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        fixture.World.TryAddGrid(added, new[] { default(VoxelIndex) }, out _)
            .Should().BeTrue();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                stamped.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease lease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Stale);

        lease.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void PostLockValidation_StaleDependencyShouldReleaseTheExactLeaseAccounting()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease lease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        NavigationWorldGraph changed = fixture.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(fixture.Graph.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);

        cache.CompletePostLockValidation(
                fixture.Store,
                fixture.Far,
                NavigationFlowFieldStatus.Success,
                ref lease)
            .Should().Be(NavigationFlowFieldStatus.Stale);

        lease.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictPrefix_ShouldPreserveTransitionRuleDependencySubset(
        bool shorterDependsOnRules)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long ruleVersion = fixture.Graph.TransitionRules.Version;
        GraphDependencyStamp WithRuleDependency(
            GraphDependencyStamp source,
            bool hasDependency) => new(
            source.AreaPolicy,
            source.Components,
            source.Pages,
            hasDependency,
            ruleVersion);
        NavigationFlowFieldPayload WithDependencies(
            NavigationFlowFieldPayload source,
            bool hasDependency) => new(
            source.Key,
            (NavigationFlowFieldNode[])source.Nodes.Clone(),
            (int[])source.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])source.TransitionInstructions.Clone(),
            WithRuleDependency(source.Dependencies, hasDependency),
            source.IsComplete,
            source.WorldChangeSequence);
        NavigationFlowFieldPayload near = WithDependencies(
            fixture.Near,
            shorterDependsOnRules);
        NavigationFlowFieldPayload far = WithDependencies(
            fixture.Far,
            !shorterDependsOnRules);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, near, fixture.NearOrigin).Dispose();
        cache.TryReservePayload(
                far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        Action publish = () => cache.TryPublishOrPromote(
            fixture.Store,
            far,
            fixture.FarOrigin,
            ref reservation,
            out _);

        if (shorterDependsOnRules)
        {
            publish.Should().Throw<InvalidOperationException>();
            cache.ReleasePayloadReservation(ref reservation);
        }
        else
        {
            publish.Should().NotThrow();
            reservation.Should().Be(default(NavigationFlowFieldReservation));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictPrefix_ShouldPreserveWorldDependencySubset(
        bool shorterHasWorldStamp)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        NavigationFlowFieldPayload WithWorldStamp(
            NavigationFlowFieldPayload source,
            bool hasStamp) => new(
            source.Key,
            (NavigationFlowFieldNode[])source.Nodes.Clone(),
            (int[])source.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])source.TransitionInstructions.Clone(),
            source.Dependencies,
            source.IsComplete,
            hasStamp ? fixture.World.ChangeSequence : null);
        NavigationFlowFieldPayload near = WithWorldStamp(
            fixture.Near,
            shorterHasWorldStamp);
        NavigationFlowFieldPayload far = WithWorldStamp(
            fixture.Far,
            !shorterHasWorldStamp);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, near, fixture.NearOrigin).Dispose();
        cache.TryReservePayload(
                far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        Action publish = () => cache.TryPublishOrPromote(
            fixture.Store,
            far,
            fixture.FarOrigin,
            ref reservation,
            out _);

        if (shorterHasWorldStamp)
        {
            publish.Should().Throw<InvalidOperationException>();
            cache.ReleasePayloadReservation(ref reservation);
        }
        else
        {
            publish.Should().NotThrow();
            reservation.Should().Be(default(NavigationFlowFieldReservation));
        }
    }

    [Fact]
    public void NonReusablePayload_ShouldRemainDetachedOnlyUntilReturn()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

        NavigationFlowFieldPayloadLease lease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);

        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.LeasedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        lease.Dispose();
        cache.DetachedBytes.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void CapacityCeilings_ShouldBoundBytesEntriesAndLeaseSlotsIndependently()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var oneByteShort = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes - 1,
            maxActivePayloadBytes: fixture.Far.RetainedBytes - 1,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        oneByteShort.TryReservePayload(fixture.Far.RetainedBytes, out _)
            .Should().BeFalse();

        using var leaseCapped = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease sole = Publish(
            leaseCapped,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        leaseCapped.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.CapacityExceeded,
                "lease count is independent even when unique active bytes do not grow");
        sole.Dispose();

        NavigationFlowFieldPayload second =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                1);
        NavigationFlowFieldPayload third =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                2);
        long twoPayloadBytes = checked(fixture.Far.RetainedBytes * 2);
        using var lru = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 2,
            maxReusableBytes: twoPayloadBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(lru, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        Publish(lru, fixture, second, fixture.FarOrigin).Dispose();
        lru.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease touched,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        touched.Dispose();
        Publish(lru, fixture, third, fixture.FarOrigin).Dispose();
        lru.Count.Should().Be(2);
        lru.CachedBytes.Should().Be(twoPayloadBytes);
        lru.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                second.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        lru.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease retained,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retained.Dispose();
    }

    [Fact]
    public void Lru_ShouldRetainATouchedMiddleEntryWhenTheNextPayloadIsPublished()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        NavigationFlowFieldPayload second =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                1);
        NavigationFlowFieldPayload third =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                2);
        NavigationFlowFieldPayload fourth =
            NavigationFlowFieldCacheTestHarness.CloneWithExpandedNodeBudget(
                fixture.Far,
                fixture.FarQuery,
                3);
        long threePayloadBytes = checked(fixture.Far.RetainedBytes * 3);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 3,
            maxReusableBytes: threePayloadBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        Publish(cache, fixture, second, fixture.FarOrigin).Dispose();
        Publish(cache, fixture, third, fixture.FarOrigin).Dispose();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                second.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease touchedMiddle,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        touchedMiddle.Dispose();
        Publish(cache, fixture, fourth, fixture.FarOrigin).Dispose();

        cache.Count.Should().Be(3);
        cache.CachedBytes.Should().Be(threePayloadBytes);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending,
                "the untouched oldest entry owns the eviction slot");
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                second.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease retainedMiddle,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success,
                "touching a middle entry makes it the most recent entry");
        retainedMiddle.Dispose();
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                third.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease retainedThird,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retainedThird.Dispose();
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fourth.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease retainedFourth,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retainedFourth.Dispose();
    }

    [Fact]
    public void Reset_ShouldInvalidateActiveHandlesCancelReservationsAndReleaseReferences()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long activeBytes = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease lease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation staleReservation)
            .Should().BeTrue();

        cache.Reset();

        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(fixture.Far.RetainedBytes);
        lease.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.ReleasePayloadReservation(ref staleReservation);
        cache.ReservedLeaseCount.Should().Be(0);
        lease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void WarmReset_ShouldClearCountersAndLruWithoutAllocating()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture);
        for (int i = 0; i < 8; i++)
        {
            Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
            cache.Reset();
        }
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();
        cache.Reset();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void WarmCheckoutAndReturn_ShouldAllocateZeroBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        for (int i = 0; i < 8; i++)
        {
            cache.TryCheckout(
                    fixture.Store,
                    fixture.Store.Current,
                    fixture.Far.Key,
                    fixture.FarOrigin,
                    out NavigationFlowFieldPayloadLease warm,
                    out _)
                .Should().Be(NavigationFlowFieldStatus.Success);
            warm.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Success);
            warm.Dispose();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool succeeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (cache.TryCheckout(
                    fixture.Store,
                    fixture.Store.Current,
                    fixture.Far.Key,
                    fixture.FarOrigin,
                    out NavigationFlowFieldPayloadLease lease,
                    out _)
                != NavigationFlowFieldStatus.Success
                || lease.TryGetPayload(out _) != NavigationFlowFieldStatus.Success)
            {
                succeeded = false;
                break;
            }
            lease.Dispose();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        succeeded.Should().BeTrue();
        allocated.Should().Be(0);
    }

    [Fact]
    public void ReservationBytes_ShouldAcceptTheExactAggregateAndRejectOneByteBelow()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long exactAggregate = checked(fixture.Far.RetainedBytes * 2);
        using var exact = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: exactAggregate,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        exact.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation first)
            .Should().BeTrue();
        exact.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation second)
            .Should().BeTrue();
        exact.ReservedPayloadBytes.Should().Be(exactAggregate);
        exact.ReleasePayloadReservation(ref second);
        exact.ReleasePayloadReservation(ref first);

        using var oneByteShort = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: exactAggregate - 1,
            maxActiveLeases: 2,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        oneByteShort.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation accepted)
            .Should().BeTrue();
        oneByteShort.TryReservePayload(fixture.Far.RetainedBytes, out _)
            .Should().BeFalse();
        oneByteShort.ReservedPayloadBytes.Should().Be(fixture.Far.RetainedBytes);
        oneByteShort.ReleasePayloadReservation(ref accepted);
    }

    [Fact]
    public void PublicationLargerThanItsReservation_ShouldFailWithoutConsumingTheSlot()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        cache.TryReservePayload(
                fixture.Near.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out _)
            .Should().Be(NavigationFlowFieldStatus.CapacityExceeded);

        cache.Count.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReservedPayloadBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.ReleasePayloadReservation(ref reservation);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void CompleteStrictPrefix_ShouldFailTheCanonicalPrefixInvariant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        var malformedCompletePrefix = new NavigationFlowFieldPayload(
            fixture.Near.Key,
            (NavigationFlowFieldNode[])fixture.Near.Nodes.Clone(),
            (int[])fixture.Near.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Near.TransitionInstructions.Clone(),
            fixture.Near.Dependencies,
            isComplete: true,
            fixture.Near.WorldChangeSequence);
        Publish(cache, fixture, malformedCompletePrefix, fixture.NearOrigin).Dispose();
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.Count.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void StrictPrefixPromotion_ShouldPreserveAnEarlierCanonicalPageDependency()
    {
        using var world = new GridWorld();
        const int CellCount = 129;
        var cells = new VoxelIndex[CellCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(CellCount),
                cells,
                "multi-page-prefix");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 4);
        VoxelIndex destinationIndex = cells[^1];
        VoxelIndex nearIndex = cells[^2];
        VoxelIndex farIndex = cells[0];
        NavigationWorkBudget budget = new(
            32_768, 32, 256, 2_048, 2_048, 0, 0, 0, 0, 0, 0);

        PathQuery CreateFlowQuery(VoxelIndex origin)
        {
            PathQuery query = fixture.CreateQuery(
                origin,
                destinationIndex,
                fixture.DefaultProfile);
            return NavigationFlowFieldCacheTestHarness.ToFlowField(
                new PathQuery(
                    query.Start,
                    query.End,
                    query.Agent,
                    query.AreaPolicy,
                    query.Traversal,
                    query.Algorithm,
                    budget,
                    query.AllowTransitions),
                Fixed64.One);
        }

        var destination = new NavigationCellAddress(fixture.MapId, destinationIndex);
        var nearOrigin = new NavigationCellAddress(fixture.MapId, nearIndex);
        var farOrigin = new NavigationCellAddress(fixture.MapId, farIndex);
        NavigationFlowFieldPayload near = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            CreateFlowQuery(nearIndex),
            nearOrigin,
            destination,
            NavigationFlowFieldStatus.Success,
            nodeCapacity: 256);
        NavigationFlowFieldPayload far = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            fixture.Graph,
            CreateFlowQuery(farIndex),
            farOrigin,
            destination,
            NavigationFlowFieldStatus.Success,
            nodeCapacity: 256);
        near.Key.Should().Be(far.Key);
        near.Nodes.Length.Should().BeLessThan(far.Nodes.Length);
        far.Dependencies.Pages[0].PageIndex.Should().BeLessThan(
            near.Dependencies.Pages[0].PageIndex,
            "the longer proof owns one earlier sorted page before the shorter proof pages");
        foreach (GraphPageDependency dependency in near.Dependencies.Pages)
        {
            Array.Exists(far.Dependencies.Pages, candidate => candidate.Equals(dependency))
                .Should().BeTrue();
        }
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: far.RetainedBytes,
            maxSinglePayloadBytes: far.RetainedBytes,
            maxActivePayloadBytes: far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: fixture.Graph.MapCount,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                near.RetainedBytes,
                out NavigationFlowFieldReservation nearReservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                store,
                near,
                nearOrigin,
                ref nearReservation,
                out NavigationFlowFieldPayloadLease nearLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        nearLease.Dispose();
        cache.TryReservePayload(
                far.RetainedBytes,
                out NavigationFlowFieldReservation farReservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                store,
                far,
                farOrigin,
                ref farReservation,
                out NavigationFlowFieldPayloadLease promoted)
            .Should().Be(NavigationFlowFieldStatus.Success);

        promoted.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(far);
        cache.Count.Should().Be(1);
        promoted.Dispose();
    }

    [Fact]
    public void StrictPrefixPromotion_ShouldPreserveAnEarlierCanonicalComponentDependency()
    {
        using var world = new GridWorld();
        NavigationAStarExitTestHarness.GraphFixture earlierFixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
                    new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                    storageKind: GridStorageKind.Sparse),
                new[] { default(VoxelIndex) },
                "a-earlier-component");
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
            new(4, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture routeFixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "z-component-prefix");
        var baseGraph = new NavigationWorldGraph(
            1,
            new[]
            {
                earlierFixture.Graph.GetInstance(0),
                routeFixture.Graph.GetInstance(0)
            },
            areaCatalog: routeFixture.Graph.AreaCatalog);
        NavigationWorldGraph graph = baseGraph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(baseGraph));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 4);
        PathQuery nearQuery = NavigationFlowFieldCacheTestHarness.ToFlowField(
            routeFixture.CreateQuery(cells[1], cells[0], routeFixture.DefaultProfile),
            Fixed64.One);
        PathQuery farQuery = NavigationFlowFieldCacheTestHarness.ToFlowField(
            routeFixture.CreateQuery(cells[3], cells[0], routeFixture.DefaultProfile),
            Fixed64.One);
        var destination = new NavigationCellAddress(routeFixture.MapId, cells[0]);
        var nearOrigin = new NavigationCellAddress(routeFixture.MapId, cells[1]);
        var farOrigin = new NavigationCellAddress(routeFixture.MapId, cells[3]);
        NavigationFlowFieldPayload near = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            graph,
            nearQuery,
            nearOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        NavigationFlowFieldPayload far = NavigationFlowFieldCacheTestHarness.RunFlow(
            world,
            store,
            graph,
            farQuery,
            farOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress(earlierFixture.MapId, default),
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey earlierKey,
                out _)
            .Should().BeTrue();
        graph.TryGetComponentDependency(earlierKey, out GraphComponentDependency earlier)
            .Should().BeTrue();
        graph.TryGetPageDependency(
                new GraphPageDependencyAddress(earlierFixture.MapId, pageIndex: 0),
                out GraphPageDependency earlierPage)
            .Should().BeTrue();
        earlier.Key.CompareTo(far.Dependencies.Components[0].Key).Should().BeNegative();
        string.CompareOrdinal(earlierPage.MapId, far.Dependencies.Pages[0].MapId)
            .Should().BeNegative();
        var components = new GraphComponentDependency[
            far.Dependencies.Components.Length + 1];
        components[0] = earlier;
        Array.Copy(
            far.Dependencies.Components,
            0,
            components,
            1,
            far.Dependencies.Components.Length);
        var pages = new GraphPageDependency[far.Dependencies.Pages.Length + 1];
        pages[0] = earlierPage;
        Array.Copy(
            far.Dependencies.Pages,
            0,
            pages,
            1,
            far.Dependencies.Pages.Length);
        var promotedDependencies = new GraphDependencyStamp(
            far.Dependencies.AreaPolicy,
            components,
            pages,
            far.Dependencies.HasTransitionRuleDependency,
            far.Dependencies.TransitionRuleVersion);
        var longer = new NavigationFlowFieldPayload(
            far.Key,
            (NavigationFlowFieldNode[])far.Nodes.Clone(),
            (int[])far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])far.TransitionInstructions.Clone(),
            promotedDependencies,
            far.IsComplete,
            far.WorldChangeSequence);
        graph.IsDependencyCurrent(longer.Dependencies).Should().BeTrue();
        using var cache = new NavigationFlowFieldPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: longer.RetainedBytes,
            maxSinglePayloadBytes: longer.RetainedBytes,
            maxActivePayloadBytes: longer.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: graph.MapCount,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                near.RetainedBytes,
                out NavigationFlowFieldReservation nearReservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                store,
                near,
                nearOrigin,
                ref nearReservation,
                out NavigationFlowFieldPayloadLease nearLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        nearLease.Dispose();
        cache.TryReservePayload(
                longer.RetainedBytes,
                out NavigationFlowFieldReservation farReservation)
            .Should().BeTrue();

        cache.TryPublishOrPromote(
                store,
                longer,
                farOrigin,
                ref farReservation,
                out NavigationFlowFieldPayloadLease promoted)
            .Should().Be(NavigationFlowFieldStatus.Success);

        promoted.TryGetPayload(out NavigationFlowFieldPayload canonical)
            .Should().Be(NavigationFlowFieldStatus.Success);
        canonical.Should().BeSameAs(longer);
        cache.Count.Should().Be(1);
        promoted.Dispose();
    }

    [Fact]
    public void EqualNodePrefixWithConflictingCompletion_ShouldFailTheInvariant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        var conflicting = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone(),
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            fixture.Far.Dependencies,
            !fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        cache.TryReservePayload(
                conflicting.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                conflicting,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.Count.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongerPrefixMissingShorterDependencies_ShouldFailTheInvariant(
        bool omitPages)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Near, fixture.NearOrigin).Dispose();
        var missingDependencies = new GraphDependencyStamp(
            fixture.Far.Dependencies.AreaPolicy,
            omitPages
                ? fixture.Far.Dependencies.Components
                : Array.Empty<GraphComponentDependency>(),
            omitPages
                ? Array.Empty<GraphPageDependency>()
                : fixture.Far.Dependencies.Pages);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone(),
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            (NavigationTransitionInstruction[])fixture.Far.TransitionInstructions.Clone(),
            missingDependencies,
            fixture.Far.IsComplete,
            fixture.Far.WorldChangeSequence);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        FluentActions.Invoking(() => cache.TryPublishOrPromote(
                fixture.Store,
                malformed,
                fixture.FarOrigin,
                ref reservation,
                out _))
            .Should().Throw<InvalidOperationException>();

        cache.Count.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
    }

    [Fact]
    public void ReleasingThroughTheWrongCache_ShouldNotDestroyReservationOwnership()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var owner = CreateCache(fixture);
        using var other = CreateCache(fixture);
        owner.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        other.ReleasePayloadReservation(ref reservation);

        reservation.Owner.Should().BeSameAs(owner);
        owner.ReservedLeaseCount.Should().Be(1);
        owner.ReleasePayloadReservation(ref reservation);
        owner.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void PayloadAndCacheCounters_ShouldUseExactLogicalRetainedBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        fixture.Near.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Near.Nodes.Length,
                fixture.Near.TransitionInstructions.Length,
                fixture.Near.Dependencies.Components.Length,
                fixture.Near.Dependencies.Pages.Length));
        fixture.Far.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Far.Nodes.Length,
                fixture.Far.TransitionInstructions.Length,
                fixture.Far.Dependencies.Components.Length,
                fixture.Far.Dependencies.Pages.Length));
        fixture.Complete.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Complete.Nodes.Length,
                fixture.Complete.TransitionInstructions.Length,
                fixture.Complete.Dependencies.Components.Length,
                fixture.Complete.Dependencies.Pages.Length));
        using var cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease lease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        cache.CachedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.LeasedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.DetachedBytes.Should().Be(0);
        lease.Dispose();
        cache.CachedBytes.Should().Be(fixture.Near.RetainedBytes);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void WarmReservationAndRelease_ShouldAllocateZeroBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture);
        for (int i = 0; i < 8; i++)
        {
            cache.TryReservePayload(
                    fixture.Far.RetainedBytes,
                    out NavigationFlowFieldReservation warm)
                .Should().BeTrue();
            cache.ReleasePayloadReservation(ref warm);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool succeeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (!cache.TryReservePayload(
                    fixture.Far.RetainedBytes,
                    out NavigationFlowFieldReservation reservation))
            {
                succeeded = false;
                break;
            }
            cache.ReleasePayloadReservation(ref reservation);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        succeeded.Should().BeTrue();
        allocated.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void SamePayloadCheckout_ShouldNotReserveDuplicateUniqueBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long twoPayloadBytes = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            fixture.World,
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: twoPayloadBytes,
            maxActiveLeases: 3,
            guideMapCapacity: 0,
            immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        NavigationFlowFieldPayloadLease first = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation unrelatedWorker)
            .Should().BeTrue();

        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease shared,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success,
                "a second lease of the same immutable field adds no unique payload bytes");

        cache.LeasedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.ReservedPayloadBytes.Should().Be(fixture.Far.RetainedBytes);
        shared.Dispose();
        cache.ReleasePayloadReservation(ref unrelatedWorker);
        first.Dispose();
        cache.LeasedBytes.Should().Be(0);
    }

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture) => new(
        fixture.World,
        maxEntries: 2,
        maxReusableBytes: checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes),
        maxSinglePayloadBytes: fixture.Far.RetainedBytes,
        maxActivePayloadBytes: checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes),
        maxActiveLeases: 4,
        guideMapCapacity: 0,
        immediateRayWorkspace: NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress origin)
    {
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                payload,
                origin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        reservation.Should().Be(default(NavigationFlowFieldReservation));
        return lease;
    }
}

internal static class NavigationFlowFieldCacheTestHarness
{
    internal static NavigationImmediateRayWorkspace CreateImmediateRayWorkspace() =>
        new(8, 64, 64, 128, 128);

    internal sealed class LineFixture : IDisposable
    {
        internal LineFixture(
            GridWorld world,
            NavigationWorldGraphStore store,
            NavigationWorldGraph graph,
            NavigationFlowFieldPayload near,
            NavigationFlowFieldPayload far,
            NavigationFlowFieldPayload complete,
            PathQuery farQuery,
            NavigationCellAddress nearOrigin,
            NavigationCellAddress marginOrigin,
            NavigationCellAddress farOrigin)
        {
            World = world;
            Store = store;
            Graph = graph;
            Near = near;
            Far = far;
            Complete = complete;
            FarQuery = farQuery;
            NearOrigin = nearOrigin;
            MarginOrigin = marginOrigin;
            FarOrigin = farOrigin;
        }

        internal GridWorld World { get; }
        internal NavigationWorldGraphStore Store { get; }
        internal NavigationWorldGraph Graph { get; }
        internal NavigationFlowFieldPayload Near { get; }
        internal NavigationFlowFieldPayload Far { get; }
        internal NavigationFlowFieldPayload Complete { get; }
        internal PathQuery FarQuery { get; }
        internal NavigationCellAddress NearOrigin { get; }
        internal NavigationCellAddress MarginOrigin { get; }
        internal NavigationCellAddress FarOrigin { get; }

        public void Dispose()
        {
            Store.Dispose();
            World.Dispose();
        }
    }

    internal static LineFixture CreateLine(Fixed64 extraIntegrationCost)
    {
        var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
            new(4, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture graphFixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "flow-cache");
        NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graphFixture.Graph, 8);
        PathQuery nearQuery = ToFlowField(
            graphFixture.CreateQuery(cells[1], cells[0], graphFixture.DefaultProfile),
            extraIntegrationCost);
        PathQuery farQuery = ToFlowField(
            graphFixture.CreateQuery(cells[3], cells[0], graphFixture.DefaultProfile),
            extraIntegrationCost);
        PathQuery completeQuery = ToFlowField(
            graphFixture.CreateQuery(cells[4], cells[0], graphFixture.DefaultProfile),
            extraIntegrationCost);
        NavigationCellAddress destination = new(graphFixture.MapId, cells[0]);
        NavigationCellAddress nearOrigin = new(graphFixture.MapId, cells[1]);
        NavigationCellAddress marginOrigin = new(graphFixture.MapId, cells[2]);
        NavigationCellAddress farOrigin = new(graphFixture.MapId, cells[3]);
        NavigationCellAddress completeOrigin = new(graphFixture.MapId, cells[4]);
        NavigationFlowFieldPayload near = RunFlow(
            world,
            store,
            graphFixture.Graph,
            nearQuery,
            nearOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        NavigationFlowFieldPayload far = RunFlow(
            world,
            store,
            graphFixture.Graph,
            farQuery,
            farOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        NavigationFlowFieldPayload complete = RunFlow(
            world,
            store,
            graphFixture.Graph,
            completeQuery,
            completeOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        near.Key.Should().Be(far.Key);
        near.Nodes.Length.Should().BeLessThan(far.Nodes.Length);
        return new LineFixture(
            world,
            store,
            graphFixture.Graph,
            near,
            far,
            complete,
            farQuery,
            nearOrigin,
            marginOrigin,
            farOrigin);
    }

    internal static NavigationFlowFieldPayload Clone(
        NavigationFlowFieldPayload payload,
        NavigationFlowFieldPayloadKey key) => new(
        key,
        (NavigationFlowFieldNode[])payload.Nodes.Clone(),
        (int[])payload.AddressLookupOrdinals.Clone(),
        (NavigationTransitionInstruction[])payload.TransitionInstructions.Clone(),
        payload.Dependencies,
        payload.IsComplete,
        payload.WorldChangeSequence);

    internal static NavigationFlowFieldPayload CloneWithExpandedNodeBudget(
        NavigationFlowFieldPayload payload,
        PathQuery query,
        int expansion) => Clone(
        payload,
        new NavigationFlowFieldPayloadKey(
            WithExpandedNodeBudget(query, expansion),
            payload.Key.DestinationAddress,
            payload.Key.StartMedium,
            payload.Key.TargetMedia));

    internal static NavigationFlowFieldPayload RunFlow(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress origin,
        NavigationCellAddress destination,
        NavigationFlowFieldStatus expectedStatus,
        int nodeCapacity = 128)
    {
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        graph.TryGetNodeRef(origin, out NavigationNodeRef originNode).Should().BeTrue();
        graph.TryGetNodeRef(destination, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            query,
            new NavigationResolvedEndpoint(
                originNode,
                origin,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                destinationNode,
                destination,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Solid,
            new NavigationWorkMeter(query.Budget),
            world.ChangeSequence,
            requiresWorldStamp: false);
        var workspace = new NavigationFlowFieldWorkspace(
            0,
            128,
            128,
            nodeCapacity,
            128,
            128);
        using var work = new NavigationFlowFieldWork(world, resolved, workspace);
        for (int step = 0;
            step < 4_096 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(128, 128, 128, 128);
        }
        work.Status.Should().Be(expectedStatus);
        work.Result.Should().NotBeNull();
        return work.Result!;
    }

    internal static PathQuery ToFlowField(
        PathQuery query,
        Fixed64 extraIntegrationCost) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        PathAlgorithm.FlowField,
        query.Budget,
        query.AllowTransitions,
        new FlowFieldQueryOptions(extraIntegrationCost));

    private static PathQuery WithExpandedNodeBudget(PathQuery query, int expansion) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        query.Algorithm,
        new NavigationWorkBudget(
            query.Budget.MaxLookupProbes,
            query.Budget.MaxEndpointCandidates,
            checked(query.Budget.MaxExpandedNodes + expansion),
            query.Budget.MaxEvaluatedEdges,
            query.Budget.MaxConnectionLegs,
            query.Budget.MaxTransitionCandidates,
            query.Budget.MaxTransitionPairs,
            query.Budget.MaxStagedLegAttempts,
            query.Budget.MaxTraceIntervals,
            query.Budget.MaxCoveredVoxelIntervals,
            query.Budget.MaxSimplificationRays),
        query.AllowTransitions,
        query.FlowField);
}
