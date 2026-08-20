//=======================================================================
// NavigationAStarAdmissionGate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Serializes context-owned A* resource admission.</summary>
internal sealed class NavigationAStarAdmissionGate : IDisposable
{
    private readonly object _sync = new();
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationQueryLimits _limits;
    private readonly NavigationQueryAdmissionCoordinator _coordinator;
    private readonly NavigationAStarPayloadCache _cache;
    private readonly NavigationAStarQueryWork[] _queries;
    private readonly NavigationWorldGraphLease?[] _leases;
    private readonly NavigationAStarPayloadReservation[] _reservations;
    private readonly BatchDescriptor[] _descriptors;
    private ulong _generation;
    private ulong _activeGeneration;
    private NavigationQueryCapacityReservation _capacityReservation;
    private int _activeCount;
    private int _activeAdmittedCount;
    private int _nextAdmission;
    private int _nextPublication;
    private bool _active;
    private bool _disposed;

    internal NavigationAStarAdmissionGate(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationQueryLimits limits,
        NavigationQueryAdmissionCoordinator coordinator)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(coordinator, nameof(coordinator));
        SwiftThrowHelper.ThrowIfArgument(
            limits.MaxConcurrentNavigationQueries <= 0,
            nameof(limits),
            "Query limits must be explicitly initialized.");
        _store = store;
        _limits = limits;
        _coordinator = coordinator;
        _cache = new NavigationAStarPayloadCache(
            world,
            limits.MaxAStarCacheEntries,
            limits.MaxAStarReusablePayloadBytes,
            limits.MaxAStarSinglePayloadBytes,
            limits.MaxAStarActivePayloadBytes,
            limits.MaxAStarActivePayloadLeases);
        _queries = new NavigationAStarQueryWork[limits.MaxConcurrentNavigationQueries];
        _leases = new NavigationWorldGraphLease[limits.MaxConcurrentNavigationQueries];
        _reservations = new NavigationAStarPayloadReservation[limits.MaxConcurrentNavigationQueries];
        for (int i = 0; i < _queries.Length; i++)
        {
            var workspace = new NavigationAStarWorkspace(
                mapCapacity: limits.AStarWorkspaceMapCapacity,
                endpointPageCapacity: limits.AStarWorkspaceEndpointPageCapacity,
                componentCapacity: limits.AStarWorkspaceComponentCapacity,
                nodeCapacity: limits.AStarWorkspaceNodeCapacity,
                rayCoveredAddressCapacity: limits.RayWorkspaceCoveredAddressCapacity,
                rayTraceIntervalCapacity: limits.RayWorkspaceTraceIntervalCapacity,
                guidePointCapacity: limits.AStarWorkspaceGuidePointCapacity);
            _queries[i] = new NavigationAStarQueryWork(world, store, workspace, _cache);
        }
        _descriptors = new BatchDescriptor[limits.MaxBatchItems];
    }

    internal NavigationAStarPayloadCache PayloadCache => _cache;

    internal NavigationAStarQueryStatus Begin(
        PathQuery query,
        out NavigationAStarBatchWork work)
    {
        lock (_sync)
        {
            if (!CanBegin() || !FitsSingleEnvelope(query))
            {
                work = default;
                return NavigationAStarQueryStatus.CapacityExceeded;
            }
            _descriptors[0] = new BatchDescriptor(0, 0, query);
            return BeginCore(1, out work);
        }
    }

    internal NavigationAStarQueryStatus Begin(
        PathQueryBatch batch,
        out NavigationAStarBatchWork work)
    {
        lock (_sync)
        {
            if (!CanBegin() || !FitsBatchEnvelope(batch))
            {
                work = default;
                return NavigationAStarQueryStatus.CapacityExceeded;
            }
            for (int i = 0; i < batch.Count; i++)
            {
                PathQueryBatchItem item = batch.Items[i];
                _descriptors[i] = new BatchDescriptor(item.StableOrdinal, i, item.Query);
            }
            Array.Sort(_descriptors, 0, batch.Count);
            return BeginCore(batch.Count, out work);
        }
    }

    public void Dispose()
    {
        NavigationAStarBatchWork active = default;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_active)
                active = new NavigationAStarBatchWork(this, _activeGeneration);
        }
        active.Dispose();
    }

    internal void CancelActive()
    {
        NavigationAStarBatchWork active = default;
        lock (_sync)
        {
            if (_active)
                active = new NavigationAStarBatchWork(this, _activeGeneration);
        }
        active.Dispose();
    }

    private bool CanBegin() =>
        !_disposed
        && !_active
        && _generation != ulong.MaxValue;

    private bool FitsBatchEnvelope(PathQueryBatch batch) =>
        batch.Count > 0
        && batch.Count <= _limits.MaxBatchItems
        && batch.GetLogicalRetainedBytes() <= _limits.MaxBatchDescriptorBytes;

    private bool FitsSingleEnvelope(PathQuery query)
    {
        return PathQueryBatchItem.GetLogicalRetainedBytes(query)
            <= _limits.MaxBatchDescriptorBytes;
    }

    private NavigationAStarQueryStatus BeginCore(
        int count,
        out NavigationAStarBatchWork work)
    {
        int candidateCount = _coordinator.TryReservePrefix(
            PathAlgorithm.AStar,
            Math.Min(count, _queries.Length),
            out _capacityReservation);
        int leased = _store.TryAcquirePrefix(_leases.AsSpan(0, candidateCount));
        _capacityReservation = _coordinator.Trim(_capacityReservation, leased);

        int admitted = 0;
        for (; admitted < leased; admitted++)
        {
            PathQuery query = _descriptors[admitted].Query;
            long maximumBytes = Math.Min(
                NavigationAStarPayload.GetMaximumRetainedBytes(
                    _limits.AStarWorkspaceGuidePointCapacity,
                    _limits.AStarWorkspaceComponentCapacity,
                    _limits.AStarWorkspaceEndpointPageCapacity),
                _limits.MaxAStarSinglePayloadBytes);
            if (!_cache.TryReservePayload(maximumBytes, out _reservations[admitted]))
                break;
        }
        for (int i = admitted; i < leased; i++)
        {
            _leases[i]!.Dispose();
            _leases[i] = null;
        }
        _capacityReservation = _coordinator.Trim(_capacityReservation, admitted);
        for (int i = 0; i < admitted; i++)
        {
            _descriptors[i].SlotIndex = i;
            _queries[i].BeginReserved(
                _descriptors[i].Query,
                _leases[i]!,
                ref _reservations[i]);
            _leases[i] = null;
        }
        _generation = checked(_generation + 1UL);
        _activeGeneration = _generation;
        _activeCount = count;
        _activeAdmittedCount = admitted;
        _nextAdmission = 0;
        _nextPublication = 0;
        _active = true;
        work = new NavigationAStarBatchWork(this, _activeGeneration);
        return NavigationAStarQueryStatus.Pending;
    }

    private BatchDescriptor GetDescriptor(int inputIndex)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            inputIndex < 0 || inputIndex >= _activeCount,
            inputIndex,
            nameof(inputIndex));
        for (int i = 0; i < _activeCount; i++)
        {
            if (_descriptors[i].InputIndex == inputIndex)
                return _descriptors[i];
        }
        throw new InvalidOperationException("The A* batch descriptor set is inconsistent.");
    }

    internal int GetAdmittedCount(NavigationAStarBatchWork work)
    {
        lock (_sync)
        {
            EnsureActive(work);
            return _activeAdmittedCount;
        }
    }

    internal bool IsAdmissionComplete(NavigationAStarBatchWork work)
    {
        lock (_sync)
        {
            EnsureActive(work);
            return _nextAdmission >= _activeAdmittedCount;
        }
    }

    internal NavigationAStarQueryStatus GetStatus(
        NavigationAStarBatchWork work,
        int inputIndex)
    {
        lock (_sync)
        {
            EnsureActive(work);
            BatchDescriptor descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                return NavigationAStarQueryStatus.CapacityExceeded;
            NavigationAStarQueryWork query = _queries[descriptor.SlotIndex];
            lock (query)
                return query.Status;
        }
    }

    internal bool IsReadyToPublish(
        NavigationAStarBatchWork work,
        int inputIndex)
    {
        lock (_sync)
        {
            EnsureActive(work);
            BatchDescriptor descriptor = GetDescriptor(inputIndex);
            return descriptor.SlotIndex < 0
                || _queries[descriptor.SlotIndex].IsReadyToPublish;
        }
    }

    internal void AdvanceAdmission(
        NavigationAStarBatchWork work,
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        lock (_sync)
        {
            EnsureActive(work);
            while (_nextAdmission < _activeAdmittedCount)
            {
                NavigationAStarQueryWork query = _queries[_nextAdmission];
                query.PrepareSearchOrCheckout(
                    lookupStepLimit,
                    endpointCandidateStepLimit);
                if (!query.IsPrepared)
                    return;
                _nextAdmission++;
            }
        }
    }

    internal NavigationAStarQueryStatus AdvanceSearch(
        NavigationAStarBatchWork work,
        int inputIndex,
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        BatchDescriptor descriptor;
        NavigationAStarQueryWork query;
        lock (_sync)
        {
            EnsureActive(work);
            descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                return NavigationAStarQueryStatus.CapacityExceeded;
            query = _queries[descriptor.SlotIndex];
            if (!query.IsPrepared)
                throw new InvalidOperationException("The query has not completed sequential admission.");
            Monitor.Enter(query);
        }
        try
        {
            return query.AdvanceSearch(
                lookupStepLimit,
                nodeStepLimit,
                edgeStepLimit,
                connectionStepLimit);
        }
        finally
        {
            Monitor.Exit(query);
        }
    }

    internal int PublishReadyPrefix(
        NavigationAStarBatchWork work,
        int maximumCount)
    {
        SwiftThrowHelper.ThrowIfNegative(maximumCount, nameof(maximumCount));
        lock (_sync)
        {
            EnsureActive(work);
            int published = 0;
            while (published < maximumCount
                && _nextPublication < _activeAdmittedCount)
            {
                NavigationAStarQueryWork query = _queries[_nextPublication];
                lock (query)
                {
                    if (!query.IsReadyToPublish)
                        break;
                    query.Publish();
                }
                _nextPublication++;
                published++;
            }
            return published;
        }
    }

    internal NavigationAStarPayloadLease TakeResult(
        NavigationAStarBatchWork work,
        int inputIndex)
    {
        lock (_sync)
        {
            EnsureActive(work);
            BatchDescriptor descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                throw new InvalidOperationException("The query was not admitted.");
            return _queries[descriptor.SlotIndex].TakeResult();
        }
    }

    internal void Release(NavigationAStarBatchWork work)
    {
        lock (_sync)
        {
            if (!IsActive(work))
                return;
            for (int i = 0; i < _activeAdmittedCount; i++)
            {
                lock (_queries[i])
                    _queries[i].Dispose();
            }
            _coordinator.Release(_capacityReservation);
            _capacityReservation = default;
            if (_activeCount > 0)
                Array.Clear(_descriptors, 0, _activeCount);
            _active = false;
            _activeGeneration = 0;
            _activeCount = 0;
            _activeAdmittedCount = 0;
            _nextAdmission = 0;
            _nextPublication = 0;
        }
    }

    private void EnsureActive(NavigationAStarBatchWork work)
    {
        if (!IsActive(work))
            throw new ObjectDisposedException(nameof(NavigationAStarBatchWork));
    }

    private bool IsActive(NavigationAStarBatchWork work) =>
        _active
        && work.IsOwnedBy(this)
        && work.Generation == _activeGeneration;

    private struct BatchDescriptor : IComparable<BatchDescriptor>
    {
        internal BatchDescriptor(long stableOrdinal, int inputIndex, PathQuery query)
        {
            StableOrdinal = stableOrdinal;
            InputIndex = inputIndex;
            Query = query;
            SlotIndex = -1;
        }

        internal long StableOrdinal;
        internal int InputIndex;
        internal PathQuery Query;
        internal int SlotIndex;

        public readonly int CompareTo(BatchDescriptor other)
        {
            int ordinalComparison = StableOrdinal.CompareTo(other.StableOrdinal);
            return ordinalComparison != 0
                ? ordinalComparison
                : InputIndex.CompareTo(other.InputIndex);
        }
    }
}

