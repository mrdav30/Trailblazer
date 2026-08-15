//=======================================================================
// NavigationAStarContentionBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures same-key A* search and cache publication with persistent manual workers.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase34", "Graph", "AStar", "Contention")]
public class NavigationAStarContentionBenchmarks
{
    private const int CorridorLength = 32;

    private BenchmarkPathFixture _fixture;
    private NavigationAStarAdmissionGate _gate;
    private PathQuery _template;
    private PathQueryBatchItem[] _items;
    private NavigationAStarPayloadLease[] _results;
    private Thread[] _workers;
    private AutoResetEvent[] _starts;
    private AutoResetEvent[] _completed;
    private Exception[] _workerFailures;
    private long[] _workerAllocatedBytes;
    private NavigationAStarBatchWork _activeWork;
    private int _workerPhase;
    private int _stopping;
    private int _querySequence;
    private long _maximumReservedPayloadBytes;
    private int _maximumReservedLeaseCount;
    private int _maximumActivePayloadLeases;
    private long _maximumLeasedPayloadBytes;
    private long _maximumDetachedPayloadBytes;
    private long _maximumCachedPayloadBytes;
    private int _maximumGraphActiveLeases;
    private int _maximumGraphActiveGenerations;
    private int _maximumGraphRetiredGenerations;
    private long _maximumGraphRetiredBytes;
    private long _workspaceBytesPerWorker;
    private long _maximumActiveWorkspaceBytes;
    private long _retainedWorkspaceBytes;
    private long _maximumAggregateResultPayloadBytes;
    private long _maximumDuplicateDiscardPayloadBytes;
    private long _maximumWorkerThreadAllocatedBytes = -1;
    private long _reverseCompletions;
    private long _duplicateDiscards;

