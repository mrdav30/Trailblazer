//=======================================================================
// NavigationEndpointResolutionWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Reports bounded endpoint-resolution progress.</summary>
internal enum NavigationEndpointResolutionStatus : byte
{
    Pending = 0,
    Success = 1,
    NoMap = 2,
    InvalidEndpoint = 3,
    BudgetExceeded = 4,
    CostOverflow = 5,
    CapacityExceeded = 6,
    Stale = 7
}

/// <summary>Identifies the directed query endpoint being resolved.</summary>
internal enum NavigationEndpointRole : byte
{
    Start,
    Destination
}

/// <summary>Identifies one exact resolved graph endpoint.</summary>
internal readonly struct NavigationResolvedEndpoint
{
    internal NavigationResolvedEndpoint(
        NavigationNodeRef node,
        NavigationCellAddress address,
        TraversalMedia media,
        TraversalMedium resolutionMedium,
        Vector3d footAnchor,
        Fixed64 resolutionDistance)
    {
        Node = node;
        Address = address;
        Media = media;
        ResolutionMedium = resolutionMedium;
        FootAnchor = footAnchor;
        ResolutionDistance = resolutionDistance;
    }

    internal NavigationNodeRef Node { get; }
    internal NavigationCellAddress Address { get; }
    internal TraversalMedia Media { get; }
    internal TraversalMedium ResolutionMedium { get; }
    internal Vector3d FootAnchor { get; }
    internal Fixed64 ResolutionDistance { get; }
}

/// <summary>Resolves one endpoint through bounded GridForge coverage and immutable graph filtering.</summary>
internal sealed class NavigationEndpointResolutionWork
{
    private readonly GridWorld _world;
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationWorkMeter _meter;
    private readonly NavigationEndpointWorkspace _workspace;
    private readonly NavigationRayWorkspace _rayWorkspace;
    private readonly NavigationRayWork _rayWork;
    private NavigationWorldGraph _graph = null!;
    private NavigationEndpoint _endpoint;
    private TraversalEvaluator _evaluator;
    private NavigationVolumeAnchorEvaluator _volumeEvaluator;
    private TraversalMedia _media;
    private NavigationEndpointRole _role;
    private NavigationResolvedEndpoint _pendingCandidate;
    private int _mapOrdinal;
    private int _generationInputOrdinal;
    private ulong _worldChangeSequence;
    private bool _discoveryComplete;
    private bool _cursorBegun;
    private bool _cursorComplete;
    private bool _hasResult;

