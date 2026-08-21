//=======================================================================
// NavigationAStarConcurrencyTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Reflection;
using System.Threading;
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
public sealed class NavigationAStarConcurrencyTests
{
    [Fact]
    public void NegativePublicationMutationAfterPrecheck_ShouldReturnStaleWithoutLeaks()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = { default, new VoxelIndex(2, 0, 0) };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(4),
                cells,
                "negative-publication");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 4);
        var workspace = new NavigationAStarWorkspace(1, 8, 10, 8, 8, 8, 8);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: long.MaxValue,
            maxSinglePayloadBytes: long.MaxValue,
            maxActivePayloadBytes: long.MaxValue,
            maxActiveLeases: 2);
        PathQuery query = fixture.CreateQuery(
            cells[0],
            cells[1],
            fixture.DefaultProfile);
        using NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        Prepare(work);
        CompleteSearch(work);

        object cacheSync = typeof(NavigationAStarPayloadCache)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        using var publisherStarted = new ManualResetEventSlim();
        NavigationAStarQueryStatus publicationStatus = default;
        Exception? publicationError = null;
        var publisher = new Thread(() =>
        {
            publisherStarted.Set();
            try
            {
                publicationStatus = work.Publish();
            }
            catch (Exception error)
            {
                publicationError = error;
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
                .Should().BeTrue(
                    "the publisher must reach the cache after its dependency precheck");

            NavigationWorldGraph changed = fixture.Graph.WithSurfaceComponents(
                NavigationSurfaceComponentIndex.Empty).WithGraphVersion(
                    fixture.Graph.GraphVersion + 1);
            store.TryPublish(changed).Should().Be(NavigationCandidatePublication.Published);
        }
        finally
        {
            Monitor.Exit(cacheSync);
            publisher.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        publicationError.Should().BeNull();
        publicationStatus.Should().Be(NavigationAStarQueryStatus.Stale);
        store.ActiveLeaseCount.Should().Be(0);
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void PublicationAfterSearchBeforeReconstruction_ShouldReturnStaleWithoutPublishingRawLegs()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = CreateLine(16);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(20),
                cells,
                "mutation");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 4);
        var workspace = new NavigationAStarWorkspace(1, 64, 66, 32, 32, 32, 32);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 2,
            maxReusableBytes: long.MaxValue,
            maxSinglePayloadBytes: long.MaxValue,
            maxActivePayloadBytes: long.MaxValue,
            maxActiveLeases: 4);
        PathQuery query = fixture.CreateQuery(
            cells[0],
            cells[cells.Length - 1],
            fixture.DefaultProfile);
        using NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        Prepare(work);
        NavigationSurfaceAStarWork search = (NavigationSurfaceAStarWork)typeof(
                NavigationAStarQueryWork)
            .GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(work)!;
        FieldInfo stage = typeof(NavigationSurfaceAStarWork)
            .GetField("_stage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int step = 0;
            step < cells.Length && stage.GetValue(search)!.ToString() == "Search";
            step++)
        {
            work.AdvanceSearch(
                lookupStepLimit: int.MaxValue,
                nodeStepLimit: 1,
                edgeStepLimit: int.MaxValue,
                connectionStepLimit: int.MaxValue);
        }
        stage.GetValue(search)!.ToString().Should().Be("Reconstruct");
        work.Status.Should().Be(NavigationAStarQueryStatus.Pending);
        work.IsReadyToPublish.Should().BeFalse();

        NavigationWorldGraph changed = fixture.Graph.WithSurfaceComponents(
            NavigationSurfaceComponentIndex.Empty).WithGraphVersion(
                fixture.Graph.GraphVersion + 1);
        store.TryPublish(changed).Should().Be(NavigationCandidatePublication.Published);
        CompleteSearch(work);

        work.Publish().Should().Be(NavigationAStarQueryStatus.Stale);
        store.ActiveLeaseCount.Should().Be(0);
        cache.Count.Should().Be(0);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void SameKeyMisses_ShouldConvergeOnOnePublishedPayloadRegardlessOfCompletionOrder()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = CreateLine(4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                cells,
                "same-key");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 4);
        var firstWorkspace = new NavigationAStarWorkspace(1, 32, 34, 16, 16, 16, 16);
        var secondWorkspace = new NavigationAStarWorkspace(1, 32, 34, 16, 16, 16, 16);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 2,
            maxReusableBytes: long.MaxValue,
            maxSinglePayloadBytes: long.MaxValue,
            maxActivePayloadBytes: long.MaxValue,
            maxActiveLeases: 4);
        PathQuery query = fixture.CreateQuery(
            cells[0],
            cells[cells.Length - 1],
            fixture.DefaultProfile);
        using NavigationAStarQueryWork first = BeginReservedQuery(
            world,
            store,
            query,
            firstWorkspace,
            cache);
        using NavigationAStarQueryWork second = BeginReservedQuery(
            world,
            store,
            query,
            secondWorkspace,
            cache);
        Prepare(first);
        Prepare(second);
        cache.Count.Should().Be(0, "both workers observed a real cache miss before publication");
        cache.ReservedLeaseCount.Should().Be(2);

        CompleteSearch(second);
        CompleteSearch(first);
        first.Publish().Should().Be(NavigationAStarQueryStatus.Success);
        second.Publish().Should().Be(NavigationAStarQueryStatus.Success);
        NavigationAStarPayloadLease firstLease = first.TakeResult();
        NavigationAStarPayloadLease secondLease = second.TakeResult();

        secondLease.Payload.Should().BeSameAs(firstLease.Payload);
        cache.Count.Should().Be(1);
        cache.ActiveLeaseCount.Should().Be(2);
        cache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        secondLease.Dispose();
        firstLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void StalePayload_ShouldFailPromotionAndGuideAcquisitionWithoutLeakingLeases()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = CreateLine(3);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                cells,
                "stale-payload");
        PathQuery query = fixture.CreateQuery(
            cells[0],
            cells[cells.Length - 1],
            fixture.DefaultProfile);
        NavigationAStarExitTestHarness.SearchResult search =
            NavigationAStarExitTestHarness.RunAStar(world, fixture.Graph, query);
        NavigationAStarPayload payload = search.Payload!;
        NavigationWorldGraph changed = fixture.Graph.WithSurfaceComponents(
            NavigationSurfaceComponentIndex.Empty).WithGraphVersion(
                fixture.Graph.GraphVersion + 1);

        using (NavigationWorldGraphStore staleStore =
            NavigationAStarExitTestHarness.CreateStore(changed, 2))
        {
            var staleCache = CreateCache(world, payload.RetainedBytes);
            staleCache.TryReservePayload(
                    payload.RetainedBytes,
                    out NavigationAStarPayloadReservation staleReservation)
                .Should().BeTrue();
            staleCache.TryPublish(
                    payload,
                    staleStore,
                    ref staleReservation,
                    out _)
                .Should().BeFalse("promotion rechecks dependencies against the published root");
            staleCache.ReleasePayloadReservation(ref staleReservation);
            staleCache.Count.Should().Be(0);
            staleCache.ActiveLeaseCount.Should().Be(0);
            staleCache.ReservedLeaseCount.Should().Be(0);
        }

        using NavigationWorldGraphStore guideStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        NavigationAStarPayloadCache guideCache = CreateCache(world, payload.RetainedBytes);
        guideCache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation guideReservation)
            .Should().BeTrue();
        guideCache.TryPublish(
                payload,
                guideStore,
                ref guideReservation,
                out NavigationAStarPayloadLease payloadLease)
            .Should().BeTrue();
        guideStore.TryPublish(changed).Should().Be(NavigationCandidatePublication.Published);

        guideCache.TryCreateGuide(guideStore, payloadLease, out NavigationAStarGuideLease? guide)
            .Should().Be(NavigationAStarQueryStatus.Stale);
        guide.Should().BeNull();
        guideCache.ActiveLeaseCount.Should().Be(0);
        guideStore.ActiveLeaseCount.Should().Be(0);
        guideCache.TryCheckout(payload.Key, changed, out _).Should().BeFalse();
        guideCache.Count.Should().Be(0);
    }

    [Fact]
    public void SimplifiedPayload_WhenWorldEpochChanges_ShouldFailCacheAndGuideUse()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = CreateLine(3);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                cells,
                "world-epoch");
        PathQuery query = new(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[0]),
                fixture.MapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[^1]),
                fixture.MapId),
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                8_192, 32, 128, 1_024, 1_024, 0, 0, 0, 128, 128, 1),
            allowTransitions: false);
        NavigationAStarPayload payload = NavigationAStarExitTestHarness
            .RunAStar(world, fixture.Graph, query)
            .Payload!;
        payload.WorldChangeSequence.Should().Be(world.ChangeSequence);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        NavigationAStarPayloadCache cache = CreateCache(world, payload.RetainedBytes);
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        cache.TryPublish(payload, store, ref reservation, out NavigationAStarPayloadLease lease)
            .Should().BeTrue();
        cache.TryCreateGuide(store, lease, out NavigationAStarGuideLease? guide)
            .Should().Be(NavigationAStarQueryStatus.Success);
        guide.Should().NotBeNull();
        long generation = guide!.Generation;
        GridConfiguration mutation = new(
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.One,
                Fixed64.One,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(mutation, out _).Should().BeTrue();

        guide.TryGetCurrentWaypoint(generation, out _, out _)
            .Should().Be(NavigationAStarQueryStatus.Stale);
        cache.TryCheckout(payload.Key, fixture.Graph, out _).Should().BeFalse();
        cache.Count.Should().Be(0);
        guide.Dispose(generation);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    private static void Prepare(NavigationAStarQueryWork work)
    {
        for (int step = 0;
            step < 1_024 && work.Status == NavigationAStarQueryStatus.Pending && !work.IsPrepared;
            step++)
        {
            work.PrepareSearchOrCheckout(64, 16);
        }
        work.Status.Should().Be(NavigationAStarQueryStatus.Pending);
        work.IsPrepared.Should().BeTrue();
        work.IsReadyToPublish.Should().BeFalse();
    }

    private static NavigationAStarQueryWork BeginReservedQuery(
        GridWorld world,
        NavigationWorldGraphStore store,
        PathQuery query,
        NavigationAStarWorkspace workspace,
        NavigationAStarPayloadCache cache)
    {
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        long maximumBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        var work = new NavigationAStarQueryWork(world, store, workspace, cache);
        work.BeginReserved(query, lease!, ref reservation);
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return work;
    }

    private static void CompleteSearch(NavigationAStarQueryWork work)
    {
        for (int step = 0;
            step < 4_096 && !work.IsReadyToPublish;
            step++)
        {
            work.AdvanceSearch(64, 64, 64, 64);
        }
        work.IsReadyToPublish.Should().BeTrue();
        work.Status.Should().Be(NavigationAStarQueryStatus.Pending);
    }

    private static VoxelIndex[] CreateLine(int count)
    {
        var cells = new VoxelIndex[count];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        return cells;
    }

    private static NavigationAStarPayloadCache CreateCache(
        GridWorld world,
        long retainedBytes) => new(
        world,
        maxEntries: 1,
        maxReusableBytes: retainedBytes,
        maxSinglePayloadBytes: retainedBytes,
        maxActivePayloadBytes: retainedBytes,
        maxActiveLeases: 1);

}
