using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures stable-ordinal same-key Flow publication on persistent workers.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Flow", "Contention")]
public class NavigationFlowFieldContentionBenchmarks
{
    private const int CorridorLength = 32;
    private BenchmarkPathFixture _fixture;
    private NavigationFlowAdmissionGate _gate;
    private PathQuery _farQuery;
    private PathQuery _nearQuery;
    private PathQueryBatchItem[] _items;
    private NavigationFlowQueryResult[] _results;
    private Thread[] _workers;
    private AutoResetEvent[] _starts;
    private AutoResetEvent[] _completed;
    private Exception[] _failures;
    private NavigationFlowBatchWork _activeWork;
    private int _phase;
    private int _stopping;
    private long _reverseCompletions;
    private long _duplicateDiscards;
    private long _workerAllocatedBytes;
    private int _nearPayloads;
    private int _farPayloads;
    private int _incompatiblePayloads;

    /// <summary>Contention family and persistent worker count.</summary>
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; }

    /// <summary>Supported same-key and near/far contention cases.</summary>
    public IEnumerable<string> Cases => new[]
    {
        "SameKey:1",
        "SameKey:2",
        "SameKey:4",
        "SameKey:8",
        "NearFar:2",
        "NearFar:4",
        "NearFar:8"
    };

    private int WorkerCount => int.Parse(Case.Substring(Case.IndexOf(':') + 1));

    private bool IsNearFar => Case.StartsWith("NearFar", StringComparison.Ordinal);

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(CorridorLength, 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                CorridorLength,
                WorkerCount,
                cacheEntries: 1_024));
        PathQuery published = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"flow-contention-{WorkerCount}",
            CorridorLength,
            1,
            NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength));
        _farQuery = NavigationGraphBenchmarkScenario.ToFlow(published);
        _nearQuery = NavigationGraphBenchmarkScenario.WithStart(
            _farQuery,
            configuration,
            new GridForge.Spatial.VoxelIndex(CorridorLength - 8, 0, 0));
        _gate = _fixture.Context.Pathing.NavigationFlowAdmissionGate;
        _items = new PathQueryBatchItem[WorkerCount];
        _results = new NavigationFlowQueryResult[WorkerCount];
        _workers = new Thread[WorkerCount];
        _starts = new AutoResetEvent[WorkerCount];
        _completed = new AutoResetEvent[WorkerCount];
        _failures = new Exception[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            int inputIndex = i;
            _starts[i] = new AutoResetEvent(false);
            _completed[i] = new AutoResetEvent(false);
            _workers[i] = new Thread(() => RunWorker(inputIndex))
            {
                IsBackground = true,
                Name = $"Trailblazer Flow contention worker {i}"
            };
            _workers[i].Start();
        }
        long preflight = SameKeyReversedCompletion();
        if (preflight == 0
            || (!IsNearFar && _duplicateDiscards != WorkerCount - 1)
            || (IsNearFar && (_nearPayloads == 0
                || _farPayloads == 0
                || _incompatiblePayloads != 0)))
        {
            throw new InvalidOperationException(
                $"Flow contention preflight for {Case}: signal={preflight}, "
                + $"duplicate_discards={_duplicateDiscards}/{(IsNearFar ? -1 : WorkerCount - 1)}, "
                + $"near_payloads={_nearPayloads}, far_payloads={_farPayloads}, "
                + $"incompatible_payloads={_incompatiblePayloads}/0.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Volatile.Write(ref _stopping, 1);
        for (int i = 0; i < WorkerCount; i++)
            _starts[i]?.Set();
        for (int i = 0; i < WorkerCount; i++)
        {
            _workers[i]?.Join();
            _starts[i]?.Dispose();
            _completed[i]?.Dispose();
        }
        NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
        Console.WriteLine(
            $"NAVIGATION_FLOW_CONTENTION case={Case} workers={WorkerCount} "
            + $"reverse_completions={_reverseCompletions} "
            + $"duplicate_discards={_duplicateDiscards} worker_allocated_bytes={_workerAllocatedBytes} "
            + $"near_payloads={_nearPayloads} far_payloads={_farPayloads} "
            + $"incompatible_payloads={_incompatiblePayloads} "
            + $"cached_payload_bytes={cache.CachedBytes} detached_bytes={cache.DetachedBytes} "
            + $"reserved_bytes={cache.ReservedPayloadBytes} reserved_leases={cache.ReservedLeaseCount} "
            + $"active_leases={cache.ActiveLeaseCount} cache_entries={cache.Count}");
        _fixture?.Teardown();
    }

    /// <summary>Completes higher ordinals first and publishes one canonical same-key payload.</summary>
    [Benchmark]
    public long SameKeyReversedCompletion()
    {
        _reverseCompletions = 0;
        _duplicateDiscards = 0;
        _workerAllocatedBytes = 0;
        _nearPayloads = 0;
        _farPayloads = 0;
        _incompatiblePayloads = 0;
        for (int i = 0; i < WorkerCount; i++)
        {
            PathQuery query = IsNearFar && (i & 1) != 0 ? _nearQuery : _farQuery;
            _items[i] = new PathQueryBatchItem(WorkerCount - i, query);
            _failures[i] = null;
            _results[i] = default;
        }
        var batch = new PathQueryBatch(_items, WorkerCount);
        if (_gate.Begin(batch, out NavigationFlowBatchWork work) != NavigationFlowQueryStatus.Pending)
            throw new InvalidOperationException("Flow contention admission was rejected.");
        _activeWork = work;
        try
        {
            work.AdvanceAdmission(int.MaxValue, int.MaxValue);
            if (!work.IsAdmissionComplete)
                throw new InvalidOperationException("Flow contention admission did not complete.");
            Volatile.Write(ref _phase, 1);
            SignalAllAndWait();
            ThrowFailures();
            Volatile.Write(ref _phase, 2);
            for (int inputIndex = 0; inputIndex < WorkerCount; inputIndex++)
            {
                _starts[inputIndex].Set();
                _completed[inputIndex].WaitOne();
                ThrowFailure(inputIndex);
                if (!work.IsReadyToPublish(inputIndex))
                    throw new InvalidOperationException($"Flow worker {inputIndex} did not finish.");
                _reverseCompletions++;
                if (inputIndex + 1 < WorkerCount && work.PublishReadyPrefix(WorkerCount) != 0)
                    throw new InvalidOperationException("A higher Flow ordinal published early.");
            }
            if (work.PublishReadyPrefix(WorkerCount) != WorkerCount)
                throw new InvalidOperationException("Flow contention did not publish the full prefix.");
            NavigationFlowFieldPayload canonical = null;
            int duplicates = 0;
            for (int i = 0; i < WorkerCount; i++)
            {
                if (work.GetStatus(i) != NavigationFlowQueryStatus.Success)
                    throw new InvalidOperationException($"Flow worker {i} failed with {work.GetStatus(i)}.");
                NavigationFlowQueryResult result = work.TakeResult(i);
                _results[i] = result;
                if (result.PayloadLease.TryGetPayload(out NavigationFlowFieldPayload payload)
                    != NavigationFlowFieldStatus.Success)
                {
                    throw new InvalidOperationException($"Flow worker {i} returned a stale payload.");
                }
                if (canonical == null || payload.Nodes.Length > canonical.Nodes.Length)
                    canonical = payload;
            }
            for (int i = 0; i < WorkerCount; i++)
            {
                _results[i].PayloadLease.TryGetPayload(out NavigationFlowFieldPayload payload);
                if (ReferenceEquals(canonical, payload))
                    duplicates++;
                if (payload.Nodes.Length == canonical.Nodes.Length)
                    _farPayloads++;
                else if (NavigationGraphBenchmarkScenario.IsStrictPrefix(payload, canonical))
                    _nearPayloads++;
                else
                    _incompatiblePayloads++;
            }
            _duplicateDiscards = Math.Max(0, duplicates - 1);
            if ((!IsNearFar && _duplicateDiscards != WorkerCount - 1)
                || (IsNearFar && (_nearPayloads == 0
                    || _farPayloads == 0
                    || _incompatiblePayloads != 0)))
            {
                throw new InvalidOperationException(
                    $"Flow contention convergence failed for {Case}: "
                    + $"duplicate_discards={_duplicateDiscards}, near_payloads={_nearPayloads}, "
                    + $"far_payloads={_farPayloads}, incompatible_payloads={_incompatiblePayloads}.");
            }
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            if (cache.Count != 1 || cache.ActiveLeaseCount != WorkerCount)
            {
                throw new InvalidOperationException(
                    $"Flow contention retained state for {Case}: "
                    + $"cache_entries={cache.Count}/1, active_leases={cache.ActiveLeaseCount}/{WorkerCount}.");
            }
            for (int i = 0; i < WorkerCount; i++)
            {
                _results[i].Dispose();
                _results[i] = default;
            }
            cache.Reset();
            if (cache.ActiveLeaseCount != 0
                || cache.LeasedBytes != 0
                || cache.DetachedBytes != 0
                || cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0)
            {
                throw new InvalidOperationException("Flow contention leases or reservations did not drain.");
            }
            return canonical.Nodes.Length + _duplicateDiscards + _nearPayloads + 1L;
        }
        finally
        {
            for (int i = 0; i < WorkerCount; i++)
            {
                _results[i].Dispose();
                _results[i] = default;
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
            long before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                int phase = Volatile.Read(ref _phase);
                _activeWork.AdvanceSearch(
                    inputIndex,
                    int.MaxValue,
                    phase == 1
                        ? (IsNearFar ? 7 : CorridorLength - 1)
                        : CorridorLength * 4,
                    int.MaxValue,
                    0);
            }
            catch (Exception exception)
            {
                _failures[inputIndex] = exception;
            }
            finally
            {
                Interlocked.Add(
                    ref _workerAllocatedBytes,
                    GC.GetAllocatedBytesForCurrentThread() - before);
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

    private void ThrowFailures()
    {
        for (int i = 0; i < WorkerCount; i++)
            ThrowFailure(i);
    }

    private void ThrowFailure(int inputIndex)
    {
        if (_failures[inputIndex] != null)
            throw new InvalidOperationException($"Flow worker {inputIndex} failed.", _failures[inputIndex]);
    }
}
