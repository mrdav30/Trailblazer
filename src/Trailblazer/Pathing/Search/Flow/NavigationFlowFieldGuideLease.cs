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
    private NavigationWorldGraphStore? _store;
    private NavigationCellAddress _currentSource;
    private TraversalMedium _currentMedium;
    private Fixed64 _originIntegrationCost;
    private NavigationGuideStatus _status;
    private int _payloadSlot;
    private ulong _payloadGeneration;
    private ulong _generation;
    private long _sampleOrdinal;
    private bool _hasPendingTransition;

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
        NavigationWorldGraphStore store,
        int payloadSlot,
        ulong payloadGeneration,
        NavigationCellAddress resolvedOrigin,
        TraversalMedium startMedium,
        Fixed64 originIntegrationCost)
    {
        lock (_sync)
        {
            if (_owner != null
                || _store != null
                || _payloadSlot >= 0)
                throw new InvalidOperationException("The flow guide lease is already active.");
            if (_generation == ulong.MaxValue)
                throw new InvalidOperationException("The flow guide generation is exhausted.");
            _generation++;
            _owner = owner;
            _store = store;
            _payloadSlot = payloadSlot;
            _payloadGeneration = payloadGeneration;
            _currentSource = resolvedOrigin;
            _currentMedium = startMedium;
            _originIntegrationCost = originIntegrationCost;
            _sampleOrdinal = 0;
            _hasPendingTransition = false;
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
        ref GuideSampleWorkMeter meter,
        out NavigationFlowSample sample)
    {
        lock (_sync)
        {
            sample = default;
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationGuideStatus.Stale;
            if (_status != NavigationGuideStatus.Success)
                return _status;
            if (!TryGetCurrentPayloadUnderLock(out NavigationFlowFieldPayload payload))
                return MarkStaleUnderLock();
            NavigationFlowFieldPayloadCache owner = _owner!;
            GridWorld world = owner.World;
            ulong worldSequence = world.ChangeSequence;
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
                    world,
                    store,
                    graph,
                    payload,
                    _currentSource,
                    _currentMedium,
                    actualFootPosition,
                    ref meter,
                    _coveredAddressCursor,
                    _coveredAddressGenerations,
                    _coveredAddressOutput,
                    owner.ImmediateRayWorkspace,
                    out NavigationFlowFieldNode currentNode,
                    out candidateSource,
                    out Vector3d target,
                    out Vector3d candidateHeading);
                if (!TryGetCurrentPayloadUnderLock(out NavigationFlowFieldPayload current)
                    || !ReferenceEquals(current, payload)
                    || world.ChangeSequence != worldSequence)
                {
                    return MarkStaleUnderLock();
                }
                if (status == NavigationGuideStatus.Stale)
                    return MarkStaleUnderLock();
                if (status == NavigationGuideStatus.Success)
                {
                    bool sourceChanged = candidateSource != _currentSource;
                    if (sourceChanged && _sampleOrdinal == long.MaxValue)
                        return MarkStaleUnderLock();
                    long candidateSampleOrdinal = sourceChanged
                        ? _sampleOrdinal + 1L
                        : _sampleOrdinal;
                    if (currentNode.TransitionInstructionOrdinal >= 0)
                    {
                        NavigationGuideStatus transitionStatus =
                            TrySampleTransitionUnderLock(
                            generation,
                            world,
                            store,
                            graph,
                            owner,
                            payload,
                            currentNode,
                            candidateSource,
                            candidateSampleOrdinal,
                            actualFootPosition,
                            ref meter,
                            out sample);
                        if (transitionStatus == NavigationGuideStatus.Stale)
                            return MarkStaleUnderLock();
                        if (transitionStatus != NavigationGuideStatus.Success)
                            return transitionStatus;
                        if (!TryGetCurrentPayloadUnderLock(
                                out NavigationFlowFieldPayload transitionPayload)
                            || !ReferenceEquals(transitionPayload, payload)
                            || world.ChangeSequence != worldSequence)
                        {
                            sample = default;
                            return MarkStaleUnderLock();
                        }
                        _currentSource = candidateSource;
                        _sampleOrdinal = candidateSampleOrdinal;
                        _hasPendingTransition = sample.HasTransition;
                        return NavigationGuideStatus.Success;
                    }
                    _currentSource = candidateSource;
                    _sampleOrdinal = candidateSampleOrdinal;
                    sample = new NavigationFlowSample(
                        candidateHeading,
                        target,
                        _currentMedium,
                        default,
                        hasTransition: false);
                }
                return status;
            }
        }
    }

    internal NavigationGuideStatus CompletePendingTransition(
        ulong generation,
        in NavigationTransitionInstruction instruction)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation)
                || !_hasPendingTransition
                || !TryGetCurrentPayloadUnderLock(out NavigationFlowFieldPayload payload)
                || !payload.TryGetNode(
                    _currentSource,
                    _currentMedium,
                    out NavigationFlowFieldNode current)
                || (uint)current.TransitionInstructionOrdinal
                    >= (uint)payload.TransitionInstructions.Length
                || !instruction.MatchesCompletion(
                    this,
                    generation,
                    _sampleOrdinal))
            {
                return NavigationGuideStatus.Stale;
            }
            NavigationTransitionInstruction expected =
                payload.TransitionInstructions[current.TransitionInstructionOrdinal];
            if (expected.SourceAddress != _currentSource
                || expected.SourceMedium != _currentMedium
                || expected.DestinationAddress != current.SelectedEdge.Target
                || expected.DestinationMedium != current.SelectedEdge.TargetMedium)
            {
                return MarkStaleUnderLock();
            }
            if (_sampleOrdinal == long.MaxValue)
                return MarkStaleUnderLock();
            _currentSource = expected.DestinationAddress;
            _currentMedium = expected.DestinationMedium;
            _hasPendingTransition = false;
            _sampleOrdinal++;
            return NavigationGuideStatus.Success;
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
            _store = null;
            _currentSource = default;
            _currentMedium = TraversalMedium.Unknown;
            _originIntegrationCost = Fixed64.Zero;
            _sampleOrdinal = 0;
            _hasPendingTransition = false;
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
            && store.Current.IsDependencyCurrent(payload.Dependencies)
            && owner.IsWorldCurrent(payload))
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

    private NavigationGuideStatus TrySampleTransitionUnderLock(
        ulong generation,
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayloadCache owner,
        NavigationFlowFieldPayload payload,
        in NavigationFlowFieldNode node,
        NavigationCellAddress candidateSource,
        long candidateSampleOrdinal,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        out NavigationFlowSample sample)
    {
        sample = default;
        if ((uint)node.TransitionInstructionOrdinal
                >= (uint)payload.TransitionInstructions.Length)
        {
            return MarkStaleUnderLock();
        }
        NavigationTransitionInstruction instruction =
            payload.TransitionInstructions[node.TransitionInstructionOrdinal];
        if (instruction.SourceAddress != candidateSource
            || instruction.SourceMedium != _currentMedium
            || instruction.DestinationAddress != node.SelectedEdge.Target
            || instruction.DestinationMedium != node.SelectedEdge.TargetMedium)
        {
            return MarkStaleUnderLock();
        }
        NavigationGuideStatus headingStatus;
        Vector3d heading;
        if (_hasPendingTransition)
        {
            headingStatus = NavigationGuideStatus.Success;
            heading = Vector3d.Zero;
        }
        else
        {
            headingStatus = NavigationSelectedEdgeProgressWork.TrySampleTransitionApproach(
                world,
                store,
                graph,
                payload,
                candidateSource,
                _currentMedium,
                actualFootPosition,
                instruction.SourcePosition,
                ref meter,
                owner.ImmediateRayWorkspace,
                out heading);
        }
        if (headingStatus != NavigationGuideStatus.Success)
            return headingStatus;
        bool hasTransition = heading == Vector3d.Zero;
        if (hasTransition && candidateSampleOrdinal == long.MaxValue)
            return NavigationGuideStatus.Stale;
        sample = new NavigationFlowSample(
            heading,
            instruction.SourcePosition,
            _currentMedium,
            hasTransition
                ? instruction.WithCompletionStamp(
                    this,
                    generation,
                    candidateSampleOrdinal)
                : default,
            hasTransition);
        return NavigationGuideStatus.Success;
    }
}
