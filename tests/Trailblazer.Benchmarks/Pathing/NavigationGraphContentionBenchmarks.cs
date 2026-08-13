//=======================================================================
// NavigationGraphContentionBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures pinned-root pressure and active query-admission contention at the frozen 1/2/4/8-reader gate.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase2", "Graph", "Contention")]
public class NavigationGraphContentionBenchmarks
{
    private GridWorld _world;
    private NavigationGraphRuntime _runtime;
    private VoxelGrid _grid;
    private Voxel _voxel;
    private ObstacleToken _obstacle;
    private Thread[] _readers;
    private AutoResetEvent[] _readerSignals;
    private NavigationQueryAdmissionRequest _queryRequest;
    private int _activeReaderCount;
    private int _readerMode;
    private int _generation;
    private int _ready;
    private int _completed;
    private int _releasedGeneration;
    private int _completedGeneration;
    private int _completedTarget;
    private int _stopping;
    private int _frame;
    private bool _blocked;
    private int _maximumActiveSnapshotCount;
    private int _maximumActiveLeaseCount;
    private int _maximumRetiredGenerationCount;
    private long _maximumRetiredSnapshotBytes;
    private long _maximumActiveSnapshotBytes;
    private int _maximumPersistentGraphPages;
    private int _maximumRepathWaves;
    private long _minimumWriterVersionAdvance = long.MaxValue;
    private int _writerSamples;
    private int _maximumActiveQueryCount;
    private long _maximumActiveWorkspaceBytes;
    private long _maximumRetainedWorkspaceBytes;
    private long _maximumActiveResultBytes;
    private long _activeAdmissionAttempts;
    private long _activeAdmissionSuccesses;
    private long _activeAdmissionRejections;
    private long _activeLookupCount;
    private long _activeLookupFailures;
    private int _activeAdmissionLeaseCount;
    private int _maximumActiveAdmissionLeaseCount;

