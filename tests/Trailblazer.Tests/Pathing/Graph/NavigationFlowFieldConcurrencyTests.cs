//=======================================================================
// NavigationFlowFieldConcurrencyTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Reflection;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationFlowFieldConcurrencyTests
{
    [Fact]
    public void SimultaneousNearAndFarPublication_ShouldLeaveOneCanonicalFarPrefix()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long twoMaximumPayloads = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: twoMaximumPayloads,
            maxActiveLeases: 2);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation nearReservation)
            .Should().BeTrue();
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation farReservation)
            .Should().BeTrue();
        using var start = new ManualResetEventSlim();
        NavigationFlowFieldStatus nearStatus = default;
        NavigationFlowFieldStatus farStatus = default;
        NavigationFlowFieldPayloadLease nearLease = default;
        NavigationFlowFieldPayloadLease farLease = default;
        Exception? nearError = null;
        Exception? farError = null;
        var nearThread = new Thread(() =>
        {
            try
            {
                start.Wait();
                nearStatus = cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Near,
                    fixture.NearOrigin,
                    ref nearReservation,
                    out nearLease);
            }
            catch (Exception error)
            {
                nearError = error;
            }
        }) { IsBackground = true };
        var farThread = new Thread(() =>
        {
            try
            {
                start.Wait();
                farStatus = cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref farReservation,
                    out farLease);
            }
            catch (Exception error)
            {
                farError = error;
            }
        }) { IsBackground = true };

        nearThread.Start();
        farThread.Start();
        start.Set();
        nearThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        farThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        nearError.Should().BeNull();
        farError.Should().BeNull();
        nearStatus.Should().Be(NavigationFlowFieldStatus.Success);
        farStatus.Should().Be(NavigationFlowFieldStatus.Success);
        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        farLease.TryGetPayload(out NavigationFlowFieldPayload farPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        farPayload.Should().BeSameAs(fixture.Far);
        nearLease.Dispose();
        farLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedNearAndFarWorkers_ShouldConvergeOnTheLongerPrefix(
        bool publishFarFirst)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.One);
        long twoMaximumPayloads = checked(fixture.Far.RetainedBytes * 2);
        using var cache = new NavigationFlowFieldPayloadCache(
            maxEntries: 1,
            maxReusableBytes: fixture.Far.RetainedBytes,
            maxSinglePayloadBytes: fixture.Far.RetainedBytes,
            maxActivePayloadBytes: twoMaximumPayloads,
            maxActiveLeases: 2);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation nearReservation)
            .Should().BeTrue();
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation farReservation)
            .Should().BeTrue();
        NavigationFlowFieldPayloadLease nearLease = default;
        NavigationFlowFieldPayloadLease farLease = default;

        if (publishFarFirst)
        {
            PublishFar();
            PublishNear();
        }
        else
        {
            PublishNear();
            PublishFar();
        }

        cache.Count.Should().Be(1);
        cache.CachedBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.TryCheckout(
                fixture.Store,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease canonicalLease)
            .Should().Be(NavigationFlowFieldStatus.CapacityExceeded,
                "both reserved slots were atomically transferred to active leases");
        farLease.TryGetPayload(out NavigationFlowFieldPayload farPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        farPayload.Should().BeSameAs(fixture.Far);
        nearLease.TryGetPayload(out NavigationFlowFieldPayload nearPayload)
            .Should().Be(NavigationFlowFieldStatus.Success);
        nearPayload.Should().BeSameAs(
            publishFarFirst ? fixture.Far : fixture.Near);
        cache.DetachedBytes.Should().Be(
            publishFarFirst ? 0 : fixture.Near.RetainedBytes);
        canonicalLease.Dispose();
        nearLease.Dispose();
        farLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);

        void PublishNear()
        {
            cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Near,
                    fixture.NearOrigin,
                    ref nearReservation,
                    out nearLease)
                .Should().Be(NavigationFlowFieldStatus.Success);
        }

        void PublishFar()
        {
            cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref farReservation,
                    out farLease)
                .Should().Be(NavigationFlowFieldStatus.Success);
        }
    }

    [Fact]
    public void DependencyMutationBeforePublication_ShouldReturnStaleWithoutLeaks()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture, maxActiveLeases: 1);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        PublishChangedGraph(fixture);

        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Stale);

        lease.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void DependencyMutationWhilePublisherWaitsForCacheLock_ShouldFailClosed()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture, maxActiveLeases: 1);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        object cacheSync = typeof(NavigationFlowFieldPayloadCache)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        using var publisherStarted = new ManualResetEventSlim();
        NavigationFlowFieldStatus status = default;
        NavigationFlowFieldPayloadLease publishedLease = default;
        Exception? error = null;
        var publisher = new Thread(() =>
        {
            publisherStarted.Set();
            try
            {
                status = cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref reservation,
                    out publishedLease);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        })
        {
            IsBackground = true
        };

        Monitor.Enter(cacheSync);
        try
        {
            publisher.Start();
            publisherStarted.Wait(5_000, TestContext.Current.CancellationToken)
                .Should().BeTrue();
            SpinWait.SpinUntil(
                    () => (publisher.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue();
            PublishChangedGraph(fixture);
        }
        finally
        {
            Monitor.Exit(cacheSync);
            publisher.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        error.Should().BeNull();
        status.Should().Be(NavigationFlowFieldStatus.Stale);
        publishedLease.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref reservation);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void DependencyMutationAfterPublication_ShouldInvalidateTheActivePayloadOnNextUse()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture, maxActiveLeases: 2);
        NavigationFlowFieldPayloadLease active = Publish(cache, fixture);
        PublishChangedGraph(fixture);

        cache.TryCheckout(
                fixture.Store,
                fixture.Far.Key,
                fixture.FarOrigin,
                out NavigationFlowFieldPayloadLease checkout)
            .Should().Be(NavigationFlowFieldStatus.Stale);

        checkout.Should().Be(default(NavigationFlowFieldPayloadLease));
        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(fixture.Far.RetainedBytes);
        active.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        active.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void ResetAndSlotReuse_ShouldRejectAStaleReservationAlias()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture, maxActiveLeases: 1);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation first)
            .Should().BeTrue();
        NavigationFlowFieldReservation staleAlias = first;

        cache.Reset();
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation rebound)
            .Should().BeTrue();
        cache.ReleasePayloadReservation(ref staleAlias);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReservedPayloadBytes.Should().Be(fixture.Far.RetainedBytes);
        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref first,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Stale);
        cache.ReservedLeaseCount.Should().Be(1);
        cache.ReleasePayloadReservation(ref rebound);
        cache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Dispose_ShouldInvalidateActiveAndReservedStateWithoutLeakingReturns()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        var cache = CreateCache(fixture, maxActiveLeases: 2);
        NavigationFlowFieldPayloadLease active = Publish(cache, fixture);
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();

        cache.Dispose();

        cache.Count.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        active.TryGetPayload(out _).Should().Be(NavigationFlowFieldStatus.Stale);
        cache.TryReservePayload(fixture.Far.RetainedBytes, out _).Should().BeFalse();
        cache.TryCheckout(
                fixture.Store,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Stale);
        cache.ReleasePayloadReservation(ref reservation);
        active.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        cache.Dispose();
    }

    [Fact]
    public void ExhaustedLeaseSlotGeneration_ShouldRetireWithoutWrapping()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(extraIntegrationCost: Fixed64.Zero);
        using var cache = CreateCache(fixture, maxActiveLeases: 1);
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Array slots = (Array)typeof(NavigationFlowFieldPayloadCache)
            .GetField("_leaseSlots", PrivateInstance)!
            .GetValue(cache)!;
        object slot = slots.GetValue(0)!;
        slot.GetType().GetField("Generation", PrivateInstance)!
            .SetValue(slot, ulong.MaxValue);
        slots.SetValue(slot, 0);

        cache.TryReservePayload(fixture.Far.RetainedBytes, out _).Should().BeFalse();
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.TryCheckout(
                fixture.Store,
                fixture.Far.Key,
                fixture.FarOrigin,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Pending);
    }

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        int maxActiveLeases) => new(
        maxEntries: 1,
        maxReusableBytes: fixture.Far.RetainedBytes,
        maxSinglePayloadBytes: fixture.Far.RetainedBytes,
        maxActivePayloadBytes: checked(fixture.Far.RetainedBytes * maxActiveLeases),
        maxActiveLeases);

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        return lease;
    }

    private static void PublishChangedGraph(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        NavigationWorldGraph changed = fixture.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(fixture.Store.Current.GraphVersion + 1);
        fixture.Store.TryPublish(changed)
            .Should().Be(NavigationCandidatePublication.Published);
    }
}