    internal NavigationEndpointResolutionWork(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorkMeter meter,
        NavigationEndpointWorkspace workspace,
        NavigationRayWorkspace rayWorkspace,
        NavigationRayWork rayWork)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        SwiftThrowHelper.ThrowIfNull(rayWorkspace, nameof(rayWorkspace));
        SwiftThrowHelper.ThrowIfNull(rayWork, nameof(rayWork));
        _world = world;
        _store = store;
        _meter = meter;
        _workspace = workspace;
        _rayWorkspace = rayWorkspace;
        _rayWork = rayWork;
    }

    internal void Begin(
        NavigationWorldGraph graph,
        NavigationEndpoint endpoint,
        NavigationEndpointRole role,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalMedia media)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        SwiftThrowHelper.ThrowIfArgument(
            role is not NavigationEndpointRole.Start
                and not NavigationEndpointRole.Destination,
            nameof(role),
            "Endpoint role must be start or destination.");
        _graph = graph;
        _endpoint = endpoint;
        _role = role;
        _media = media;
        _evaluator = new TraversalEvaluator(
            graph,
            profile,
            areaPolicy,
            TraversalMedium.Solid);
        _volumeEvaluator = new NavigationVolumeAnchorEvaluator(
            _world,
            graph,
            profile,
            areaPolicy,
            _rayWorkspace);
        _pendingCandidate = default;
        _mapOrdinal = 0;
        _generationInputOrdinal = 0;
        _worldChangeSequence = _world.ChangeSequence;
        _cursorBegun = false;
        _cursorComplete = false;
        _hasResult = false;
        Result = default;
        _rayWork.Reset();
        _workspace.ResetResolution();
        _discoveryComplete = endpoint.MapId == null && graph.MapCount == 0;
        Status = (endpoint.MapId == null
                && graph.MapCount > _workspace.CoveredAddressGenerations.Length)
            || (endpoint.MapId != null
                && _workspace.CoveredAddressGenerations.Length == 0)
                ? NavigationEndpointResolutionStatus.CapacityExceeded
                : NavigationEndpointResolutionStatus.Pending;
    }

    internal void Reset()
    {
        _graph = null!;
        _endpoint = default;
        _evaluator = default;
        _volumeEvaluator = default;
        _media = default;
        _role = default;
        _pendingCandidate = default;
        _mapOrdinal = 0;
        _generationInputOrdinal = 0;
        _worldChangeSequence = 0;
        _discoveryComplete = false;
        _cursorBegun = false;
        _cursorComplete = false;
        _hasResult = false;
        _rayWork.Reset();
        Status = default;
        Result = default;
    }

    internal NavigationEndpointResolutionStatus Status { get; private set; }

    internal NavigationResolvedEndpoint Result { get; private set; }

    internal NavigationEndpointResolutionStatus Advance(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(
            endpointCandidateStepLimit,
            nameof(endpointCandidateStepLimit));
        if (Status != NavigationEndpointResolutionStatus.Pending)
            return Status;

        if (_pendingCandidate.Node.IsValid)
        {
            NavigationEndpointResolutionStatus candidateStatus = AdvanceCandidateRay();
            if (candidateStatus != NavigationEndpointResolutionStatus.Pending)
                return candidateStatus;
            return Status;
        }
        if (_cursorComplete)
        {
            return Finish(
                _hasResult
                    ? NavigationEndpointResolutionStatus.Success
                    : NavigationEndpointResolutionStatus.InvalidEndpoint);
        }

        int lookupRemaining = Math.Min(lookupStepLimit, _meter.RemainingLookupProbes);
        int candidateRemaining = Math.Min(
            endpointCandidateStepLimit,
            _meter.RemainingEndpointCandidates);
        if (!_discoveryComplete)
        {
            if (lookupRemaining == 0)
            {
                return _meter.RemainingLookupProbes == 0
                    ? Finish(NavigationEndpointResolutionStatus.BudgetExceeded)
                    : Status;
            }
            DiscoverGeneration();
            _meter.TryConsumeLookupProbes(1);
            lookupRemaining--;
            if (!_discoveryComplete || lookupRemaining == 0)
                return Status;
        }

        if (!_cursorBegun)
        {
            if (_workspace.CoveredAddressGenerationCount == 0)
                return Finish(NavigationEndpointResolutionStatus.NoMap);
            if (!TryGetBounds(out Vector3d minimum, out Vector3d maximum))
                return Finish(NavigationEndpointResolutionStatus.CostOverflow);
            if (!_world.TryBeginCoveredAddresses(
                    _workspace.CoveredAddressCursor,
                    minimum,
                    maximum,
                    _workspace.CoveredAddressGenerationCount))
            {
                return Finish(NavigationEndpointResolutionStatus.CapacityExceeded);
            }
            _cursorBegun = true;
        }

        while (lookupRemaining > 0 || candidateRemaining > 0)
        {
            bool binding = _generationInputOrdinal
                < _workspace.CoveredAddressGenerationCount;
            ReadOnlySpan<GridCoveredAddressGeneration> input = binding
                ? _workspace.CoveredAddressGenerations.AsSpan(
                    _generationInputOrdinal,
                    _workspace.CoveredAddressGenerationCount - _generationInputOrdinal)
                : ReadOnlySpan<GridCoveredAddressGeneration>.Empty;
            GridCoveredAddressCursorStatus cursorStatus = _world.AdvanceCoveredAddresses(
                _workspace.CoveredAddressCursor,
                input,
                _workspace.CoveredAddressOutput,
                lookupProbeLimit: binding ? lookupRemaining : 0,
                addressProbeLimit: binding ? 0 : lookupRemaining,
                outputLimit: candidateRemaining > 0 ? 1 : 0,
                out int lookupProbes,
                out int addressProbes,
                out int inputsConsumed,
                out int outputCount);
            _cursorComplete = cursorStatus == GridCoveredAddressCursorStatus.Complete;
            int consumedLookup = checked(lookupProbes + addressProbes);
            _meter.TryConsumeLookupProbes(consumedLookup);
            lookupRemaining -= consumedLookup;
            _generationInputOrdinal += inputsConsumed;
            if (cursorStatus == GridCoveredAddressCursorStatus.Stale)
                return Finish(NavigationEndpointResolutionStatus.Stale);
            if (outputCount != 0)
            {
                _meter.TryConsumeEndpointCandidates(1);
                candidateRemaining--;
                if (!ConsiderCandidate(_workspace.CoveredAddressOutput[0]))
                    return Status;
            }
            if (cursorStatus == GridCoveredAddressCursorStatus.Complete)
            {
                return Finish(
                    _hasResult
                        ? NavigationEndpointResolutionStatus.Success
                        : NavigationEndpointResolutionStatus.InvalidEndpoint);
            }
            if (consumedLookup == 0 && outputCount == 0)
                break;
        }

        if (_meter.RemainingLookupProbes == 0
            || (_generationInputOrdinal >= _workspace.CoveredAddressGenerationCount
                && _meter.RemainingEndpointCandidates == 0))
        {
            return Finish(NavigationEndpointResolutionStatus.BudgetExceeded);
        }
        return Status;
    }

    private void DiscoverGeneration()
    {
        if (_endpoint.MapId != null)
        {
            if (_mapOrdinal == 0
                && _graph.TryGetCoveredAddressGeneration(
                    _endpoint.MapId,
                    out GridCoveredAddressGeneration generation))
            {
                _workspace.CoveredAddressGenerations[0] = generation;
                _workspace.CoveredAddressGenerationCount = 1;
            }
            _mapOrdinal = 1;
            _discoveryComplete = true;
            return;
        }

        if (_mapOrdinal < _graph.MapCount
            && _graph.TryGetCoveredAddressGeneration(
                _mapOrdinal,
                out string mapId,
                out GridCoveredAddressGeneration generationAtOrdinal))
        {
            _workspace.CoveredAddressGenerations[
                _workspace.CoveredAddressGenerationCount++] = generationAtOrdinal;
        }
        _mapOrdinal++;
        _discoveryComplete = _mapOrdinal >= _graph.MapCount;
    }

    private bool ConsiderCandidate(GridCoveredAddress candidate)
    {
        if (!_graph.TryGetMapId(candidate.ConfigurationKey, out string mapId)
            || !_graph.TryGetNodeRef(
                new NavigationCellAddress(mapId, candidate.VoxelIndex),
                out NavigationNodeRef node))
        {
            return true;
        }
        if (!_workspace.TryRecordPage(
                mapId,
                node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            Finish(NavigationEndpointResolutionStatus.CapacityExceeded);
            return false;
        }
        bool consumedVolumeWork = false;
        if (!_graph.TryGetNodeState(node, out NavigationNodeState state)
            || !TryQualifyCandidate(
                node,
                state,
                out TraversalMedia qualifyingMedia,
                out TraversalMedium resolutionMedium,
                out Vector3d footAnchor,
                out Fixed64 distance,
                out consumedVolumeWork))
        {
            return !consumedVolumeWork;
        }
        var address = new NavigationCellAddress(mapId, candidate.VoxelIndex);
        if (!CanBeatCurrentResult(address, distance, resolutionMedium))
            return true;

        var resolved = new NavigationResolvedEndpoint(
            node,
            address,
            qualifyingMedia,
            resolutionMedium,
            footAnchor,
            distance);
        if (_endpoint.Resolution == EndpointResolutionPolicy.Strict
            || (qualifyingMedia & TraversalMedia.Solid) == 0)
        {
            AcceptCandidate(resolved);
            return _endpoint.Resolution == EndpointResolutionPolicy.Strict
                && resolutionMedium == TraversalMedium.Solid;
        }

        _pendingCandidate = resolved;
        Vector3d start = _role == NavigationEndpointRole.Start
            ? _endpoint.Position
            : state.FootAnchor;
        Vector3d end = _role == NavigationEndpointRole.Start
            ? state.FootAnchor
            : _endpoint.Position;
        NavigationRayChainConstraint constraint = _role == NavigationEndpointRole.Start
            ? NavigationRayChainConstraint.FinishAt(address)
            : NavigationRayChainConstraint.SeedAt(address);
        _rayWork.Begin(new NavigationRayRequest(
            _world,
            _store,
            _graph,
            _evaluator.Profile,
            _evaluator.AreaPolicy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            allowTransitions: false,
            start,
            end,
            _role == NavigationEndpointRole.Start
                ? NavigationRayEndpointAllowance.StartPrefix
                : NavigationRayEndpointAllowance.DestinationSuffix,
            constraint));
        return false;
    }

    private NavigationEndpointResolutionStatus AdvanceCandidateRay()
    {
        NavigationRayStatus rayStatus = _rayWork.Advance(_meter);
        if (rayStatus == NavigationRayStatus.Pending)
            return Status;
        if (rayStatus is NavigationRayStatus.Success or NavigationRayStatus.Blocked)
        {
            if (!TryMergeRayDependencies())
                return Finish(NavigationEndpointResolutionStatus.CapacityExceeded);
            if (rayStatus == NavigationRayStatus.Success)
                AcceptCandidate(_pendingCandidate);
            else
                TryAcceptPendingVolumeFallback();
            _pendingCandidate = default;
            _rayWork.Reset();
            return Status;
        }
        return Finish(rayStatus switch
        {
            NavigationRayStatus.BudgetExceeded =>
                NavigationEndpointResolutionStatus.BudgetExceeded,
            NavigationRayStatus.CostOverflow =>
                NavigationEndpointResolutionStatus.CostOverflow,
            NavigationRayStatus.CapacityExceeded =>
                NavigationEndpointResolutionStatus.CapacityExceeded,
            _ => NavigationEndpointResolutionStatus.Stale
        });
    }

    private bool TryMergeRayDependencies()
    {
        NavigationDependencyWorkspace dependencies = _rayWorkspace.Dependencies;
        for (int i = 0; i < dependencies.ComponentCount; i++)
        {
            if (!_workspace.TryRecordComponent(dependencies.Components[i]))
                return false;
        }
        for (int i = 0; i < dependencies.PageCount; i++)
        {
            GraphPageDependencyAddress page = dependencies.Pages[i];
            if (!_workspace.TryRecordPage(page.MapId, page.PageIndex))
                return false;
        }
        return true;
    }

    private void TryAcceptPendingVolumeFallback()
    {
        TraversalMedia volumeMedia = _pendingCandidate.Media & TraversalMedia.AnyVolume;
        if (volumeMedia == TraversalMedia.None
            || !_graph.TryGetNodeState(
                _pendingCandidate.Node,
                out NavigationNodeState state)
            || !state.TryGetCenteredVolumeFootAnchor(
                _evaluator.Profile.Shape.Height,
                out Vector3d footAnchor)
            || !Vector3d.TryGetDistance(
                _endpoint.Position,
                footAnchor,
                out Fixed64 distance))
        {
            return;
        }

        TraversalMedium medium = FirstMedium(volumeMedia);
        if (CanBeatCurrentResult(_pendingCandidate.Address, distance, medium))
        {
            AcceptCandidate(new NavigationResolvedEndpoint(
                _pendingCandidate.Node,
                _pendingCandidate.Address,
                volumeMedia,
                medium,
                footAnchor,
                distance));
        }
    }

    private bool CanBeatCurrentResult(
        NavigationCellAddress address,
        Fixed64 distance,
        TraversalMedium medium) =>
        !_hasResult
        || distance < Result.ResolutionDistance
        || (distance == Result.ResolutionDistance
            && (address.CompareTo(Result.Address) < 0
                || (address.Equals(Result.Address)
                    && (int)medium < (int)Result.ResolutionMedium)));

    private void AcceptCandidate(NavigationResolvedEndpoint candidate)
    {
        Result = candidate;
        _hasResult = true;
    }

    private bool TryGetBounds(out Vector3d minimum, out Vector3d maximum)
    {
        Fixed64 distance = _endpoint.Resolution == EndpointResolutionPolicy.Strict
            ? Fixed64.Zero
            : _endpoint.MaxResolutionDistance;
        if (!Fixed64.TrySubtract(_endpoint.Position.X, distance, out Fixed64 minX)
            || !Fixed64.TrySubtract(_endpoint.Position.Y, distance, out Fixed64 minY)
            || !Fixed64.TrySubtract(_endpoint.Position.Z, distance, out Fixed64 minZ)
            || !Fixed64.TryAdd(_endpoint.Position.X, distance, out Fixed64 maxX)
            || !Fixed64.TryAdd(_endpoint.Position.Y, distance, out Fixed64 maxY)
            || !Fixed64.TryAdd(_endpoint.Position.Z, distance, out Fixed64 maxZ))
        {
            minimum = default;
            maximum = default;
            return false;
        }
        minimum = new Vector3d(minX, minY, minZ);
        maximum = new Vector3d(maxX, maxY, maxZ);
        return true;
    }

    private NavigationEndpointResolutionStatus Finish(
        NavigationEndpointResolutionStatus status)
    {
        _pendingCandidate = default;
        _rayWork.Reset();
        if (status == NavigationEndpointResolutionStatus.Success)
        {
            status = RecordResultComponents();
        }
        if ((status == NavigationEndpointResolutionStatus.Success
                || (status == NavigationEndpointResolutionStatus.InvalidEndpoint
                    && (_workspace.PageCount != 0 || _workspace.ComponentCount != 0)))
            && !AreDependenciesCurrent())
        {
            status = NavigationEndpointResolutionStatus.Stale;
        }
        if (_world.ChangeSequence != _worldChangeSequence)
            status = NavigationEndpointResolutionStatus.Stale;
        Status = status;
        if (status != NavigationEndpointResolutionStatus.Success)
            Result = default;
        return Status;
    }

    private bool AreDependenciesCurrent()
    {
        if (_world.ChangeSequence != _worldChangeSequence)
            return false;
        NavigationWorldGraph current = _store.Current;
        NavigationAreaPolicy areaPolicy = _evaluator.AreaPolicy;
        if (_graph.AreaCatalog.TryGet(
                areaPolicy.Key,
                out NavigationAreaPolicy? expectedPolicy)
            && expectedPolicy != null
            && (!current.AreaCatalog.TryGet(
                    areaPolicy.Key,
                    out NavigationAreaPolicy? currentPolicy)
                || currentPolicy == null
                || !currentPolicy.ContentEquals(expectedPolicy)))
        {
            return false;
        }
        NavigationDependencyWorkspace dependencies = _workspace.Dependencies;
        for (int i = 0; i < dependencies.ComponentCount; i++)
        {
            NavigationSurfaceComponentKey key = dependencies.Components[i];
            if (!_graph.TryGetComponentDependency(key, out GraphComponentDependency prior)
                || !current.TryGetComponentDependency(key, out GraphComponentDependency next)
                || !prior.Equals(next))
            {
                return false;
            }
        }
        for (int i = 0; i < dependencies.PageCount; i++)
        {
            GraphPageDependencyAddress address = dependencies.Pages[i];
            if (!_graph.TryGetPageDependency(address, out GraphPageDependency prior)
                || !current.TryGetPageDependency(address, out GraphPageDependency next)
                || !prior.Equals(next))
            {
                return false;
            }
        }
        return _world.ChangeSequence == _worldChangeSequence;
    }

    private bool TryQualifyCandidate(
        NavigationNodeRef node,
        NavigationNodeState state,
        out TraversalMedia qualifyingMedia,
        out TraversalMedium resolutionMedium,
        out Vector3d footAnchor,
        out Fixed64 distance,
        out bool consumedVolumeWork)
    {
        qualifyingMedia = TraversalMedia.None;
        resolutionMedium = default;
        footAnchor = default;
        distance = default;
        consumedVolumeWork = false;
        if ((_media & TraversalMedia.Solid) != 0
            && _evaluator.TryGetPassableNodeState(node, out _))
        {
            if (!ConsiderMedium(
                    TraversalMedium.Solid,
                    state.FootAnchor,
                    ref qualifyingMedia,
                    ref resolutionMedium,
                    ref footAnchor,
                    ref distance))
            {
                Finish(NavigationEndpointResolutionStatus.CostOverflow);
                return false;
            }
        }

        TraversalMedia requestedVolume = _media & TraversalMedia.AnyVolume;
        if (requestedVolume != TraversalMedia.None)
        {
            consumedVolumeWork = true;
            NavigationVolumeAnchorStatus volumeStatus = _volumeEvaluator.Evaluate(
                node,
                requestedVolume,
                _meter,
                _workspace.Dependencies,
                out Vector3d volumeFoot,
                out TraversalMedia volumeMedia);
            if (volumeStatus is NavigationVolumeAnchorStatus.BudgetExceeded
                or NavigationVolumeAnchorStatus.CostOverflow
                or NavigationVolumeAnchorStatus.CapacityExceeded
                or NavigationVolumeAnchorStatus.Stale)
            {
                Finish(volumeStatus switch
                {
                    NavigationVolumeAnchorStatus.BudgetExceeded =>
                        NavigationEndpointResolutionStatus.BudgetExceeded,
                    NavigationVolumeAnchorStatus.CostOverflow =>
                        NavigationEndpointResolutionStatus.CostOverflow,
                    NavigationVolumeAnchorStatus.CapacityExceeded =>
                        NavigationEndpointResolutionStatus.CapacityExceeded,
                    _ => NavigationEndpointResolutionStatus.Stale
                });
                return false;
            }
            for (TraversalMedium medium = TraversalMedium.Gas;
                 medium <= TraversalMedium.Liquid;
                 medium++)
            {
                if ((volumeMedia & NavigationCell.ToMedia(medium)) != 0
                    && !ConsiderMedium(
                        medium,
                        volumeFoot,
                        ref qualifyingMedia,
                        ref resolutionMedium,
                        ref footAnchor,
                        ref distance))
                {
                    Finish(NavigationEndpointResolutionStatus.CostOverflow);
                    return false;
                }
            }
        }
        return qualifyingMedia != TraversalMedia.None;
    }

    private bool ConsiderMedium(
        TraversalMedium medium,
        Vector3d anchor,
        ref TraversalMedia qualifyingMedia,
        ref TraversalMedium resolutionMedium,
        ref Vector3d footAnchor,
        ref Fixed64 distance)
    {
        if (!Vector3d.TryGetDistance(_endpoint.Position, anchor, out Fixed64 candidateDistance))
            return false;
        if (_endpoint.Resolution == EndpointResolutionPolicy.NearestNavigable
            && candidateDistance > _endpoint.MaxResolutionDistance)
        {
            return true;
        }
        qualifyingMedia |= NavigationCell.ToMedia(medium);
        if (resolutionMedium == TraversalMedium.Unknown
            || candidateDistance < distance
            || (candidateDistance == distance && (int)medium < (int)resolutionMedium))
        {
            resolutionMedium = medium;
            footAnchor = anchor;
            distance = candidateDistance;
        }
        return true;
    }

    private NavigationEndpointResolutionStatus RecordResultComponents()
    {
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            if ((Result.Media & NavigationCell.ToMedia(medium)) == 0)
                continue;
            if (!_graph.TryGetSurfaceComponent(
                    Result.Address,
                    medium,
                    out NavigationSurfaceComponentKey componentKey,
                    out _))
            {
                return NavigationEndpointResolutionStatus.Stale;
            }
            if (!_workspace.TryRecordComponent(componentKey))
                return NavigationEndpointResolutionStatus.CapacityExceeded;
        }
        return NavigationEndpointResolutionStatus.Success;
    }

    private static TraversalMedium FirstMedium(TraversalMedia media)
    {
        if ((media & TraversalMedia.Solid) != 0)
            return TraversalMedium.Solid;
        if ((media & TraversalMedia.Gas) != 0)
            return TraversalMedium.Gas;
        return TraversalMedium.Liquid;
    }

}
