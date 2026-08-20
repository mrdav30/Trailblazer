//=======================================================================
// NavigationAStarAdmissionTests.cs
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

public sealed class NavigationAStarAdmissionTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("batch", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    [Fact]
    public void DefaultLimits_ShouldReserveTheExactCapacityEnvelope()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 1);
        NavigationQueryLimits limits = NavigationQueryLimits.Default;
        using var gate = CreateGate(world, store, limits);

        gate.Begin(Query(maxExpandedNodes: 0), out NavigationAStarBatchWork work)
            .Should().Be(NavigationAStarQueryStatus.Pending);

        work.AdmittedCount.Should().Be(1);
        gate.PayloadCache.ReservedPayloadBytes.Should().Be(
            Math.Min(
                NavigationAStarPayload.GetMaximumRetainedBytes(
                    limits.AStarWorkspaceGuidePointCapacity,
                    limits.AStarWorkspaceComponentCapacity,
                    limits.AStarWorkspaceEndpointPageCapacity),
                limits.MaxAStarSinglePayloadBytes));
        gate.PayloadCache.ReservedLeaseCount.Should().Be(1);
        work.Dispose();
        gate.PayloadCache.ReservedPayloadBytes.Should().Be(0);
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
    }

    [Fact]
    public void AggregateCoordinator_ShouldBoundAStarAgainstFlowReservation()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 1);
        var coordinator = new NavigationQueryAdmissionCoordinator(1);
        coordinator.TryReservePrefix(
                PathAlgorithm.FlowField,
                requestedCount: 1,
                out NavigationQueryCapacityReservation flow)
            .Should().Be(1);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(maxBatchItems: 1, maxConcurrentQueries: 1),
            coordinator);

        gate.Begin(Query(maxExpandedNodes: 0), out NavigationAStarBatchWork rejected)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        rejected.AdmittedCount.Should().Be(0);
        rejected.GetStatus(0).Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
        rejected.Dispose();
        store.ActiveLeaseCount.Should().Be(0);

        coordinator.Release(flow);
        gate.Begin(Query(maxExpandedNodes: 0), out NavigationAStarBatchWork admitted)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        admitted.Dispose();
        coordinator.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void PayloadCache_ShouldReserveActiveLeaseSlots()
    {
        var cache = new NavigationAStarPayloadCache(
            maxEntries: 1,
            maxReusableBytes: 1_024,
            maxSinglePayloadBytes: 1_024,
            maxActivePayloadBytes: 2_048,
            maxActiveLeases: 1);

        cache.TryReservePayload(1_024, out NavigationAStarPayloadReservation first)
            .Should().BeTrue();
        cache.TryReservePayload(1_024, out NavigationAStarPayloadReservation second)
            .Should().BeFalse();

        cache.ReleasePayloadReservation(ref first);
        cache.TryReservePayload(1_024, out second).Should().BeTrue();
        cache.ReleasePayloadReservation(ref second);
    }

    [Fact]
    public void Batch_ShouldRejectOversizeInputBeforeAcquiringAnyResource()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 2);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(maxBatchItems: 1, maxConcurrentQueries: 2));
        PathQuery query = Query(maxExpandedNodes: 0);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(1, query),
                new PathQueryBatchItem(2, query)
            },
            count: 2);

        gate.Begin(batch, out NavigationAStarBatchWork work)
            .Should().Be(NavigationAStarQueryStatus.CapacityExceeded);

        work.Should().Be(default(NavigationAStarBatchWork));
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);

        gate.Begin(query, out NavigationAStarBatchWork single)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        single.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Batch_ShouldRejectDescriptorBytesBeforeAcquiringAnyResource()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 1);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(
                maxBatchItems: 1,
                maxConcurrentQueries: 1,
                maxBatchDescriptorBytes: 257));
        PathQuery query = Query(maxExpandedNodes: 0);
        var batch = new PathQueryBatch(
            new[] { new PathQueryBatchItem(1, query) },
            count: 1);

        gate.Begin(batch, out NavigationAStarBatchWork rejected)
            .Should().Be(NavigationAStarQueryStatus.CapacityExceeded);

        rejected.Should().Be(default(NavigationAStarBatchWork));
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        gate.Begin(query, out NavigationAStarBatchWork rejectedSingle)
            .Should().Be(NavigationAStarQueryStatus.CapacityExceeded,
                "ad-hoc queries share the exact descriptor envelope");
        rejectedSingle.Should().Be(default(NavigationAStarBatchWork));
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);

        using var acceptedGate = CreateGate(
            world,
            store,
            CreateLimits(
                maxBatchItems: 1,
                maxConcurrentQueries: 1,
                maxBatchDescriptorBytes: 258));
        acceptedGate.Begin(batch, out NavigationAStarBatchWork single)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        single.Dispose();
        acceptedGate.Begin(query, out single)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        single.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Batch_ShouldRejectEmptyInputWithoutActivatingTheGate()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 1);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(maxBatchItems: 1, maxConcurrentQueries: 1));
        var empty = new PathQueryBatch(Array.Empty<PathQueryBatchItem>(), count: 0);

        gate.Begin(empty, out NavigationAStarBatchWork rejected)
            .Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
        rejected.Should().Be(default(NavigationAStarBatchWork));

        gate.Begin(Query(maxExpandedNodes: 0), out NavigationAStarBatchWork single)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        single.Dispose();
    }

    [Fact]
    public void Batch_ShouldAdmitStableOrdinalPrefixWithoutBackfillingAfterFailure()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 3);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(
                maxBatchItems: 3,
                maxConcurrentQueries: 3,
                maxSinglePayloadBytes: 472,
                maxActivePayloadBytes: 472,
                maxActivePayloadLeases: 3));
        PathQuery small = Query(maxExpandedNodes: 0);
        PathQuery oversized = Query(maxExpandedNodes: 4);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(30, small),
                new PathQueryBatchItem(10, small),
                new PathQueryBatchItem(20, oversized)
            },
            count: 3);

        gate.Begin(batch, out NavigationAStarBatchWork work)
            .Should().Be(NavigationAStarQueryStatus.Pending);

        NavigationAStarBatchWork admitted = work;
        using (admitted)
        {
            admitted.GetStatus(inputIndex: 1).Should().Be(NavigationAStarQueryStatus.Pending);
            admitted.GetStatus(inputIndex: 2).Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
            admitted.GetStatus(inputIndex: 0).Should().Be(NavigationAStarQueryStatus.CapacityExceeded,
                "a later smaller item cannot backfill after the first ordinal reservation failure");
            gate.PayloadCache.ReservedLeaseCount.Should().Be(1);
            store.ActiveLeaseCount.Should().Be(1);
        }

        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Batch_ShouldPublishTheSameCanonicalPayloadUnderForwardAndReverseCompletion()
    {
        using var world = new GridWorld();
        NavigationWorldGraph graph = CreateLineGraph(
            world,
            out Vector3d start,
            out Vector3d end);
        using NavigationWorldGraphStore reverseStore = CreateStore(graph, maxConcurrentLeases: 2);
        NavigationAStarPayload reverse = RunCompletionOrder(
            world,
            reverseStore,
            start,
            end,
            reverseCompletion: true);
        using NavigationWorldGraphStore forwardStore = CreateStore(graph, maxConcurrentLeases: 2);
        NavigationAStarPayload forward = RunCompletionOrder(
            world,
            forwardStore,
            start,
            end,
            reverseCompletion: false);

        reverse.GuidePoints.Should().Equal(forward.GuidePoints);
        reverse.Cost.Should().Be(forward.Cost);
        reverse.Status.Should().Be(forward.Status);
    }

    [Fact]
    public void SingleAndBatchAdmission_ShouldShareOneGateAndReleaseResources()
    {
        using var world = new GridWorld();
        using NavigationWorldGraphStore store = CreateStore(maxConcurrentLeases: 1);
        using var gate = CreateGate(
            world,
            store,
            CreateLimits(maxBatchItems: 1, maxConcurrentQueries: 1));
        PathQuery query = Query(maxExpandedNodes: 0);

        gate.Begin(query, out NavigationAStarBatchWork single)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        gate.Begin(
                new PathQueryBatch(
                    new[] { new PathQueryBatchItem(1, query) },
                    count: 1),
                out NavigationAStarBatchWork overlapping)
            .Should().Be(NavigationAStarQueryStatus.CapacityExceeded);

        overlapping.Should().Be(default(NavigationAStarBatchWork));
        store.ActiveLeaseCount.Should().Be(1);
        single.Dispose();
        store.ActiveLeaseCount.Should().Be(0);

        gate.Begin(query, out NavigationAStarBatchWork replacement)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        replacement.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ReleasedBatchHandle_ShouldRemainStaleAfterReplacementBegins()
    {
        using var world = new GridWorld();
        NavigationWorldGraph graph = CreateLineGraph(
            world,
            out Vector3d start,
            out Vector3d end);
        using NavigationWorldGraphStore store = CreateStore(graph, maxConcurrentLeases: 1);
        using var gate = CreateGate(
            world,
            store,
            new NavigationQueryLimits(
                maxBatchItems: 1,
                maxBatchDescriptorBytes: 1_024,
                maxConcurrentNavigationQueries: 1,
                aStarWorkspaceMapCapacity: 1,
                aStarWorkspaceEndpointPageCapacity: 2,
                aStarWorkspaceNodeCapacity: 2,
                maxAStarCacheEntries: 1,
                maxAStarReusablePayloadBytes: 2_048,
                maxAStarSinglePayloadBytes: 1_024,
                maxAStarActivePayloadBytes: 1_024,
                maxAStarActivePayloadLeases: 1,
                aStarWorkspaceComponentCapacity: 4,
                flowWorkspaceMapCapacity: 1,
                flowWorkspaceEndpointPageCapacity: 2,
                flowWorkspaceComponentCapacity: 4,
                flowWorkspaceNodeCapacity: 2,
                rayWorkspaceCoveredAddressCapacity: 2,
                rayWorkspaceTraceIntervalCapacity: 2,
                aStarWorkspaceGuidePointCapacity: 3,
                maxFlowCacheEntries: 1,
                maxFlowReusablePayloadBytes: 2_048,
                maxFlowSinglePayloadBytes: 1_024,
                maxFlowActivePayloadBytes: 1_024,
                maxFlowActivePayloadLeases: 1));
        PathQuery query = Query(start, end, maxExpandedNodes: 2);

        gate.Begin(query, out NavigationAStarBatchWork first)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork stale = first;
        stale.Dispose();
        gate.Begin(query, out NavigationAStarBatchWork second)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork replacement = second;

        stale.Dispose();
        Action getStaleStatus = () => stale.GetStatus(inputIndex: 0);
        Action advanceStaleSearch = () => stale.AdvanceSearch(
            inputIndex: 0,
            lookupStepLimit: 1,
            nodeStepLimit: 1,
            edgeStepLimit: 1,
            connectionStepLimit: 1);

        getStaleStatus.Should().Throw<ObjectDisposedException>();
        advanceStaleSearch.Should().Throw<ObjectDisposedException>();
        gate.PayloadCache.ReservedLeaseCount.Should().Be(1);
        store.ActiveLeaseCount.Should().Be(1);

        using (replacement)
        {
            DrainAdmission(replacement);
            DrainSearch(replacement, inputIndex: 0);
            replacement.PublishReadyPrefix(maximumCount: 1).Should().Be(1);
            replacement.GetStatus(inputIndex: 0)
                .Should().Be(NavigationAStarQueryStatus.Success);
            replacement.TakeResult(inputIndex: 0).Dispose();
        }
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        gate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ContextReset_ShouldCancelActiveQueryAdmissionBeforeResettingTheGraph()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationAStarAdmissionGate gate = context.Pathing.NavigationAStarAdmissionGate;

        gate.Begin(Query(maxExpandedNodes: 0), out NavigationAStarBatchWork work)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        gate.PayloadCache.ReservedLeaseCount.Should().Be(1);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(1);

        context.Reset();

        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void CancelActive_ShouldDrainExecutingSlotBeforeReusingItsResources()
    {
        using var world = new GridWorld();
        NavigationWorldGraph graph = CreateLineGraph(
            world,
            out Vector3d start,
            out Vector3d end);
        using NavigationWorldGraphStore store = CreateStore(graph, maxConcurrentLeases: 1);
        using var gate = CreateGate(
            world,
            store,
            new NavigationQueryLimits(
                maxBatchItems: 1,
                maxBatchDescriptorBytes: 1_024,
                maxConcurrentNavigationQueries: 1,
                aStarWorkspaceMapCapacity: 1,
                aStarWorkspaceEndpointPageCapacity: 2,
                aStarWorkspaceNodeCapacity: 2,
                maxAStarCacheEntries: 1,
                maxAStarReusablePayloadBytes: 2_048,
                maxAStarSinglePayloadBytes: 1_024,
                maxAStarActivePayloadBytes: 1_024,
                maxAStarActivePayloadLeases: 1,
                aStarWorkspaceComponentCapacity: 4,
                flowWorkspaceMapCapacity: 1,
                flowWorkspaceEndpointPageCapacity: 2,
                flowWorkspaceComponentCapacity: 4,
                flowWorkspaceNodeCapacity: 2,
                rayWorkspaceCoveredAddressCapacity: 2,
                rayWorkspaceTraceIntervalCapacity: 2,
                aStarWorkspaceGuidePointCapacity: 3,
                maxFlowCacheEntries: 1,
                maxFlowReusablePayloadBytes: 2_048,
                maxFlowSinglePayloadBytes: 1_024,
                maxFlowActivePayloadBytes: 1_024,
                maxFlowActivePayloadLeases: 1));
        PathQuery query = Query(start, end, maxExpandedNodes: 2);
        gate.Begin(query, out NavigationAStarBatchWork created)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork work = created;
        DrainAdmission(work);
        work.AdmittedCount.Should().Be(1);

        object sync = typeof(NavigationAStarAdmissionGate)
            .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(gate)!;
        NavigationAStarQueryWork slot = ((NavigationAStarQueryWork[])typeof(
                NavigationAStarAdmissionGate)
            .GetField("_queries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(gate)!)[0];
        using var workerStarted = new ManualResetEventSlim();
        using var workerExited = new ManualResetEventSlim();
        using var cancelStarted = new ManualResetEventSlim();
        using var cancelExited = new ManualResetEventSlim();
        Exception? workerFailure = null;
        Exception? cancelFailure = null;
        var worker = new Thread(() =>
        {
            workerStarted.Set();
            try
            {
                work.AdvanceSearch(0, 0, 1, 1, 0);
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                workerExited.Set();
            }
        });
        var cancel = new Thread(() =>
        {
            cancelStarted.Set();
            try
            {
                gate.CancelActive();
            }
            catch (Exception exception)
            {
                cancelFailure = exception;
            }
            finally
            {
                cancelExited.Set();
            }
        });

        Monitor.Enter(slot);
        try
        {
            worker.Start();
            workerStarted.Wait(5_000, TestContext.Current.CancellationToken)
                .Should().BeTrue();
            bool workerBlockedInGate = false;
            for (int attempt = 0;
                attempt < 50 && !workerExited.IsSet && !workerBlockedInGate;
                attempt++)
            {
                if (worker.Join(millisecondsTimeout: 1))
                    break;
                bool enteredGate = Monitor.TryEnter(sync, millisecondsTimeout: 100);
                if (enteredGate)
                    Monitor.Exit(sync);
                else
                    workerBlockedInGate = true;
            }
            workerFailure.Should().BeNull();
            workerBlockedInGate.Should().BeTrue(
                "the admitted worker must own the gate until it holds its slot monitor");

            cancel.Start();
            cancelStarted.Wait(5_000, TestContext.Current.CancellationToken)
                .Should().BeTrue();
            cancelExited.IsSet.Should().BeFalse(
                "cancellation cannot release a slot while its worker is executing");
        }
        finally
        {
            Monitor.Exit(slot);
            worker.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            if (cancel.ThreadState == ThreadState.Unstarted)
                cancel.Start();
            cancel.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        workerFailure.Should().BeNull();
        cancelFailure.Should().BeNull();
        workerExited.IsSet.Should().BeTrue();
        cancelExited.IsSet.Should().BeTrue();
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        gate.PayloadCache.ReservedPayloadBytes.Should().Be(0);
        gate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        gate.PayloadCache.LeasedBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);

        var replacementBatch = new PathQueryBatch(
            new[] { new PathQueryBatchItem(1, query) },
            count: 1);
        gate.Begin(replacementBatch, out NavigationAStarBatchWork replacement)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork replacementWork = replacement;
        using (replacementWork)
        {
            DrainAdmission(replacementWork);
            DrainSearch(replacementWork, inputIndex: 0);
            replacementWork.PublishReadyPrefix(maximumCount: 1).Should().Be(1);
            replacementWork.GetStatus(inputIndex: 0)
                .Should().Be(NavigationAStarQueryStatus.Success);
            replacementWork.TakeResult(inputIndex: 0).Dispose();
        }
        gate.PayloadCache.ReservedLeaseCount.Should().Be(0);
        gate.PayloadCache.ReservedPayloadBytes.Should().Be(0);
        gate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        gate.PayloadCache.LeasedBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void CachedCheckout_ShouldEvictExactPayloadWhenPublicationMakesItStale()
    {
        using var world = new GridWorld();
        NavigationWorldGraph graph = CreateLineGraph(
            world,
            out Vector3d start,
            out Vector3d end);
        using NavigationWorldGraphStore store = CreateStore(graph, maxConcurrentLeases: 1);
        var workspace = new NavigationAStarWorkspace(1, 2, 4, 2, 2, 2, 3);
        var cache = new NavigationAStarPayloadCache(
            maxEntries: 1,
            maxReusableBytes: 2_048,
            maxSinglePayloadBytes: 1_024,
            maxActivePayloadBytes: 1_024,
            maxActiveLeases: 1);
        PathQuery query = Query(start, end, maxExpandedNodes: 2);
        using (NavigationAStarQueryWork seed = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(seed);
            seed.Status.Should().Be(NavigationAStarQueryStatus.Success);
            seed.TakeResult().Dispose();
        }
        cache.Count.Should().Be(1);

        using NavigationAStarQueryWork cached = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        for (int step = 0; step < 64 && !cached.IsReadyToPublish; step++)
            cached.PrepareSearchOrCheckout(1, 1);
        cached.IsReadyToPublish.Should().BeTrue();
        NavigationWorldGraph changed = graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(graph.GraphVersion + 1);
        store.TryPublish(changed).Should().Be(NavigationCandidatePublication.Published);

        cached.Publish().Should().Be(NavigationAStarQueryStatus.Stale);
        cache.Count.Should().Be(0,
            "the stale exact hit must not survive for another checkout");
        cache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void BatchCacheCheckout_ShouldTouchInOrdinalOrderBeforeForcedEviction()
    {
        using var world = new GridWorld();
        NavigationWorldGraph graph = CreateLineGraph(
            world,
            out Vector3d start,
            out Vector3d end);
        using NavigationWorldGraphStore store = CreateStore(graph, maxConcurrentLeases: 2);
        using var gate = CreateGate(
            world,
            store,
            new NavigationQueryLimits(
                maxBatchItems: 2,
                maxBatchDescriptorBytes: 1_024,
                maxConcurrentNavigationQueries: 2,
                aStarWorkspaceMapCapacity: 1,
                aStarWorkspaceEndpointPageCapacity: 2,
                aStarWorkspaceNodeCapacity: 2,
                maxAStarCacheEntries: 2,
                maxAStarReusablePayloadBytes: 4_096,
                maxAStarSinglePayloadBytes: 1_024,
                maxAStarActivePayloadBytes: 2_048,
                maxAStarActivePayloadLeases: 2,
                aStarWorkspaceComponentCapacity: 4,
                flowWorkspaceMapCapacity: 1,
                flowWorkspaceEndpointPageCapacity: 2,
                flowWorkspaceComponentCapacity: 4,
                flowWorkspaceNodeCapacity: 2,
                rayWorkspaceCoveredAddressCapacity: 2,
                rayWorkspaceTraceIntervalCapacity: 2,
                aStarWorkspaceGuidePointCapacity: 3,
                maxFlowCacheEntries: 2,
                maxFlowReusablePayloadBytes: 4_096,
                maxFlowSinglePayloadBytes: 1_024,
                maxFlowActivePayloadBytes: 2_048,
                maxFlowActivePayloadLeases: 2));
        PathQuery queryA = Query(start, end, maxExpandedNodes: 2);
        PathQuery queryB = Query(end, start, maxExpandedNodes: 2);
        PathQuery queryC = Query(start, end, maxExpandedNodes: 2, maxEvaluatedEdges: 7);
        NavigationAStarPayload payloadA = RunSingle(gate, queryA);
        NavigationAStarPayload payloadB = RunSingle(gate, queryB);
        var reversed = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(20, queryB),
                new PathQueryBatchItem(10, queryA)
            },
            count: 2);

        gate.Begin(reversed, out NavigationAStarBatchWork created)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork work = created;
        using (work)
        {
            DrainAdmission(work);
            work.PublishReadyPrefix(maximumCount: 2).Should().Be(2);
            NavigationAStarPayloadLease resultB = work.TakeResult(inputIndex: 0);
            NavigationAStarPayloadLease resultA = work.TakeResult(inputIndex: 1);
            resultA.Payload.Should().BeSameAs(payloadA);
            resultB.Payload.Should().BeSameAs(payloadB);
            resultA.Dispose();
            resultB.Dispose();
        }

        NavigationAStarPayload payloadC = RunSingle(gate, queryC);

        gate.PayloadCache.TryCheckout(payloadA.Key, graph, out _).Should().BeFalse(
            "ordinal A was touched before B and is the one forced LRU eviction");
        gate.PayloadCache.TryCheckout(
                payloadB.Key,
                graph,
                out NavigationAStarPayloadLease retainedB)
            .Should().BeTrue();
        retainedB.Dispose();
        gate.PayloadCache.TryCheckout(
                payloadC.Key,
                graph,
                out NavigationAStarPayloadLease retainedC)
            .Should().BeTrue();
        retainedC.Dispose();
    }

    private static void DrainAdmission(NavigationAStarBatchWork work)
    {
        for (int step = 0; step < 64 && !work.IsAdmissionComplete; step++)
            work.AdvanceAdmission(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        work.IsAdmissionComplete.Should().BeTrue();
    }

    private static NavigationAStarAdmissionGate CreateGate(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationQueryLimits limits) => new(
            world,
            store,
            limits,
            new NavigationQueryAdmissionCoordinator(
                limits.MaxConcurrentNavigationQueries));

    private static NavigationAStarAdmissionGate CreateGate(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationQueryLimits limits,
        NavigationQueryAdmissionCoordinator coordinator) => new(
            world,
            store,
            limits,
            coordinator);

    private static NavigationQueryLimits CreateLimits(
        int maxBatchItems = 8,
        int maxConcurrentQueries = 8,
        long maxBatchDescriptorBytes = 16_384,
        long maxSinglePayloadBytes = 2_048,
        long maxActivePayloadBytes = 16_384,
        int maxActivePayloadLeases = 8) => new(
            maxBatchItems,
            maxBatchDescriptorBytes,
            maxConcurrentNavigationQueries: maxConcurrentQueries,
            aStarWorkspaceMapCapacity: 0,
            aStarWorkspaceEndpointPageCapacity: 0,
            aStarWorkspaceNodeCapacity: 8,
            maxAStarCacheEntries: 8,
            maxAStarReusablePayloadBytes: 16_384,
            maxAStarSinglePayloadBytes: maxSinglePayloadBytes,
            maxAStarActivePayloadBytes: maxActivePayloadBytes,
            maxAStarActivePayloadLeases: maxActivePayloadLeases,
            aStarWorkspaceComponentCapacity: 2,
            flowWorkspaceMapCapacity: 0,
            flowWorkspaceEndpointPageCapacity: 0,
            flowWorkspaceComponentCapacity: 2,
            flowWorkspaceNodeCapacity: 8,
            rayWorkspaceCoveredAddressCapacity: 8,
            rayWorkspaceTraceIntervalCapacity: 8,
            aStarWorkspaceGuidePointCapacity: 8,
            maxFlowCacheEntries: 8,
            maxFlowReusablePayloadBytes: 16_384,
            maxFlowSinglePayloadBytes: maxSinglePayloadBytes,
            maxFlowActivePayloadBytes: maxActivePayloadBytes,
            maxFlowActivePayloadLeases: maxActivePayloadLeases);

    private static PathQuery Query(int maxExpandedNodes) => new(
        new NavigationEndpoint(Vector3d.Zero),
        new NavigationEndpoint(Vector3d.One),
        new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
            maxStepUp: Fixed64.Zero,
            maxDropDown: Fixed64.Zero,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None),
        Policy.Key,
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(
            maxLookupProbes: 64,
            maxEndpointCandidates: 4,
            maxExpandedNodes,
            maxEvaluatedEdges: 8,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: 0,
            maxSimplificationRays: 0),
        allowTransitions: false);

    private static NavigationAStarPayload RunCompletionOrder(
        GridWorld world,
        NavigationWorldGraphStore store,
        Vector3d start,
        Vector3d end,
        bool reverseCompletion)
    {
        using var gate = CreateGate(
            world,
            store,
            new NavigationQueryLimits(
                maxBatchItems: 2,
                maxBatchDescriptorBytes: 1_024,
                maxConcurrentNavigationQueries: 2,
                aStarWorkspaceMapCapacity: 1,
                aStarWorkspaceEndpointPageCapacity: 2,
                aStarWorkspaceNodeCapacity: 2,
                maxAStarCacheEntries: 1,
                maxAStarReusablePayloadBytes: 2_048,
                maxAStarSinglePayloadBytes: 1_024,
                maxAStarActivePayloadBytes: 2_048,
                maxAStarActivePayloadLeases: 2,
                aStarWorkspaceComponentCapacity: 4,
                flowWorkspaceMapCapacity: 1,
                flowWorkspaceEndpointPageCapacity: 2,
                flowWorkspaceComponentCapacity: 4,
                flowWorkspaceNodeCapacity: 2,
                rayWorkspaceCoveredAddressCapacity: 2,
                rayWorkspaceTraceIntervalCapacity: 2,
                aStarWorkspaceGuidePointCapacity: 3,
                maxFlowCacheEntries: 1,
                maxFlowReusablePayloadBytes: 4_096,
                maxFlowSinglePayloadBytes: 1_024,
                maxFlowActivePayloadBytes: 2_048,
                maxFlowActivePayloadLeases: 2));
        PathQuery query = Query(start, end, maxExpandedNodes: 2);
        var batch = new PathQueryBatch(
            new[]
            {
                new PathQueryBatchItem(20, query),
                new PathQueryBatchItem(10, query)
            },
            count: 2);
        gate.Begin(batch, out NavigationAStarBatchWork created)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork work = created;
        using (work)
        {
            DrainAdmission(work);
            int firstInput = reverseCompletion ? 0 : 1;
            int secondInput = reverseCompletion ? 1 : 0;
            DrainSearch(work, firstInput);
            if (reverseCompletion)
            {
                work.PublishReadyPrefix(maximumCount: 2).Should().Be(0,
                    "a completed higher ordinal cannot publish before the lower ordinal");
                work.GetStatus(inputIndex: 0).Should().Be(NavigationAStarQueryStatus.Pending);
            }
            else
            {
                work.PublishReadyPrefix(maximumCount: 2).Should().Be(1);
            }
            DrainSearch(work, secondInput);
            work.PublishReadyPrefix(maximumCount: 2).Should().Be(reverseCompletion ? 2 : 1);
            work.GetStatus(inputIndex: 0).Should().Be(NavigationAStarQueryStatus.Success);
            work.GetStatus(inputIndex: 1).Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease first = work.TakeResult(inputIndex: 0);
            NavigationAStarPayloadLease second = work.TakeResult(inputIndex: 1);
            try
            {
                ReferenceEquals(first.Payload, second.Payload).Should().BeTrue(
                    "ordinal publication must converge same-key workers on one canonical cache payload");
                return first.Payload;
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }
    }

    private static NavigationAStarPayload RunSingle(
        NavigationAStarAdmissionGate gate,
        PathQuery query)
    {
        gate.Begin(query, out NavigationAStarBatchWork created)
            .Should().Be(NavigationAStarQueryStatus.Pending);
        NavigationAStarBatchWork work = created;
        using (work)
        {
            DrainAdmission(work);
            if (!work.IsReadyToPublish(inputIndex: 0))
                DrainSearch(work, inputIndex: 0);
            work.PublishReadyPrefix(maximumCount: 1).Should().Be(1);
            work.GetStatus(inputIndex: 0).Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease result = work.TakeResult(inputIndex: 0);
            try
            {
                return result.Payload;
            }
            finally
            {
                result.Dispose();
            }
        }
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
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        cache.TryReservePayload(maximumBytes, out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        var work = new NavigationAStarQueryWork(world, store, workspace, cache);
        work.BeginReserved(query, lease!, ref reservation);
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return work;
    }

    private static void DrainQuery(NavigationAStarQueryWork work)
    {
        for (int step = 0; step < 64 && !work.IsPrepared; step++)
            work.PrepareSearchOrCheckout(1, 1);
        for (int step = 0; step < 64 && !work.IsReadyToPublish; step++)
            work.AdvanceSearch(1, 1, 1, 1);
        if (work.IsReadyToPublish)
            work.Publish();
    }

    private static void DrainSearch(NavigationAStarBatchWork work, int inputIndex)
    {
        for (int step = 0; step < 64 && !work.IsReadyToPublish(inputIndex); step++)
            work.AdvanceSearch(inputIndex, 1, 1, 1, 1);
        work.IsReadyToPublish(inputIndex).Should().BeTrue();
    }

    private static PathQuery Query(
        Vector3d start,
        Vector3d end,
        int maxExpandedNodes,
        int maxEvaluatedEdges = 8)
    {
        PathQuery template = Query(maxExpandedNodes);
        return new PathQuery(
            new NavigationEndpoint(start),
            new NavigationEndpoint(end),
            template.Agent,
            template.AreaPolicy,
            template.Traversal,
            template.Algorithm,
            new NavigationWorkBudget(
                template.Budget.MaxLookupProbes,
                template.Budget.MaxEndpointCandidates,
                template.Budget.MaxExpandedNodes,
                maxEvaluatedEdges,
                template.Budget.MaxConnectionLegs,
                template.Budget.MaxTransitionCandidates,
                template.Budget.MaxTransitionPairs,
                template.Budget.MaxStagedLegAttempts,
                template.Budget.MaxTraceIntervals,
                template.Budget.MaxCoveredVoxelIntervals,
                template.Budget.MaxSimplificationRays),
            template.AllowTransitions);
    }

    private static NavigationWorldGraph CreateLineGraph(
        GridWorld world,
        out Vector3d start,
        out Vector3d end)
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)2),
            storageKind: GridStorageKind.Sparse);
        var endIndex = new VoxelIndex(1, 0, 0);
        world.TryAddGrid(configuration, new[] { default(VoxelIndex), endIndex }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, Cell)
            .AddCell(endIndex, Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        binding.TryGetCellPrism(default, out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(endIndex, out GridCellPrism endPrism).Should().BeTrue();
        start = new Vector3d(startPrism.Center.X, startPrism.VerticalMin, startPrism.Center.Z);
        end = new Vector3d(endPrism.Center.X, endPrism.VerticalMin, endPrism.Center.Z);
        var graph = new NavigationWorldGraph(
            1,
            new[] { instance },
            areaCatalog: catalog);
        return graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
    }

    private static NavigationWorldGraphStore CreateStore(
        int maxConcurrentLeases) =>
        CreateStore(NavigationWorldGraph.Empty, maxConcurrentLeases);

    private static NavigationWorldGraphStore CreateStore(
        NavigationWorldGraph graph,
        int maxConcurrentLeases)
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases);
        if (!ReferenceEquals(graph, NavigationWorldGraph.Empty))
        {
            store.TryPublish(graph)
                .Should().Be(NavigationCandidatePublication.Published);
        }
        return store;
    }
}
