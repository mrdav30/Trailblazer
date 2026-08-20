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
        Fixed64 resolutionDistance)
    {
        Node = node;
        Address = address;
        ResolutionDistance = resolutionDistance;
    }

    internal NavigationNodeRef Node { get; }
    internal NavigationCellAddress Address { get; }
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
    private TraversalIntent _intent;
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
        TraversalIntent intent)
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
        _intent = intent;
        _evaluator = new TraversalEvaluator(
            graph,
            profile,
            areaPolicy,
            intent.CurrentMedium == TraversalMedium.Unknown
                ? TraversalMedium.Solid
                : intent.CurrentMedium);
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
        _intent = default;
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
        if (!_evaluator.TryGetPassableNodeState(node, out NavigationNodeState state))
            return true;
        if (!Vector3d.TryGetDistance(
                _endpoint.Position,
                state.FootAnchor,
                out Fixed64 distance))
        {
            Finish(NavigationEndpointResolutionStatus.CostOverflow);
            return false;
        }
        if (_endpoint.Resolution == EndpointResolutionPolicy.NearestNavigable
            && distance > _endpoint.MaxResolutionDistance)
        {
            return true;
        }

        var address = new NavigationCellAddress(mapId, candidate.VoxelIndex);
        if (!_graph.TryGetSurfaceComponent(address, out _, out _))
        {
            Finish(NavigationEndpointResolutionStatus.Stale);
            return false;
        }
        if (!CanBeatCurrentResult(address, distance))
            return true;

        var resolved = new NavigationResolvedEndpoint(node, address, distance);
        if (_endpoint.Resolution == EndpointResolutionPolicy.Strict)
        {
            AcceptCandidate(resolved);
            return true;
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
            _intent,
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

    private bool CanBeatCurrentResult(
        NavigationCellAddress address,
        Fixed64 distance) =>
        !_hasResult
        || distance < Result.ResolutionDistance
        || (distance == Result.ResolutionDistance
            && address.CompareTo(Result.Address) < 0);

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
            if (!_graph.TryGetSurfaceComponent(
                    Result.Address,
                    out NavigationSurfaceComponentKey componentKey,
                    out _))
            {
                status = NavigationEndpointResolutionStatus.Stale;
            }
            else if (!_workspace.TryRecordComponent(componentKey))
            {
                status = NavigationEndpointResolutionStatus.CapacityExceeded;
            }
        }
        if ((status == NavigationEndpointResolutionStatus.Success
                || (status == NavigationEndpointResolutionStatus.InvalidEndpoint
                    && (_workspace.PageCount != 0 || _workspace.ComponentCount != 0)))
            && !AreDependenciesCurrent())
        {
            status = NavigationEndpointResolutionStatus.Stale;
        }
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
}
