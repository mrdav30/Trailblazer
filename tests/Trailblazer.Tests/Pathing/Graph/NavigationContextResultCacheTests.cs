//=======================================================================
// NavigationContextResultCacheTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationContextResultCacheTests
{
    [Fact]
    public void Checkout_ShouldRejectAnExactDependencyThatIsNoLongerCurrent()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp firstStamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);

        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        PathRequestCacheKey key = TestPathRequest.CreateCacheKey(7);
        cache.TryCreateDetached(query!, key, new object(), firstStamp, 64, out NavigationResultEntryLease<object>? seeded)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, seeded!)
            .Should().Be(NavigationResultCacheStatus.Published);
        seeded!.Dispose();
        query!.Dispose();

        PublishPolicy(store, "ground", revision: 2, graphVersion: 2);

        cache.TryCheckout(key, out NavigationResultEntryLease<object>? stale).Should().BeFalse();
        stale.Should().BeNull();
        cache.EntryCount.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Publication_ShouldWaitForPromotionGate_ThenCheckoutShouldRejectStaleEntry()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp firstStamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        cache.TryCreateDetached(query!, TestPathRequest.CreateCacheKey(7), new object(), firstStamp, 64, out NavigationResultEntryLease<object>? candidate)
            .Should().Be(NavigationResultCacheStatus.Detached);

        PathRequestCacheKey key = TestPathRequest.CreateCacheKey(7);
        using var started = new ManualResetEventSlim();
        Task<NavigationCandidatePublication> publication;
        lock (store.CacheGate.SyncRoot)
        {
            publication = Task.Run(() =>
            {
                started.Set();
                return store.TryPublish(
                    CreatePolicyGraph("ground", revision: 2, graphVersion: 2));
            });
            started.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken)
                .Should().BeTrue();
            cache.TryPromote(query!, candidate!)
                .Should().Be(NavigationResultCacheStatus.Published);
            publication.IsCompleted.Should()
                .BeFalse("graph publication must share and wait for the promotion gate");
        }

        (await publication).Should().Be(NavigationCandidatePublication.Published);
        cache.TryCheckout(key, out NavigationResultEntryLease<object>? stale)
            .Should().BeFalse();
        stale.Should().BeNull();
        cache.EntryCount.Should().Be(0);
        candidate!.Dispose();
        cache.DetachedBytes.Should().Be(0);
        query!.Dispose();
    }

    [Fact]
    public void SameKeyPromotion_ShouldKeepTheFirstValidResultAndCheckoutItForTheLoser()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        var winnerPayload = new object();
        var loserPayload = new object();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? firstQuery)
            .Should().BeTrue();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(2, 4, 64),
                out NavigationQueryAdmissionLease? secondQuery)
            .Should().BeTrue();
        PathRequestCacheKey key = TestPathRequest.CreateCacheKey(7);
        cache.TryCreateDetached(firstQuery!, key, winnerPayload, stamp, 64, out NavigationResultEntryLease<object>? first)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryCreateDetached(secondQuery!, key, loserPayload, stamp, 64, out NavigationResultEntryLease<object>? second)
            .Should().Be(NavigationResultCacheStatus.Detached);

        cache.TryPromote(firstQuery!, first!)
            .Should().Be(NavigationResultCacheStatus.Published);
        cache.TryPromote(secondQuery!, second!)
            .Should().Be(NavigationResultCacheStatus.ReusedExisting);

        first!.Payload.Should().BeSameAs(winnerPayload);
        second!.Payload.Should().BeSameAs(winnerPayload);
        cache.EntryCount.Should().Be(1);
        cache.CachedBytes.Should().Be(64);
        cache.DetachedBytes.Should().Be(0);
        cache.DiscardedDuplicateBytes.Should().Be(64);
        first.Dispose();
        second.Dispose();
        firstQuery!.Dispose();
        secondQuery!.Dispose();
    }

    [Fact]
    public void Return_ShouldRemoveAnEntryWhoseDependencyChangedWhileCheckedOut()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        cache.TryCreateDetached(query!, TestPathRequest.CreateCacheKey(7), new object(), stamp, 64, out NavigationResultEntryLease<object>? result)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, result!)
            .Should().Be(NavigationResultCacheStatus.Published);

        PublishPolicy(store, "ground", revision: 2, graphVersion: 2);
        result!.Dispose();

        cache.EntryCount.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        query!.Dispose();
    }

    [Fact]
    public async Task Checkout_ShouldAcquireSnapshotLeaseBeforeWaitingForCacheGate()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        PathRequestCacheKey key = TestPathRequest.CreateCacheKey(7);
        cache.TryCreateDetached(query!, key, new object(), stamp, 64, out NavigationResultEntryLease<object>? seeded)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, seeded!)
            .Should().Be(NavigationResultCacheStatus.Published);
        seeded!.Dispose();
        query!.Dispose();

        NavigationResultEntryLease<object>? checkout = null;
        bool checkedOut = false;
        Task checkoutTask;
        lock (store.CacheGate.SyncRoot)
        {
            checkoutTask = Task.Run(() => checkedOut = cache.TryCheckout(key, out checkout));
            SpinWait.SpinUntil(() => store.ActiveLeaseCount == 1, TimeSpan.FromSeconds(5))
                .Should().BeTrue("checkout must pin the snapshot before it can enter the cache gate");
            checkoutTask.IsCompleted.Should().BeFalse();
        }

        await checkoutTask;
        checkedOut.Should().BeTrue();
        checkout.Should().NotBeNull();
        store.ActiveLeaseCount.Should().Be(0);
        checkout!.Dispose();
    }

    [Fact]
    public void DetachedResult_ShouldBeBoundedByItsQueryReservation()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxActiveResultBytes: 64);
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 32),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();

        cache.TryCreateDetached(query!, TestPathRequest.CreateCacheKey(7), new object(), stamp, 33, out NavigationResultEntryLease<object>? result)
            .Should().Be(NavigationResultCacheStatus.CapacityExceeded);
        result.Should().BeNull();
        cache.DetachedBytes.Should().Be(0);
        query!.Dispose();
    }

    [Fact]
    public void CachedAndDetachedPayloads_ShouldShareTheConfiguredResultByteCeiling()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxActiveResultBytes: 64);
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 40),
                out NavigationQueryAdmissionLease? firstQuery)
            .Should().BeTrue();
        cache.TryCreateDetached(
                firstQuery!,
                TestPathRequest.CreateCacheKey(1),
                new object(),
                stamp,
                40,
                out NavigationResultEntryLease<object>? cached)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(firstQuery!, cached!)
            .Should().Be(NavigationResultCacheStatus.Published);
        cached!.Dispose();
        firstQuery!.Dispose();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(2, 4, 25),
                out NavigationQueryAdmissionLease? secondQuery)
            .Should().BeFalse();
        secondQuery.Should().BeNull();
        cache.CachedBytes.Should().Be(40);
        cache.DetachedBytes.Should().Be(0);
        store.CacheGate.TotalResultBytes.Should().Be(40);
    }

    [Fact]
    public void MultipleCachesAndQueryReservations_ShouldShareOneContextByteCeiling()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxActiveResultBytes: 64);
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var firstCache = new NavigationContextResultCache<object>(store, settings);
        using var secondCache = new NavigationContextResultCache<string>(store, settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();

        firstCache.TryCreateDetached(
                query!,
                TestPathRequest.CreateCacheKey(1),
                new object(),
                stamp,
                32,
                out NavigationResultEntryLease<object>? first)
            .Should().Be(NavigationResultCacheStatus.Detached);
        firstCache.TryPromote(query!, first!).Should().Be(NavigationResultCacheStatus.Published);
        first!.Dispose();
        store.CacheGate.ReservedResultBytes.Should().Be(32);
        store.CacheGate.PayloadResultBytes.Should().Be(32);
        secondCache.TryCreateDetached(
                query!,
                TestPathRequest.CreateCacheKey(2),
                "payload",
                stamp,
                32,
                out NavigationResultEntryLease<string>? second)
            .Should().Be(NavigationResultCacheStatus.Detached);
        secondCache.TryPromote(query!, second!).Should().Be(NavigationResultCacheStatus.Published);
        second!.Dispose();

        store.CacheGate.ReservedResultBytes.Should().Be(0);
        store.CacheGate.PayloadResultBytes.Should().Be(64);
        store.CacheGate.TotalResultBytes.Should().Be(64);
        query!.Dispose();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(2, 4, 1),
                out _)
            .Should().BeFalse("cached payloads consume the context ceiling after their query returns");

        firstCache.Dispose();
        store.CacheGate.PayloadResultBytes.Should().Be(32);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(3, 4, 32),
                out NavigationQueryAdmissionLease? admitted)
            .Should().BeTrue();
        store.CacheGate.TotalResultBytes.Should().Be(64);
        admitted!.Dispose();
        store.CacheGate.TotalResultBytes.Should().Be(32);
    }

    [Fact]
    public void DuplicateReuseAndStaleRemoval_ShouldConserveSharedPayloadLedger()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxActiveResultBytes: 128);
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(store, settings);
        admission.TryAdmit(new NavigationQueryAdmissionRequest(1, 4, 128), out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        PathRequestCacheKey key = TestPathRequest.CreateCacheKey(1);
        cache.TryCreateDetached(query!, key, new object(), stamp, 32, out NavigationResultEntryLease<object>? winner)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryCreateDetached(query!, key, new object(), stamp, 32, out NavigationResultEntryLease<object>? loser)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, winner!).Should().Be(NavigationResultCacheStatus.Published);
        cache.TryPromote(query!, loser!).Should().Be(NavigationResultCacheStatus.ReusedExisting);

        store.CacheGate.ReservedResultBytes.Should().Be(64);
        store.CacheGate.PayloadResultBytes.Should().Be(32,
            "duplicate payload ownership is released when the loser rebinds");
        winner!.Dispose();
        loser!.Dispose();
        query!.Dispose();
        store.CacheGate.TotalResultBytes.Should().Be(32);

        PublishPolicy(store, "ground", revision: 2, graphVersion: 2);
        cache.TryCheckout(key, out _).Should().BeFalse();
        store.CacheGate.TotalResultBytes.Should().Be(0,
            "removing a stale unleased entry releases its payload ownership");
    }

    [Fact]
    public void Dispose_ShouldLeaveCheckedOutResultSafeToReturn()
    {
        TrailblazerWorldContextSettings settings = QuerySettings();
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        var cache = new NavigationContextResultCache<object>(
            store,
            settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 64),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        cache.TryCreateDetached(
                query!,
                TestPathRequest.CreateCacheKey(1),
                new object(),
                stamp,
                64,
                out NavigationResultEntryLease<object>? result)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, result!)
            .Should().Be(NavigationResultCacheStatus.Published);

        cache.Dispose();

        Action release = () => result!.Dispose();
        release.Should().NotThrow();
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        query!.Dispose();
    }

    [Fact]
    public void PendingSafetyEpoch_ShouldBlockAdmissionAndDiscardSpanningResults()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxConcurrentQueries: 3);
        using var store = CreateStore(settings);
        GraphDependencyStamp stamp = PublishPolicy(store, "ground", revision: 1, graphVersion: 1);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        using var cache = new NavigationContextResultCache<object>(store, settings);
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 192),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        PathRequestCacheKey cachedKey = TestPathRequest.CreateCacheKey(1);
        PathRequestCacheKey detachedKey = TestPathRequest.CreateCacheKey(2);
        PathRequestCacheKey returnedKey = TestPathRequest.CreateCacheKey(3);
        cache.TryCreateDetached(
                query!,
                cachedKey,
                new object(),
                stamp,
                64,
                out NavigationResultEntryLease<object>? cached)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, cached!)
            .Should().Be(NavigationResultCacheStatus.Published);
        cache.TryCreateDetached(
                query!,
                returnedKey,
                new object(),
                stamp,
                64,
                out NavigationResultEntryLease<object>? returned)
            .Should().Be(NavigationResultCacheStatus.Detached);
        cache.TryPromote(query!, returned!)
            .Should().Be(NavigationResultCacheStatus.Published);
        cache.TryCreateDetached(
                query!,
                detachedKey,
                new object(),
                stamp,
                64,
                out NavigationResultEntryLease<object>? detached)
            .Should().Be(NavigationResultCacheStatus.Detached);

        store.CacheGate.MarkSafetyPending();

        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(2, 4, 1),
                out _)
            .Should().BeFalse();
        cache.TryCreateDetached(
                query!,
                TestPathRequest.CreateCacheKey(4),
                new object(),
                stamp,
                1,
                out NavigationResultEntryLease<object>? duringSafety)
            .Should().Be(NavigationResultCacheStatus.Stale);
        duringSafety.Should().BeNull();
        cache.TryPromote(query!, detached!)
            .Should().Be(NavigationResultCacheStatus.Stale);
        returned!.Dispose();
        cache.EntryCount.Should().Be(1,
            "returning a cached result during pending safety must drop that entry");
        cache.TryCheckout(cachedKey, out NavigationResultEntryLease<object>? checkout)
            .Should().BeFalse();
        checkout.Should().BeNull();
        cache.EntryCount.Should().Be(0);

        cached!.Dispose();
        detached!.Dispose();
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        store.CacheGate.ClearSafetyPending();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(3, 4, 1),
                out NavigationQueryAdmissionLease? afterSafety)
            .Should().BeTrue();
        cache.TryCreateDetached(
                query!,
                TestPathRequest.CreateCacheKey(5),
                new object(),
                stamp,
                1,
                out _)
            .Should().Be(NavigationResultCacheStatus.Stale,
                "clearing the barrier must not revive a pre-safety query epoch");
        afterSafety!.Dispose();
        query!.Dispose();
    }

    private static GraphDependencyStamp PublishPolicy(
        NavigationWorldGraphStore store,
        string policyId,
        long revision,
        long graphVersion)
    {
        var key = new NavigationAreaPolicyKey(policyId, revision);
        NavigationWorldGraph graph = CreatePolicyGraph(policyId, revision, graphVersion);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        return new GraphDependencyStamp(
            key,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());
    }

    private static NavigationWorldGraph CreatePolicyGraph(
        string policyId,
        long revision,
        long graphVersion)
    {
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey(policyId, revision),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        NavigationOperationRejection rejection = NavigationAreaCatalog.Empty.TryPublish(
            policy,
            1,
            1,
            1,
            1,
            out NavigationAreaCatalog catalog);
        if (rejection != NavigationOperationRejection.None)
            throw new InvalidOperationException("The test policy graph could not be created.");
        return new NavigationWorldGraph(
            graphVersion,
            Array.Empty<NavigationMapInstance>(),
            catalog);
    }

    private static NavigationWorldGraphStore CreateStore(TrailblazerWorldContextSettings settings) => new(
        settings.MaxActiveSnapshots,
        settings.MaxRetiredSnapshots,
        settings.MaxRetiredSnapshotBytes,
        settings.MaxActiveSnapshotBytes,
        settings.MaxPersistentGraphPages,
        settings.MaxConcurrentPathQueries,
        settings.MaxActiveQueryResultBytes);

    private static TrailblazerWorldContextSettings QuerySettings(
        long maxActiveResultBytes = 1_000_000,
        int maxConcurrentQueries = 2)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            maxIngressEntries: 32,
            maxIngressBytes: 32 * 256,
            maxActiveSnapshots: 3,
            maxActiveSnapshotBytes: 1_000_000,
            maxRetiredSnapshots: 2,
            maxRetiredSnapshotBytes: 2_000_000,
            maxPersistentGraphPages: 1_000,
            maxDynamicCellSlotsPerMap: defaults.MaxDynamicCellSlotsPerMap,
            maxDynamicCellSlots: defaults.MaxDynamicCellSlots,
            navigationAreaCount: 1,
            maxAreaPolicies: 8,
            maxAreaRulesPerPolicy: 32,
            maxAreaRules: 64,
            maxConcurrentPathQueries: maxConcurrentQueries,
            maxActiveWorkspaceBytes: 1_000_000,
            maxRetainedWorkspaceBytes: 1_000_000,
            maxActiveQueryResultBytes: maxActiveResultBytes);
    }
}