/// <summary>Coordinates one deterministically admitted A* query batch.</summary>
internal readonly struct NavigationAStarBatchWork : IDisposable
{
    private readonly NavigationAStarAdmissionGate? _owner;

    internal NavigationAStarBatchWork(
        NavigationAStarAdmissionGate owner,
        ulong generation)
    {
        _owner = owner;
        Generation = generation;
    }

    internal ulong Generation { get; }

    internal int AdmittedCount => Owner.GetAdmittedCount(this);

    internal bool IsAdmissionComplete => Owner.IsAdmissionComplete(this);

    internal NavigationAStarQueryStatus GetStatus(int inputIndex) =>
        Owner.GetStatus(this, inputIndex);

    internal bool IsReadyToPublish(int inputIndex) =>
        Owner.IsReadyToPublish(this, inputIndex);

    internal void AdvanceAdmission(int lookupStepLimit, int endpointCandidateStepLimit) =>
        Owner.AdvanceAdmission(this, lookupStepLimit, endpointCandidateStepLimit);

    internal NavigationAStarQueryStatus AdvanceSearch(
        int inputIndex,
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit) => Owner.AdvanceSearch(
            this,
            inputIndex,
            lookupStepLimit,
            nodeStepLimit,
            edgeStepLimit,
            connectionStepLimit);

    internal int PublishReadyPrefix(int maximumCount) =>
        Owner.PublishReadyPrefix(this, maximumCount);

    internal NavigationAStarPayloadLease TakeResult(int inputIndex) =>
        Owner.TakeResult(this, inputIndex);

    public void Dispose() => _owner?.Release(this);

    internal bool IsOwnedBy(NavigationAStarAdmissionGate owner) =>
        ReferenceEquals(_owner, owner);

    private NavigationAStarAdmissionGate Owner =>
        _owner ?? throw new ObjectDisposedException(nameof(NavigationAStarBatchWork));
}