    /// <summary>Number of background readers; the active-query case adds one timed foreground admission.</summary>
    [Params(1, 2, 4, 8)]
    public int QueryThreads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld();
        _runtime = new NavigationGraphRuntime(
            _world,
            NavigationGraphLifecycleBenchmarks.CreateSettings());
        _world.OnChangeCommitted += _runtime.EnqueueCommittedChange;
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        _world.TryAddGrid(configuration, out ushort gridIndex);
        configuration.TryNormalize(out NormalizedGridConfiguration binding);
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        _runtime.Admit(new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            1));
        _runtime.Maintain(1);
        _frame = 1;
        _queryRequest = new NavigationQueryAdmissionRequest(0, 4, 64);
        _grid = _world.ActiveGrids[gridIndex];
        _grid.TryGetVoxel(default(VoxelIndex), out _voxel);

        _readers = new Thread[QueryThreads];
        _readerSignals = new AutoResetEvent[QueryThreads];
        for (int i = 0; i < _readers.Length; i++)
        {
            int readerIndex = i;
            _readerSignals[i] = new AutoResetEvent(false);
            _readers[i] = new Thread(() => ReadSnapshots(readerIndex))
            {
                IsBackground = true,
                Name = $"Trailblazer contention reader {i}"
            };
            _readers[i].Start();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Volatile.Write(ref _stopping, 1);
        Volatile.Write(ref _releasedGeneration, int.MaxValue);
        Volatile.Write(ref _completedGeneration, int.MaxValue);
        Interlocked.Increment(ref _generation);
        for (int i = 0; i < _readerSignals.Length; i++)
            _readerSignals[i].Set();
        for (int i = 0; i < _readers.Length; i++)
        {
            _readers[i].Join();
            _readerSignals[i].Dispose();
        }
        Console.WriteLine(
            $"PHASE2_CONTENTION query_threads={QueryThreads} "
            + $"active_snapshots={_maximumActiveSnapshotCount} "
            + $"active_leases={_maximumActiveLeaseCount} "
            + $"retired_generations={_maximumRetiredGenerationCount} "
            + $"retired_bytes={_maximumRetiredSnapshotBytes} "
            + $"active_snapshot_bytes={_maximumActiveSnapshotBytes} "
            + $"persistent_graph_pages={_maximumPersistentGraphPages} "
            + $"active_queries={_maximumActiveQueryCount} "
            + $"active_workspace_bytes={_maximumActiveWorkspaceBytes} "
            + $"retained_workspace_bytes={_maximumRetainedWorkspaceBytes} "
            + $"active_result_bytes={_maximumActiveResultBytes} "
            + $"repath_waves={_maximumRepathWaves} "
            + $"active_admission_attempts={_activeAdmissionAttempts} "
            + $"active_admission_successes={_activeAdmissionSuccesses} "
            + $"active_admission_rejections={_activeAdmissionRejections} "
            + $"active_lookups={_activeLookupCount} "
            + $"active_lookup_failures={_activeLookupFailures} "
            + $"maximum_active_admission_leases={_maximumActiveAdmissionLeaseCount} "
            + $"minimum_writer_version_advance="
            + $"{(_writerSamples == 0 ? 0 : _minimumWriterVersionAdvance)}");
        _world.OnChangeCommitted -= _runtime.EnqueueCommittedChange;
        _runtime.Dispose();
        _world.Dispose();
    }

    [IterationSetup(Target = nameof(PinnedSnapshotQueryWait))]
    public void BeginPinnedQueryPressure() => BeginReaderWave(QueryThreads - 1, ReaderMode.Pinned);

    [IterationCleanup(Target = nameof(PinnedSnapshotQueryWait))]
    public void EndPinnedQueryPressure() => EndReaderWave();

    [IterationSetup(Target = nameof(PinnedSnapshotPublicationWriterWait))]
    public void BeginPinnedWriterPressure() => BeginReaderWave(QueryThreads, ReaderMode.Pinned);

    [IterationCleanup(Target = nameof(PinnedSnapshotPublicationWriterWait))]
    public void EndPinnedWriterPressure()
    {
        RecordDiagnostics();
        EndReaderWave();
        RecordDiagnostics();
    }

    [IterationSetup(Target = nameof(ActiveSnapshotQueryWait))]
    public void BeginActiveQueryContention() => BeginReaderWave(QueryThreads, ReaderMode.Active);

    [IterationCleanup(Target = nameof(ActiveSnapshotQueryWait))]
    public void EndActiveQueryContention() => EndReaderWave(requireExactLookups: true);

    [IterationSetup(Target = nameof(ActiveSnapshotPublicationWriterWait))]
    public void BeginActiveWriterContention() => BeginReaderWave(QueryThreads, ReaderMode.Active);

    [IterationCleanup(Target = nameof(ActiveSnapshotPublicationWriterWait))]
    public void EndActiveWriterContention()
    {
        RecordDiagnostics();
        EndReaderWave(requireExactLookups: true);
        RecordDiagnostics();
    }

    private void RecordDiagnostics()
    {
        NavigationGraphDiagnosticsSnapshot diagnostics = _runtime.GetDiagnostics(0);
        _maximumActiveSnapshotCount = Math.Max(
            _maximumActiveSnapshotCount,
            diagnostics.ActiveSnapshotCount);
        _maximumActiveLeaseCount = Math.Max(
            _maximumActiveLeaseCount,
            diagnostics.ActiveSnapshotLeaseCount);
        _maximumRetiredGenerationCount = Math.Max(
            _maximumRetiredGenerationCount,
            diagnostics.RetiredGenerationCount);
        _maximumRetiredSnapshotBytes = Math.Max(
            _maximumRetiredSnapshotBytes,
            diagnostics.RetiredSnapshotBytes);
        _maximumActiveSnapshotBytes = Math.Max(
            _maximumActiveSnapshotBytes,
            diagnostics.ActiveSnapshotBytes);
        _maximumPersistentGraphPages = Math.Max(
            _maximumPersistentGraphPages,
            diagnostics.PersistentGraphPageCount);
        _maximumActiveQueryCount = Math.Max(
            _maximumActiveQueryCount,
            diagnostics.ActiveQueryCount);
        _maximumActiveWorkspaceBytes = Math.Max(
            _maximumActiveWorkspaceBytes,
            diagnostics.ActiveWorkspaceBytes);
        _maximumRetainedWorkspaceBytes = Math.Max(
            _maximumRetainedWorkspaceBytes,
            diagnostics.RetainedWorkspaceBytes);
        _maximumActiveResultBytes = Math.Max(
            _maximumActiveResultBytes,
            diagnostics.ActiveQueryResultBytes);
        _maximumRepathWaves = Math.Max(
            _maximumRepathWaves,
            _runtime.MaintenanceMeter.CacheInvalidations);
    }

    /// <summary>Measures one admitted snapshot query while older leases pin the requested total query concurrency.</summary>
    [Benchmark(Baseline = true)]
    public long PinnedSnapshotQueryWait()
    {
        if (!_runtime.TryAdmitQuery(_queryRequest, out NavigationQueryAdmissionLease lease)
            || lease == null)
            throw new InvalidOperationException("The pinned query benchmark could not admit its foreground query.");
        using (lease)
        {
            if (!lease.Graph.TryGetMap("map", out _))
                throw new InvalidOperationException("The pinned query benchmark could not resolve its exact MapId.");
            return lease.Graph.GraphVersion;
        }
    }

    /// <summary>Measures one exact physical publication while the requested readers pin the previous root.</summary>
    [Benchmark]
    public long PinnedSnapshotPublicationWriterWait() => PublishPhysicalMutation();

    /// <summary>Measures one admitted snapshot query while the requested background readers continuously admit queries.</summary>
    [Benchmark]
    public long ActiveSnapshotQueryWait()
    {
        var spinner = new SpinWait();
        for (int spin = 0; spin < MaximumSpins; spin++)
        {
            Interlocked.Increment(ref _activeAdmissionAttempts);
            if (!_runtime.TryAdmitQuery(_queryRequest, out NavigationQueryAdmissionLease lease)
                || lease == null)
            {
                Interlocked.Increment(ref _activeAdmissionRejections);
                spinner.SpinOnce();
                continue;
            }

            Interlocked.Increment(ref _activeAdmissionSuccesses);
            using (lease)
            {
                BeginActiveAdmissionLease();
                try
                {
                    if (!lease.Graph.TryGetMap("map", out _))
                    {
                        Interlocked.Increment(ref _activeLookupFailures);
                        throw new InvalidOperationException(
                            "The active query benchmark could not resolve its exact MapId.");
                    }
                    Interlocked.Increment(ref _activeLookupCount);
                    return lease.Graph.GraphVersion;
                }
                finally
                {
                    Interlocked.Decrement(ref _activeAdmissionLeaseCount);
                }
            }
        }

        throw new TimeoutException(
            "The active query benchmark could not admit its foreground query within the deterministic spin ceiling.");
    }

    /// <summary>Measures one exact physical publication while the requested background readers continuously admit queries.</summary>
    [Benchmark]
    public long ActiveSnapshotPublicationWriterWait() => PublishPhysicalMutation();

    private long PublishPhysicalMutation()
    {
        long before = _runtime.Current.GraphVersion;
        if (_blocked)
            _grid.TryRemoveObstacle(_voxel, _obstacle);
        else
        {
            _obstacle = _world.AllocateObstacleToken();
            _grid.TryAddObstacle(_voxel, _obstacle);
        }
        _blocked = !_blocked;
        _runtime.Maintain(++_frame);
        long after = _runtime.Current.GraphVersion;
        long advance = after - before;
        if (advance <= 0)
        {
            throw new InvalidOperationException(
                "The writer benchmark did not publish a new graph generation.");
        }
        _minimumWriterVersionAdvance = Math.Min(_minimumWriterVersionAdvance, advance);
        _writerSamples++;
        return after;
    }

    private void BeginReaderWave(int activeReaderCount, ReaderMode mode)
    {
        Volatile.Write(ref _activeReaderCount, activeReaderCount);
        Volatile.Write(ref _readerMode, (int)mode);
        int readyTarget = Volatile.Read(ref _ready) + activeReaderCount;
        _completedTarget = Volatile.Read(ref _completed) + activeReaderCount;
        int generation = Interlocked.Increment(ref _generation);
        if (mode == ReaderMode.Pinned)
            Volatile.Write(ref _releasedGeneration, generation);
        for (int i = 0; i < activeReaderCount; i++)
            _readerSignals[i].Set();
        WaitFor(ref _ready, readyTarget);
        if (mode == ReaderMode.Active)
            Volatile.Write(ref _releasedGeneration, generation);
    }

    private void EndReaderWave(bool requireExactLookups = false)
    {
        Volatile.Write(ref _completedGeneration, Volatile.Read(ref _generation));
        if ((ReaderMode)Volatile.Read(ref _readerMode) == ReaderMode.Pinned)
        {
            for (int i = 0; i < Volatile.Read(ref _activeReaderCount); i++)
                _readerSignals[i].Set();
        }
        WaitFor(ref _completed, _completedTarget);
        if (requireExactLookups && Volatile.Read(ref _activeLookupFailures) != 0)
        {
            throw new InvalidOperationException(
                "An active contention reader failed its exact MapId lookup.");
        }
    }

    private void ReadSnapshots(int readerIndex)
    {
        var spinner = new SpinWait();
        while (true)
        {
            _readerSignals[readerIndex].WaitOne();
            if (Volatile.Read(ref _stopping) != 0)
                break;
            int next = Volatile.Read(ref _generation);
            spinner.Reset();
            if ((ReaderMode)Volatile.Read(ref _readerMode) == ReaderMode.Active)
            {
                ReadActiveQueries(next, ref spinner);
                continue;
            }
            while (Volatile.Read(ref _releasedGeneration) < next
                && Volatile.Read(ref _stopping) == 0)
            {
                spinner.SpinOnce();
            }
            spinner.Reset();
            NavigationQueryAdmissionLease lease = null;
            while (lease == null && Volatile.Read(ref _stopping) == 0)
            {
                _runtime.TryAdmitQuery(_queryRequest, out lease);
                if (lease == null)
                    spinner.SpinOnce();
            }
            if (lease == null)
                break;
            lease.Graph.TryGetMap("map", out _);
            Interlocked.Increment(ref _ready);
            _readerSignals[readerIndex].WaitOne();
            lease.Dispose();
            Interlocked.Increment(ref _completed);
        }
    }

    private void ReadActiveQueries(int generation, ref SpinWait spinner)
    {
        bool ready = false;
        while (Volatile.Read(ref _completedGeneration) < generation
            && Volatile.Read(ref _stopping) == 0)
        {
            Interlocked.Increment(ref _activeAdmissionAttempts);
            if (!_runtime.TryAdmitQuery(_queryRequest, out NavigationQueryAdmissionLease lease)
                || lease == null)
            {
                Interlocked.Increment(ref _activeAdmissionRejections);
                spinner.SpinOnce();
                continue;
            }

            Interlocked.Increment(ref _activeAdmissionSuccesses);
            using (lease)
            {
                BeginActiveAdmissionLease();
                try
                {
                    if (lease.Graph.TryGetMap("map", out _))
                        Interlocked.Increment(ref _activeLookupCount);
                    else
                        Interlocked.Increment(ref _activeLookupFailures);
                    if (!ready)
                    {
                        ready = true;
                        Interlocked.Increment(ref _ready);
                        while (Volatile.Read(ref _releasedGeneration) < generation
                            && Volatile.Read(ref _stopping) == 0)
                        {
                            spinner.SpinOnce();
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeAdmissionLeaseCount);
                }
            }
            spinner.Reset();
        }
        Interlocked.Increment(ref _completed);
    }

    private void BeginActiveAdmissionLease()
    {
        int active = Interlocked.Increment(ref _activeAdmissionLeaseCount);
        int maximum = Volatile.Read(ref _maximumActiveAdmissionLeaseCount);
        while (active > maximum)
        {
            int observed = Interlocked.CompareExchange(
                ref _maximumActiveAdmissionLeaseCount,
                active,
                maximum);
            if (observed == maximum)
                break;
            maximum = observed;
        }
    }

    private static void WaitFor(ref int value, int target)
    {
        var spinner = new SpinWait();
        for (int spin = 0; Volatile.Read(ref value) < target; spin++)
        {
            if (spin >= MaximumSpins)
            {
                throw new TimeoutException(
                    $"The contention reader wave did not reach {target} within the deterministic spin ceiling.");
            }
            spinner.SpinOnce();
        }
    }

    private const int MaximumSpins = 10_000_000;

    private enum ReaderMode
    {
        Pinned,
        Active
    }
}
