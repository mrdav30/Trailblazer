//=======================================================================
// NavigationFlowFieldCacheTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationFlowFieldCacheTests
{
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
            maxEntries: 1,
            maxReusableBytes: newerPayload.RetainedBytes,
            maxSinglePayloadBytes: newerPayload.RetainedBytes,
            maxActivePayloadBytes: newerPayload.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
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
                out NavigationFlowFieldPayloadLease rejected)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        rejected.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(1,
            "an older in-flight query cannot evict a payload valid for the current graph");

        cache.TryCheckout(
                fixture.Store,
                newer,
                newerPayload.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease current)
            .Should().Be(NavigationFlowFieldStatus.Success);
        current.TryGetPayload(out NavigationFlowFieldPayload retained)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retained.Should().BeSameAs(newerPayload);
        current.Dispose();
    }

    [Fact]
    public void NearThenFar_ShouldPromoteAndDetachTheActiveSmallerPrefix()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long activeBytes = checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 4,
            guideMapCapacity: 0);

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
    public void RemoveExact_ShouldInvalidateOnlyTheExactPayloadAcrossPromotion()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long activeBytes = checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 4,
            guideMapCapacity: 0);
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

        fixture.Near.TryGetNode(fixture.MarginOrigin, out _).Should().BeTrue(
            "the cache must check the requested extra-cost margin, not just node presence");
        cache.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Near.Key,
                fixture.MarginOrigin,
                out NavigationFlowFieldPayloadLease checkout)
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
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
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
                out NavigationFlowFieldPayloadLease rebound)
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
                    out NavigationFlowFieldPayloadLease lease)
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
            maxEntries: 1,
            maxReusableBytes: fixture.Complete.RetainedBytes,
            maxSinglePayloadBytes: fixture.Complete.RetainedBytes,
            maxActivePayloadBytes: fixture.Complete.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
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
                out NavigationFlowFieldPayloadLease checkout)
            .Should().Be(NavigationFlowFieldStatus.NoPath);
        checkout.Should().Be(default(NavigationFlowFieldPayloadLease));
    }

    [Fact]
    public void DuplicatePublication_ShouldReturnTheIncumbentWithoutDoubleAccounting()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long activeBytes = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 0);
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
            Fixed64.One,
            first.SelectedEdge);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            malformedNodes,
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete);
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
            useLowerCost ? Fixed64.Zero : fixture.Near.LastSettledCost,
            firstExtra.SelectedEdge);
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            malformedNodes,
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            fixture.Far.Dependencies,
            fixture.Far.IsComplete);
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
    public void NonReusablePayload_ShouldRemainDetachedOnlyUntilReturn()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);

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
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes - 1,
            maxActivePayloadBytes: fixture.Far.RetainedBytes - 1,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
        oneByteShort.TryReservePayload(fixture.Far.RetainedBytes, out _)
            .Should().BeFalse();

        using var leaseCapped = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
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
            maxEntries: 2,
            maxReusableBytes: twoPayloadBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: fixture.Far.RetainedBytes,
            maxActiveLeases: 1,
            guideMapCapacity: 0);
        Publish(lru, fixture, fixture.Far, fixture.FarOrigin).Dispose();
        Publish(lru, fixture, second, fixture.FarOrigin).Dispose();
        lru.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease touched)
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
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
        lru.TryCheckout(
                fixture.Store,
                fixture.Store.Current,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease retained)
            .Should().Be(NavigationFlowFieldStatus.Success);
        retained.Dispose();
    }

    [Fact]
    public void Reset_ShouldInvalidateActiveHandlesCancelReservationsAndReleaseReferences()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        long activeBytes = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: activeBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 0);
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
                    out NavigationFlowFieldPayloadLease warm)
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
                    out NavigationFlowFieldPayloadLease lease)
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
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: exactAggregate,
            maxActiveLeases: 2,
            guideMapCapacity: 0);
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
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: exactAggregate - 1,
            maxActiveLeases: 2,
            guideMapCapacity: 0);
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
            fixture.Near.Dependencies,
            isComplete: true);
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
    public void LongerPrefixMissingShorterDependencies_ShouldFailTheInvariant()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        using var cache = CreateCache(fixture);
        Publish(cache, fixture, fixture.Near, fixture.NearOrigin).Dispose();
        var missingDependencies = new GraphDependencyStamp(
            fixture.Far.Dependencies.AreaPolicy,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());
        var malformed = new NavigationFlowFieldPayload(
            fixture.Far.Key,
            (NavigationFlowFieldNode[])fixture.Far.Nodes.Clone(),
            (int[])fixture.Far.AddressLookupOrdinals.Clone(),
            missingDependencies,
            fixture.Far.IsComplete);
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
    public void Reservation_ShouldExposeOnlyReadonlyStructState()
    {
        Type type = typeof(NavigationFlowFieldReservation);
        type.IsValueType.Should().BeTrue();
        type.IsDefined(
                typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute),
                inherit: false)
            .Should().BeTrue();
        System.Reflection.FieldInfo[] fields = type.GetFields(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        fields.Should().HaveCount(3);
        for (int i = 0; i < fields.Length; i++)
            fields[i].IsInitOnly.Should().BeTrue();
    }

    [Fact]
    public void PayloadAndCacheCounters_ShouldUseExactLogicalRetainedBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        fixture.Near.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Near.Nodes.Length,
                fixture.Near.Dependencies.Components.Length,
                fixture.Near.Dependencies.Pages.Length));
        fixture.Far.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Far.Nodes.Length,
                fixture.Far.Dependencies.Components.Length,
                fixture.Far.Dependencies.Pages.Length));
        fixture.Complete.RetainedBytes.Should().Be(
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                fixture.Complete.Nodes.Length,
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
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: twoPayloadBytes,
            maxActiveLeases: 3,
            guideMapCapacity: 0);
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
                out NavigationFlowFieldPayloadLease shared)
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
        maxEntries: 2,
        maxReusableBytes: checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes),
        maxSinglePayloadBytes: fixture.Far.RetainedBytes,
        maxActivePayloadBytes: checked(fixture.Near.RetainedBytes + fixture.Far.RetainedBytes),
        maxActiveLeases: 4,
        guideMapCapacity: 0);

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
            store,
            graphFixture.Graph,
            nearQuery,
            nearOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        NavigationFlowFieldPayload far = RunFlow(
            store,
            graphFixture.Graph,
            farQuery,
            farOrigin,
            destination,
            NavigationFlowFieldStatus.Success);
        NavigationFlowFieldPayload complete = RunFlow(
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
        payload.Dependencies,
        payload.IsComplete);

    internal static NavigationFlowFieldPayload CloneWithExpandedNodeBudget(
        NavigationFlowFieldPayload payload,
        PathQuery query,
        int expansion) => Clone(
        payload,
        new NavigationFlowFieldPayloadKey(
            WithExpandedNodeBudget(query, expansion),
            payload.Key.DestinationAddress));

    internal static NavigationFlowFieldPayload RunFlow(
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress origin,
        NavigationCellAddress destination,
        NavigationFlowFieldStatus expectedStatus)
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
            new NavigationResolvedEndpoint(originNode, origin, Fixed64.Zero),
            new NavigationResolvedEndpoint(destinationNode, destination, Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            new NavigationWorkMeter(query.Budget));
        var workspace = new NavigationFlowFieldWorkspace(0, 128, 128, 128, 128, 128);
        using var work = new NavigationFlowFieldWork(resolved, workspace);
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
