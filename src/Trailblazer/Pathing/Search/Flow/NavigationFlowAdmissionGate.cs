//=======================================================================
// NavigationFlowAdmissionGate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Serializes context-owned flow resource admission.</summary>
internal sealed class NavigationFlowAdmissionGate : IDisposable
{
    private readonly object _sync = new();
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationQueryLimits _limits;
    private readonly NavigationQueryAdmissionCoordinator _coordinator;
    private readonly NavigationFlowFieldPayloadCache _cache;
    private readonly NavigationFlowQueryWork[] _queries;
    private readonly NavigationWorldGraphLease?[] _leases;
    private readonly BatchDescriptor[] _descriptors;
    private NavigationQueryCapacityReservation _capacityReservation;
    private ulong _generation;
    private ulong _activeGeneration;
    private int _activeCount;
    private int _activeAdmittedCount;
    private int _nextAdmission;
    private int _nextPublication;
    private bool _active;
    private bool _disposed;

    internal NavigationFlowAdmissionGate(
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
        _cache = new NavigationFlowFieldPayloadCache(
            limits.MaxFlowCacheEntries,
            limits.MaxFlowReusablePayloadBytes,
            limits.MaxFlowSinglePayloadBytes,
            limits.MaxFlowActivePayloadBytes,
            limits.MaxFlowActivePayloadLeases,
            limits.FlowWorkspaceMapCapacity);
        _queries = new NavigationFlowQueryWork[limits.MaxConcurrentNavigationQueries];
        _leases = new NavigationWorldGraphLease[limits.MaxConcurrentNavigationQueries];
        for (int i = 0; i < _queries.Length; i++)
        {
            var workspace = new NavigationFlowFieldWorkspace(
                limits.FlowWorkspaceMapCapacity,
                limits.FlowWorkspaceEndpointPageCapacity,
                limits.FlowWorkspaceComponentCapacity,
                limits.FlowWorkspaceNodeCapacity,
                limits.RayWorkspaceCoveredAddressCapacity,
                limits.RayWorkspaceTraceIntervalCapacity);
            _queries[i] = new NavigationFlowQueryWork(world, store, workspace, _cache);
        }
        _descriptors = new BatchDescriptor[limits.MaxBatchItems];
    }

    internal NavigationFlowFieldPayloadCache PayloadCache => _cache;

    internal NavigationFlowQueryStatus Begin(
        PathQuery query,
        out NavigationFlowBatchWork work)
    {
        lock (_sync)
        {
            if (!CanBegin() || !FitsSingleEnvelope(query))
            {
                work = default;
                return NavigationFlowQueryStatus.CapacityExceeded;
            }
            _descriptors[0] = new BatchDescriptor(0, 0, query);
            return BeginCore(1, out work);
        }
    }

    internal NavigationFlowQueryStatus Begin(
        PathQueryBatch batch,
        out NavigationFlowBatchWork work)
    {
        lock (_sync)
        {
            if (!CanBegin() || !FitsBatchEnvelope(batch))
            {
                work = default;
                return NavigationFlowQueryStatus.CapacityExceeded;
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
        NavigationFlowBatchWork active = default;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_active)
                active = new NavigationFlowBatchWork(this, _activeGeneration);
        }
        active.Dispose();
        _cache.Dispose();
    }

