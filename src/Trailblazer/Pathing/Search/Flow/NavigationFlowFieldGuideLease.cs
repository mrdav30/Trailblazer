//=======================================================================
// NavigationFlowFieldGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Owns the generation-linearized state of one flow field sampler.</summary>
internal sealed class NavigationFlowFieldGuideLease
{
    private readonly object _sync = new();
    private readonly GridCoveredAddressCursor _coveredAddressCursor;
    private readonly GridCoveredAddressGeneration[] _coveredAddressGenerations;
    private readonly GridCoveredAddress[] _coveredAddressOutput;
    private NavigationFlowFieldPayloadCache? _owner;
    private GridWorld? _world;
    private NavigationWorldGraphStore? _store;
    private NavigationCellAddress _currentSource;
    private Fixed64 _originIntegrationCost;
    private NavigationGuideStatus _status;
    private int _payloadSlot;
    private ulong _payloadGeneration;
    private ulong _generation;

    internal NavigationFlowFieldGuideLease(int coveredAddressGenerationCapacity)
    {
        _coveredAddressCursor = new GridCoveredAddressCursor(
            coveredAddressGenerationCapacity);
        _coveredAddressGenerations = coveredAddressGenerationCapacity == 0
            ? Array.Empty<GridCoveredAddressGeneration>()
            : new GridCoveredAddressGeneration[coveredAddressGenerationCapacity];
        _coveredAddressOutput = new GridCoveredAddress[1];
        _payloadSlot = -1;
    }

    internal ulong Generation
    {
        get { lock (_sync) return _generation; }
    }

    internal bool CanReuse
    {
        get { lock (_sync) return _generation < ulong.MaxValue; }
    }

    internal void Bind(
        NavigationFlowFieldPayloadCache owner,
        GridWorld world,
        NavigationWorldGraphStore store,
        int payloadSlot,
        ulong payloadGeneration,
        NavigationCellAddress resolvedOrigin,
        Fixed64 originIntegrationCost)
    {
        lock (_sync)
        {
            if (_owner != null || _world != null || _store != null || _payloadSlot >= 0)
                throw new InvalidOperationException("The flow guide lease is already active.");
            if (_generation == ulong.MaxValue)
                throw new InvalidOperationException("The flow guide generation is exhausted.");
            _generation++;
            _owner = owner;
            _world = world;
            _store = store;
            _payloadSlot = payloadSlot;
            _payloadGeneration = payloadGeneration;
            _currentSource = resolvedOrigin;
            _originIntegrationCost = originIntegrationCost;
            _status = NavigationGuideStatus.Success;
        }
    }

    internal NavigationGuideStatus GetStatus(ulong generation)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationGuideStatus.Stale;
            if (_status != NavigationGuideStatus.Success)
                return _status;
            return TryGetCurrentPayloadUnderLock(out _)
                ? NavigationGuideStatus.Success
                : MarkStaleUnderLock();
        }
    }

    internal Fixed64 GetOriginIntegrationCost(ulong generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _originIntegrationCost
                : Fixed64.Zero;
        }
    }

    internal NavigationGuideStatus TrySample(
        ulong generation,
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out Vector3d heading)
    {
        var meter = new GuideSampleWorkMeter(budget);
        return TrySample(
            generation,
            actualFootPosition,
            ref meter,
            out heading);
    }

    internal NavigationGuideStatus TrySample(
        ulong generation,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        out Vector3d heading)
    {
        lock (_sync)
        {
            heading = Vector3d.Zero;
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationGuideStatus.Stale;
            if (_status != NavigationGuideStatus.Success)
                return _status;
            if (!TryGetCurrentPayloadUnderLock(out NavigationFlowFieldPayload payload))
                return MarkStaleUnderLock();
            NavigationWorldGraphStore store = _store!;
            NavigationWorldGraphLease? graphLease = store.TryAcquire();
            if (graphLease == null)
                return NavigationGuideStatus.CapacityExceeded;
            using (graphLease)
            {
                NavigationWorldGraph graph = graphLease.Graph;
                if (!graph.IsDependencyCurrent(payload.Dependencies))
                    return MarkStaleUnderLock();
                NavigationCellAddress candidateSource = _currentSource;
                NavigationGuideStatus status = NavigationSelectedEdgeProgressWork.TrySample(
                    _world!,
                    graph,
                    payload,
                    _currentSource,
                    actualFootPosition,
                    ref meter,
                    _coveredAddressCursor,
                    _coveredAddressGenerations,
                    _coveredAddressOutput,
                    out candidateSource,
                    out Vector3d candidateHeading);
                if (!TryGetCurrentPayloadUnderLock(out NavigationFlowFieldPayload current)
                    || !ReferenceEquals(current, payload))
                {
                    return MarkStaleUnderLock();
                }
                if (status == NavigationGuideStatus.Stale)
                    return MarkStaleUnderLock();
                if (status == NavigationGuideStatus.Success)
                {
                    _currentSource = candidateSource;
                    heading = candidateHeading;
                }
                return status;
            }
        }
    }

    internal bool TryDetach(
        ulong generation,
        out int payloadSlot,
        out ulong payloadGeneration)
    {
        lock (_sync)
        {
            payloadSlot = -1;
            payloadGeneration = 0;
            if (!IsGenerationActiveUnderLock(generation))
                return false;
            payloadSlot = _payloadSlot;
            payloadGeneration = _payloadGeneration;
            _owner = null;
            _world = null;
            _store = null;
            _currentSource = default;
            _originIntegrationCost = Fixed64.Zero;
            _status = NavigationGuideStatus.Stale;
            _payloadSlot = -1;
            _payloadGeneration = 0;
            return true;
        }
    }

    internal void Dispose(ulong generation)
    {
        NavigationFlowFieldPayloadCache? owner;
        lock (_sync)
            owner = IsGenerationActiveUnderLock(generation) ? _owner : null;
        owner?.ReturnGuide(this, generation);
    }

    private bool IsGenerationActiveUnderLock(ulong generation) =>
        generation != 0
        && generation == _generation
        && _owner != null
        && _world != null
        && _store != null
        && _payloadSlot >= 0;

    private bool TryGetCurrentPayloadUnderLock(
        out NavigationFlowFieldPayload payload)
    {
        NavigationFlowFieldPayloadCache? owner = _owner;
        NavigationWorldGraphStore? store = _store;
        if (owner != null
            && store != null
            && owner.TryGetGuidePayload(
                    _payloadSlot,
                    _payloadGeneration,
                    out payload) == NavigationFlowFieldStatus.Success
            && store.Current.IsDependencyCurrent(payload.Dependencies))
        {
            return true;
        }
        payload = null!;
        return false;
    }

    private NavigationGuideStatus MarkStaleUnderLock()
    {
        _status = NavigationGuideStatus.Stale;
        return _status;
    }
}
