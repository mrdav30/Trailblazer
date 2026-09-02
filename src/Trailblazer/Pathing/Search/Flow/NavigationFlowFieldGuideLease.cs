//=======================================================================
// NavigationFlowFieldGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
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
            // The cache rents only detached guides and does not return one to its pool
            // until TryDetach has cleared this complete ownership tuple.
            Debug.Assert(_owner == null);
            _generation = NavigationGenerationCounter.Advance(
                _generation,
                "The flow guide generation is exhausted.");
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
                bool dependencyCurrent = graph.IsDependencyCurrent(payload.Dependencies);
                NavigationCellAddress candidateSource = _currentSource;
                NavigationFlowFieldNode currentNode = default;
                Vector3d target = default;
                Vector3d candidateHeading = default;
                NavigationGuideStatus status =
                    NavigationSelectedEdgeProgressWork.TrySample(
                        dependencyCurrent,
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
                        out currentNode,
                        out candidateSource,
                        out target,
                        out candidateHeading);
                if (!dependencyCurrent
                    || !TryGetCurrentPayloadUnderLock(out _)
                    || world.ChangeSequence != worldSequence)
                {
                    return MarkStaleUnderLock();
                }
                if (status == NavigationGuideStatus.Stale)
                    return MarkStaleUnderLock();
                if (status == NavigationGuideStatus.Success)
                {
                    bool sourceChanged = candidateSource != _currentSource;
                    // Selected edges target earlier-settled nodes in this int-bounded
                    // payload, so one lease cannot exhaust a long source identity.
                    Debug.Assert(_sampleOrdinal < payload.Nodes.Length);
                    long candidateSampleOrdinal = AdvanceSampleOrdinal(
                        _sampleOrdinal,
                        sourceChanged);
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
                        bool transitionEpochCurrent = IsSampleEpochCurrent(
                            TryGetCurrentPayloadUnderLock(out _),
                            world.ChangeSequence,
                            worldSequence);
                        transitionStatus = ResolveTransitionSampleStatus(
                            transitionEpochCurrent,
                            transitionStatus,
                            ref sample);
                        if (transitionStatus == NavigationGuideStatus.Stale)
                            return MarkStaleUnderLock();
                        if (transitionStatus != NavigationGuideStatus.Success)
                            return transitionStatus;
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
                || !instruction.MatchesCompletion(
                    this,
                    generation,
                    _sampleOrdinal))
            {
                return NavigationGuideStatus.Stale;
            }
            NavigationTransitionInstruction expected =
                payload.TransitionInstructions[current.TransitionInstructionOrdinal];
            Debug.Assert(_sampleOrdinal < payload.Nodes.Length);
            _status = ResolveTransitionCompletion(
                expected.DestinationAddress,
                expected.DestinationMedium,
                AdvanceSampleOrdinal(_sampleOrdinal, advance: true),
                ref _currentSource,
                ref _currentMedium,
                ref _hasPendingTransition,
                ref _sampleOrdinal);
            return _status;
        }
    }

    internal static NavigationGuideStatus ResolveTransitionSampleStatus(
        bool epochCurrent,
        NavigationGuideStatus status,
        ref NavigationFlowSample sample)
    {
        if (status != NavigationGuideStatus.Success || epochCurrent)
            return status;
        sample = default;
        return NavigationGuideStatus.Stale;
    }

    internal static long AdvanceSampleOrdinal(long current, bool advance)
    {
        Debug.Assert(!advance || current < long.MaxValue);
        return current + (advance ? 1L : 0L);
    }

    internal static bool IsSampleEpochCurrent(
        bool payloadCurrent,
        ulong currentWorldSequence,
        ulong expectedWorldSequence) =>
        payloadCurrent && currentWorldSequence == expectedWorldSequence;

    internal static NavigationGuideStatus ResolveTransitionCompletion(
        NavigationCellAddress destination,
        TraversalMedium destinationMedium,
        long nextSampleOrdinal,
        ref NavigationCellAddress currentSource,
        ref TraversalMedium currentMedium,
        ref bool hasPendingTransition,
        ref long sampleOrdinal)
    {
        currentSource = destination;
        currentMedium = destinationMedium;
        hasPendingTransition = false;
        sampleOrdinal = nextSampleOrdinal;
        return NavigationGuideStatus.Success;
    }

    internal bool TryDetach(
        ulong generation,
        out NavigationFlowFieldPayloadCache owner,
        out int payloadSlot)
    {
        lock (_sync)
        {
            owner = null!;
            payloadSlot = -1;
            if (!IsGenerationActiveUnderLock(generation))
                return false;
            owner = _owner!;
            payloadSlot = _payloadSlot;
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
        if (!TryDetach(
                generation,
                out NavigationFlowFieldPayloadCache owner,
                out int payloadSlot))
        {
            return;
        }
        owner.ReturnDetachedGuide(this, generation, payloadSlot);
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
            && owner.IsPayloadCurrent(store.Current, payload))
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
        NavigationTransitionInstruction instruction =
            payload.TransitionInstructions[node.TransitionInstructionOrdinal];
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
        Debug.Assert(!hasTransition || candidateSampleOrdinal < payload.Nodes.Length);
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