    internal void CancelActive()
    {
        NavigationFlowBatchWork active = default;
        lock (_sync)
        {
            if (_active)
                active = new NavigationFlowBatchWork(this, _activeGeneration);
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

    private bool FitsSingleEnvelope(PathQuery query) =>
        PathQueryBatchItem.GetLogicalRetainedBytes(query)
        <= _limits.MaxBatchDescriptorBytes;

    private NavigationFlowQueryStatus BeginCore(
        int count,
        out NavigationFlowBatchWork work)
    {
        int candidateCount = _coordinator.TryReservePrefix(
            PathAlgorithm.FlowField,
            Math.Min(count, _queries.Length),
            out _capacityReservation);
        int leased = _store.TryAcquirePrefix(_leases.AsSpan(0, candidateCount));
        _capacityReservation = _coordinator.Trim(_capacityReservation, leased);
        for (int i = 0; i < leased; i++)
        {
            _descriptors[i].SlotIndex = i;
            _queries[i].Begin(_descriptors[i].Query, _leases[i]!);
            _leases[i] = null;
        }
        _generation = checked(_generation + 1UL);
        _activeGeneration = _generation;
        _activeCount = count;
        _activeAdmittedCount = leased;
        _nextAdmission = 0;
        _nextPublication = 0;
        _active = true;
        work = new NavigationFlowBatchWork(this, _activeGeneration);
        return NavigationFlowQueryStatus.Pending;
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
        throw new InvalidOperationException("The flow batch descriptor set is inconsistent.");
    }

    internal int GetAdmittedCount(NavigationFlowBatchWork work)
    {
        lock (_sync)
        {
            EnsureActive(work);
            return _activeAdmittedCount;
        }
    }

    internal bool IsAdmissionComplete(NavigationFlowBatchWork work)
    {
        lock (_sync)
        {
            EnsureActive(work);
            return _nextAdmission >= _activeAdmittedCount;
        }
    }

    internal NavigationFlowQueryStatus GetStatus(
        NavigationFlowBatchWork work,
        int inputIndex)
    {
        lock (_sync)
        {
            EnsureActive(work);
            BatchDescriptor descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                return NavigationFlowQueryStatus.CapacityExceeded;
            NavigationFlowQueryWork query = _queries[descriptor.SlotIndex];
            lock (query)
                return query.Status;
        }
    }

    internal bool IsReadyToPublish(
        NavigationFlowBatchWork work,
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
        NavigationFlowBatchWork work,
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        lock (_sync)
        {
            EnsureActive(work);
            while (_nextAdmission < _activeAdmittedCount)
            {
                NavigationFlowQueryWork query = _queries[_nextAdmission];
                query.PrepareSearchOrCheckout(
                    lookupStepLimit,
                    endpointCandidateStepLimit);
                if (!query.IsPrepared)
                    return;
                if (query.ReservationRejected)
                {
                    CutCapacitySuffix(_nextAdmission);
                    return;
                }
                _nextAdmission++;
            }
        }
    }

    internal NavigationFlowQueryStatus AdvanceSearch(
        NavigationFlowBatchWork work,
        int inputIndex,
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        BatchDescriptor descriptor;
        NavigationFlowQueryWork query;
        lock (_sync)
        {
            EnsureActive(work);
            if (_nextAdmission < _activeAdmittedCount)
            {
                throw new InvalidOperationException(
                    "The flow batch has not completed its sequential preparation barrier.");
            }
            descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                return NavigationFlowQueryStatus.CapacityExceeded;
            query = _queries[descriptor.SlotIndex];
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
        NavigationFlowBatchWork work,
        int maximumCount)
    {
        SwiftThrowHelper.ThrowIfNegative(maximumCount, nameof(maximumCount));
        lock (_sync)
        {
            EnsureActive(work);
            if (_nextAdmission < _activeAdmittedCount)
            {
                throw new InvalidOperationException(
                    "The flow batch has not completed its sequential preparation barrier.");
            }
            int published = 0;
            while (published < maximumCount
                && _nextPublication < _activeAdmittedCount)
            {
                NavigationFlowQueryWork query = _queries[_nextPublication];
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

    internal NavigationFlowQueryResult TakeResult(
        NavigationFlowBatchWork work,
        int inputIndex)
    {
        lock (_sync)
        {
            EnsureActive(work);
            BatchDescriptor descriptor = GetDescriptor(inputIndex);
            if (descriptor.SlotIndex < 0)
                throw new InvalidOperationException("The flow query was not admitted.");
            return _queries[descriptor.SlotIndex].TakeResult();
        }
    }

    internal void Release(NavigationFlowBatchWork work)
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

    private void CutCapacitySuffix(int firstRejected)
    {
        int previousCount = _activeAdmittedCount;
        for (int i = firstRejected; i < previousCount; i++)
        {
            _queries[i].Dispose();
            _descriptors[i].SlotIndex = -1;
        }
        _activeAdmittedCount = firstRejected;
        _nextAdmission = firstRejected;
        _capacityReservation = _coordinator.Trim(
            _capacityReservation,
            firstRejected);
    }

    private void EnsureActive(NavigationFlowBatchWork work)
    {
        if (!IsActive(work))
            throw new ObjectDisposedException(nameof(NavigationFlowBatchWork));
    }

    private bool IsActive(NavigationFlowBatchWork work) =>
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