    /// <summary>Number of admitted real search workers.</summary>
    [Params(1, 2, 4, 8)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(CorridorLength, length: 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                CorridorLength,
                WorkerCount,
                cacheEntries: 1_024));
        _template = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"astar-contention-{WorkerCount}",
            CorridorLength,
            length: 1,
            NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength));
        _gate = _fixture.Context.Pathing.NavigationAStarAdmissionGate;
        long workspaceBefore = GC.GetAllocatedBytesForCurrentThread();
        var workspaceProbe = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: NavigationGraphBenchmarkScenario.GetPageCapacity(CorridorLength),
            componentCapacity: checked(
                NavigationGraphBenchmarkScenario.GetPageCapacity(CorridorLength) + 2),
            nodeCapacity: CorridorLength);
        _workspaceBytesPerWorker = GC.GetAllocatedBytesForCurrentThread() - workspaceBefore;
        GC.KeepAlive(workspaceProbe);
        _retainedWorkspaceBytes = checked(_workspaceBytesPerWorker * WorkerCount);
        _items = new PathQueryBatchItem[WorkerCount];
        _results = new NavigationAStarPayloadLease[WorkerCount];
        _workers = new Thread[WorkerCount];
        _starts = new AutoResetEvent[WorkerCount];
        _completed = new AutoResetEvent[WorkerCount];
        _workerFailures = new Exception[WorkerCount];
        _workerAllocatedBytes = new long[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            int inputIndex = i;
            _starts[i] = new AutoResetEvent(false);
            _completed[i] = new AutoResetEvent(false);
            _workers[i] = new Thread(() => RunWorker(inputIndex))
            {
                IsBackground = true,
                Name = $"Trailblazer A* contention worker {i}"
            };
            _workers[i].Start();
        }

        long preflight = SameKeyReversedCompletion();
        if (preflight == 0
            || _duplicateDiscards != WorkerCount - 1
            || _workspaceBytesPerWorker <= 0
            || _maximumActiveWorkspaceBytes != _retainedWorkspaceBytes
            || _retainedWorkspaceBytes <= 0
            || _maximumAggregateResultPayloadBytes
                != checked(_maximumLeasedPayloadBytes * WorkerCount)
            || _maximumDuplicateDiscardPayloadBytes
                != _maximumAggregateResultPayloadBytes - _maximumLeasedPayloadBytes
            || _maximumWorkerThreadAllocatedBytes < 0)
        {
            throw new InvalidOperationException(
                $"A* contention preflight failed for {WorkerCount} workers.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Volatile.Write(ref _stopping, 1);
        if (_starts != null)
        {
            for (int i = 0; i < _starts.Length; i++)
                _starts[i]?.Set();
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i]?.Join();
                _starts[i]?.Dispose();
                _completed[i]?.Dispose();
            }
        }
        Console.WriteLine(
            $"PHASE34_ASTAR_CONTENTION workers={WorkerCount} "
            + $"reverse_completions={_reverseCompletions} duplicate_discards={_duplicateDiscards} "
            + $"reserved_leases={_maximumReservedLeaseCount} "
            + $"reserved_payload_bytes={_maximumReservedPayloadBytes} "
            + $"active_payload_leases={_maximumActivePayloadLeases} "
            + $"leased_payload_bytes={_maximumLeasedPayloadBytes} "
            + $"detached_payload_bytes={_maximumDetachedPayloadBytes} "
            + $"cached_payload_bytes={_maximumCachedPayloadBytes} "
            + $"graph_active_leases={_maximumGraphActiveLeases} "
            + $"graph_active_generations={_maximumGraphActiveGenerations} "
            + $"graph_retired_generations={_maximumGraphRetiredGenerations} "
            + $"graph_retired_bytes={_maximumGraphRetiredBytes} "
            + $"workspace_bytes_per_worker={_workspaceBytesPerWorker} "
            + $"active_workspace_bytes={_maximumActiveWorkspaceBytes} "
            + $"retained_workspace_bytes={_retainedWorkspaceBytes} "
            + $"aggregate_result_payload_bytes={_maximumAggregateResultPayloadBytes} "
            + $"duplicate_discard_payload_bytes={_maximumDuplicateDiscardPayloadBytes} "
            + $"worker_thread_allocated_bytes={_maximumWorkerThreadAllocatedBytes}");
        _fixture?.Teardown();
    }

    /// <summary>Runs concurrent same-key search, reversed completion, and canonical cache publication.</summary>
    [Benchmark]
    public long SameKeyReversedCompletion()
    {
        PathQuery query = WithEdgeSlack(_template, checked(++_querySequence));
        for (int i = 0; i < WorkerCount; i++)
        {
            _items[i] = new PathQueryBatchItem(WorkerCount - i, query);
            _workerFailures[i] = null;
            _workerAllocatedBytes[i] = 0;
            _results[i] = null;
        }

        var batch = new PathQueryBatch(_items, WorkerCount);
        NavigationAStarQueryStatus begin = _gate.Begin(batch, out NavigationAStarBatchWork work);
        if (begin != NavigationAStarQueryStatus.Pending)
            throw new InvalidOperationException($"A* contention admission failed with {begin}.");
        if (work.AdmittedCount != WorkerCount)
            throw new InvalidOperationException("A* contention did not admit every configured worker.");
        _maximumActiveWorkspaceBytes = Math.Max(
            _maximumActiveWorkspaceBytes,
            checked(_workspaceBytesPerWorker * work.AdmittedCount));
        _activeWork = work;
        try
        {
            RecordAccounting();
            work.AdvanceAdmission(int.MaxValue, int.MaxValue);
            if (!work.IsAdmissionComplete)
                throw new InvalidOperationException("A* contention endpoint admission did not complete.");

            Volatile.Write(ref _workerPhase, 1);
            SignalAllAndWait();
            ThrowWorkerFailures();
            for (int i = 0; i < WorkerCount; i++)
            {
                if (work.IsReadyToPublish(i))
                    throw new InvalidOperationException("A* contention prework completed a worker too early.");
            }

            Volatile.Write(ref _workerPhase, 2);
            for (int inputIndex = 0; inputIndex < WorkerCount; inputIndex++)
            {
                _starts[inputIndex].Set();
                _completed[inputIndex].WaitOne();
                ThrowWorkerFailure(inputIndex);
                if (!work.IsReadyToPublish(inputIndex))
                    throw new InvalidOperationException("A* contention worker did not reach publication readiness.");
                _reverseCompletions++;
                if (inputIndex + 1 < WorkerCount
                    && work.PublishReadyPrefix(WorkerCount) != 0)
                {
                    throw new InvalidOperationException(
                        "Higher-ordinal completion published before the lower ordinal.");
                }
            }
            RecordWorkerAllocation();

            if (work.PublishReadyPrefix(WorkerCount) != WorkerCount)
                throw new InvalidOperationException("A* contention did not publish its complete canonical prefix.");
            NavigationAStarPayload canonical = null;
            int duplicates = 0;
            long aggregateResultPayloadBytes = 0;
            for (int i = 0; i < WorkerCount; i++)
            {
                if (work.GetStatus(i) != NavigationAStarQueryStatus.Success)
                    throw new InvalidOperationException($"A* contention input {i} did not succeed.");
                NavigationAStarPayloadLease result = work.TakeResult(i);
                _results[i] = result;
                aggregateResultPayloadBytes = checked(
                    aggregateResultPayloadBytes + result.Payload.RetainedBytes);
                if (canonical == null)
                    canonical = result.Payload;
                else if (ReferenceEquals(canonical, result.Payload))
                    duplicates++;
            }
            if (duplicates != WorkerCount - 1)
                throw new InvalidOperationException("Same-key workers did not converge on one cached payload.");
            long duplicateDiscardPayloadBytes = checked(
                aggregateResultPayloadBytes - canonical.RetainedBytes);
            if (duplicateDiscardPayloadBytes
                != checked(canonical.RetainedBytes * duplicates))
            {
                throw new InvalidOperationException(
                    "A* contention duplicate-result payload bytes are inconsistent.");
            }
            _maximumAggregateResultPayloadBytes = Math.Max(
                _maximumAggregateResultPayloadBytes,
                aggregateResultPayloadBytes);
            _maximumDuplicateDiscardPayloadBytes = Math.Max(
                _maximumDuplicateDiscardPayloadBytes,
                duplicateDiscardPayloadBytes);
            _duplicateDiscards += duplicates;
            RecordAccounting();
            NavigationAStarPayloadCache cache = _gate.PayloadCache;
            NavigationWorldGraphStore store = _fixture.Context.Pathing.NavigationGraphStore;
            if (cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0
                || cache.ActiveLeaseCount != WorkerCount
                || cache.LeasedBytes != canonical.RetainedBytes
                || cache.DetachedBytes != 0
                || store.ActiveLeaseCount != 0
                || store.RetiredGenerationCount != 0
                || store.RetiredBytes != 0)
            {
                throw new InvalidOperationException("A* contention active/retired accounting is inconsistent.");
            }

            for (int i = 0; i < WorkerCount; i++)
            {
                _results[i].Dispose();
                _results[i] = null;
            }
            RecordAccounting();
            if (cache.ActiveLeaseCount != 0 || cache.LeasedBytes != 0)
                throw new InvalidOperationException("A* contention result leases were not released.");
            return canonical.Nodes.Length + duplicates + 1L;
        }
        finally
        {
            for (int i = 0; i < WorkerCount; i++)
            {
                _results[i]?.Dispose();
                _results[i] = null;
            }
            work.Dispose();
            _activeWork = default;
        }
    }

    private void RunWorker(int inputIndex)
    {
        while (true)
        {
            _starts[inputIndex].WaitOne();
            if (Volatile.Read(ref _stopping) != 0)
                return;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                int phase = Volatile.Read(ref _workerPhase);
                _activeWork.AdvanceSearch(
                    inputIndex,
                    lookupStepLimit: int.MaxValue,
                    nodeStepLimit: phase == 1 ? CorridorLength - 1 : (CorridorLength * 3) + 1,
                    edgeStepLimit: int.MaxValue,
                    connectionStepLimit: 0);
            }
            catch (Exception exception)
            {
                _workerFailures[inputIndex] = exception;
            }
            finally
            {
                _workerAllocatedBytes[inputIndex] = checked(
                    _workerAllocatedBytes[inputIndex]
                    + GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore);
                _completed[inputIndex].Set();
            }
        }
    }

    private void SignalAllAndWait()
    {
        for (int i = 0; i < WorkerCount; i++)
            _starts[i].Set();
        for (int i = 0; i < WorkerCount; i++)
            _completed[i].WaitOne();
    }

    private void ThrowWorkerFailures()
    {
        for (int i = 0; i < WorkerCount; i++)
            ThrowWorkerFailure(i);
    }

    private void ThrowWorkerFailure(int inputIndex)
    {
        Exception failure = _workerFailures[inputIndex];
        if (failure != null)
            throw new InvalidOperationException($"A* contention worker {inputIndex} failed.", failure);
    }

    private void RecordWorkerAllocation()
    {
        long allocatedBytes = 0;
        for (int i = 0; i < WorkerCount; i++)
            allocatedBytes = checked(allocatedBytes + _workerAllocatedBytes[i]);
        _maximumWorkerThreadAllocatedBytes = Math.Max(
            _maximumWorkerThreadAllocatedBytes,
            allocatedBytes);
    }

    private void RecordAccounting()
    {
        NavigationAStarPayloadCache cache = _gate.PayloadCache;
        NavigationWorldGraphStore store = _fixture.Context.Pathing.NavigationGraphStore;
        _maximumReservedPayloadBytes = Math.Max(
            _maximumReservedPayloadBytes,
            cache.ReservedPayloadBytes);
        _maximumReservedLeaseCount = Math.Max(
            _maximumReservedLeaseCount,
            cache.ReservedLeaseCount);
        _maximumActivePayloadLeases = Math.Max(
            _maximumActivePayloadLeases,
            cache.ActiveLeaseCount);
        _maximumLeasedPayloadBytes = Math.Max(
            _maximumLeasedPayloadBytes,
            cache.LeasedBytes);
        _maximumDetachedPayloadBytes = Math.Max(
            _maximumDetachedPayloadBytes,
            cache.DetachedBytes);
        _maximumCachedPayloadBytes = Math.Max(
            _maximumCachedPayloadBytes,
            cache.CachedBytes);
        _maximumGraphActiveLeases = Math.Max(
            _maximumGraphActiveLeases,
            store.ActiveLeaseCount);
        _maximumGraphActiveGenerations = Math.Max(
            _maximumGraphActiveGenerations,
            store.ActiveGenerationCount);
        _maximumGraphRetiredGenerations = Math.Max(
            _maximumGraphRetiredGenerations,
            store.RetiredGenerationCount);
        _maximumGraphRetiredBytes = Math.Max(
            _maximumGraphRetiredBytes,
            store.RetiredBytes);
    }

    private static PathQuery WithEdgeSlack(PathQuery query, int edgeSlack) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        query.Algorithm,
        NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength, edgeSlack),
        query.AllowTransitions,
        query.FlowField);
}
