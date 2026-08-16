using System;
using System.Reflection;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationFlowFieldSamplingConcurrencyTests
{
    [Fact]
    public void CacheReset_ShouldMakeGuideStickyStaleAndReleaseExactlyOnce()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldLease guide = CreateGuide(cache, fixture);
        NavigationFlowFieldLease copied = guide;

        cache.Reset();

        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        guide.TrySample(
                Vector3d.Zero,
                GenerousBudget,
                out Vector3d heading)
            .Should().Be(NavigationGuideStatus.Stale);
        heading.Should().Be(Vector3d.Zero);
        guide.Dispose();
        copied.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void PooledRebind_ShouldNotReviveDisposedAlias()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldLease first = CreateGuide(cache, fixture);
        NavigationFlowFieldLease staleAlias = first;
        first.Dispose();

        NavigationFlowFieldLease second = CreateGuide(cache, fixture);

        staleAlias.Status.Should().Be(NavigationGuideStatus.Stale);
        second.Status.Should().Be(NavigationGuideStatus.Success);
        staleAlias.Dispose();
        cache.ActiveLeaseCount.Should().Be(1);
        second.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void DependencyPublicationAfterGenerationValidation_ShouldFailStickyStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldLease guide = CreateGuide(cache, fixture);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        object cacheSync = typeof(NavigationFlowFieldPayloadCache)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        using var sampleStarted = new ManualResetEventSlim();
        NavigationGuideStatus sampleStatus = default;
        Vector3d heading = default;
        Exception? sampleError = null;
        var sampleThread = new Thread(() =>
        {
            sampleStarted.Set();
            try
            {
                sampleStatus = guide.TrySample(
                    source.FootAnchor,
                    GenerousBudget,
                    out heading);
            }
            catch (Exception error)
            {
                sampleError = error;
            }
        })
        {
            IsBackground = true
        };

        Monitor.Enter(cacheSync);
        try
        {
            sampleThread.Start();
            sampleStarted.Wait(5_000, TestContext.Current.CancellationToken)
                .Should().BeTrue();
            SpinWait.SpinUntil(
                    () => (sampleThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue(
                    "sampling must hold the guide generation while waiting at payload validation");
            NavigationWorldGraph changed = fixture.Graph.WithSurfaceComponents(
                NavigationSurfaceComponentIndex.Empty).WithGraphVersion(
                fixture.Graph.GraphVersion + 1);
            fixture.Store.TryPublish(changed)
                .Should().Be(NavigationCandidatePublication.Published);
        }
        finally
        {
            Monitor.Exit(cacheSync);
        }
        sampleThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        sampleError.Should().BeNull();
        sampleStatus.Should().Be(NavigationGuideStatus.Stale);
        heading.Should().Be(Vector3d.Zero);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void WarmAttachSampleDispose_ShouldAllocateZeroBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        fixture.Graph.TryGetNodeRef(
                fixture.FarOrigin,
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();
        for (int i = 0; i < 16; i++)
        {
            NavigationFlowFieldLease warm = CreateGuide(cache, fixture);
            warm.TrySample(source.FootAnchor, GenerousBudget, out _)
                .Should().Be(NavigationGuideStatus.Success);
            warm.Dispose();
        }

        bool succeeded = true;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            NavigationFlowFieldLease guide = CreateGuideUnchecked(
                cache,
                fixture,
                ref succeeded);
            if (!succeeded)
                break;
            if (guide.TrySample(
                    source.FootAnchor,
                    GenerousBudget,
                    out _) != NavigationGuideStatus.Success)
            {
                succeeded = false;
            }
            guide.Dispose();
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        succeeded.Should().BeTrue();
        allocated.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void FirstAttachAfterCacheConstruction_ShouldAllocateZeroBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using (NavigationFlowFieldPayloadCache warmCache = CreateCache(fixture))
        {
            NavigationFlowFieldLease warm = CreateGuide(warmCache, fixture);
            warm.Dispose();
        }
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = Publish(cache, fixture);

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        NavigationGuideStatus status = cache.TryCreateGuide(
            fixture.World,
            fixture.Store,
            new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
            out NavigationFlowFieldLease guide);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationGuideStatus.Success);
        allocated.Should().Be(0);
        guide.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void WarmExactNodeRebaseSample_ShouldAllocateZeroBytes()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture);
        fixture.Graph.TryGetNodeRef(
                fixture.Far.Key.DestinationAddress,
                out NavigationNodeRef destinationRef)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(destinationRef, out NavigationNodeState destination)
            .Should().BeTrue();
        for (int i = 0; i < 16; i++)
        {
            NavigationFlowFieldLease warm = CreateGuide(cache, fixture);
            warm.TrySample(destination.FootAnchor, GenerousBudget, out _)
                .Should().Be(NavigationGuideStatus.Success);
            warm.Dispose();
        }

        bool succeeded = true;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            NavigationFlowFieldLease guide = CreateGuideUnchecked(
                cache,
                fixture,
                ref succeeded);
            if (!succeeded)
                break;
            if (guide.TrySample(
                    destination.FootAnchor,
                    GenerousBudget,
                    out _) != NavigationGuideStatus.Success)
            {
                succeeded = false;
            }
            guide.Dispose();
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        succeeded.Should().BeTrue();
        allocated.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    private static GuideSampleWorkBudget GenerousBudget => new(
        128,
        128,
        8,
        32,
        32,
        32,
        1);

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture) => new(
        1,
        fixture.Far.RetainedBytes,
        fixture.Far.RetainedBytes,
        fixture.Far.RetainedBytes,
        1,
        8);

    private static NavigationFlowFieldLease CreateGuide(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        bool succeeded = true;
        NavigationFlowFieldLease guide = CreateGuideUnchecked(
            cache,
            fixture,
            ref succeeded);
        succeeded.Should().BeTrue();
        return guide;
    }

    private static NavigationFlowFieldLease CreateGuideUnchecked(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        ref bool succeeded)
    {
        NavigationFlowFieldStatus checkout = cache.TryCheckout(
            fixture.Store,
            fixture.Store.Current,
            fixture.Far.Key,
            fixture.FarOrigin,
            out NavigationFlowFieldPayloadLease payloadLease);
        if (checkout == NavigationFlowFieldStatus.Pending)
        {
            if (!cache.TryReservePayload(
                    fixture.Far.RetainedBytes,
                    out NavigationFlowFieldReservation reservation)
                || cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref reservation,
                    out payloadLease) != NavigationFlowFieldStatus.Success)
            {
                succeeded = false;
                return default;
            }
        }
        else if (checkout != NavigationFlowFieldStatus.Success)
        {
            succeeded = false;
            return default;
        }
        if (cache.TryCreateGuide(
                fixture.World,
                fixture.Store,
                new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
                out NavigationFlowFieldLease guide) != NavigationGuideStatus.Success)
        {
            succeeded = false;
            return default;
        }
        return guide;
    }

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
}
