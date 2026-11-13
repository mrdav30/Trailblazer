//=======================================================================
// NavigationAStarGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one immutable A* payload and a guide-local waypoint cursor.</summary>
internal sealed class NavigationAStarGuideLease
{
    private readonly NavigationAStarPayloadCache _owner;
    private readonly object _sync = new();
    private NavigationWorldGraphStore? _store;
    private NavigationAStarPayloadLease? _payloadLease;
    private NavigationAStarQueryStatus _status;
    private int _currentWaypointOrdinal;
    private TraversalMedium _currentMedium;
    private long _generation;
    private NavigationAStarConnectorProof? _connector;
    private bool _connectorRequired;

    internal NavigationAStarGuideLease(NavigationAStarPayloadCache owner)
    {
        _owner = owner;
    }

    internal long Generation
    {
        get { lock (_sync) return _generation; }
    }

    internal NavigationAStarGuideLease? NextPooled { get; set; }

    internal void Bind(
        NavigationWorldGraphStore store,
        NavigationAStarPayloadLease payloadLease)
    {
        lock (_sync)
        {
            // The cache rents only detached guides and does not return one to its pool
            // until TryDetach has cleared this complete ownership tuple.
            Debug.Assert(_store == null && _payloadLease == null);
            _generation = NavigationGenerationCounter.Advance(
                _generation,
                "The A* guide lease generation is exhausted.");
            _store = store;
            _payloadLease = payloadLease;
            _currentWaypointOrdinal = 0;
            NavigationAStarPayload payload = payloadLease.Payload;
            _currentMedium = payload is NavigationPlanarGuidePayload ? payload.GuidePoints[0].Medium : payload.Key.StartMedium;
            _status = NavigationAStarQueryStatus.Success;
        }
    }

    internal NavigationAStarQueryStatus GetStatus(long generation)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationAStarQueryStatus.Stale;
            return _status == NavigationAStarQueryStatus.Success
                ? ValidateCurrentPayloadUnderLock(out _)
                : _status;
        }
    }

    internal int GetCurrentWaypointOrdinal(long generation)
    {
        lock (_sync)
            return IsGenerationActiveUnderLock(generation) ? _currentWaypointOrdinal : -1;
    }

    internal int GetWaypointCount(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _payloadLease!.Payload.GuidePoints.Length
                : 0;
        }
    }

    internal Fixed64 GetTotalCost(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _payloadLease!.Payload.Cost
                : Fixed64.Zero;
        }
    }

    internal bool IsPlanarArrivalInRange(long generation, Vector2d accepted, string mapId, Fixed64 radius)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation)
                || _payloadLease!.Payload is not NavigationPlanarRegionGuidePayload region
                || _currentWaypointOrdinal != region.GuidePoints.Length - 1)
                return false;
            var point = region.GuidePoints[_currentWaypointOrdinal];
            // Authored actions occupy their source waypoint and always have a following destination.
            Debug.Assert(!point.HasTransition);
            return point.Address.MapId == mapId
                && NavigationPlanarWork.IsInsideGoalRegion(accepted, (region.GoalCenter, radius));
        }
    }

    internal NavigationAStarQueryStatus TryAdvanceWaypoint(long generation)
    {
        long released = 0;
        NavigationAStarQueryStatus status;
        lock (_sync)
        {
            status = IsGenerationActiveUnderLock(generation)
                ? TryAdvanceWaypointUnderLock()
                : NavigationAStarQueryStatus.Stale;
            if (IsGenerationActiveUnderLock(generation) && _currentWaypointOrdinal != 0)
            {
                released = DropConnectorUnderLock();
                _connectorRequired = false;
            }
        }
        _owner.ReleaseConnectorProof(released);
        return status;
    }

    internal bool HasConnector(long generation)
    {
        lock (_sync)
            return IsGenerationActiveUnderLock(generation) && _connectorRequired;
    }

    internal NavigationGuideStatus BeginConnector(long generation, out NavigationGuideStep step)
    {
        long released = 0;
        NavigationGuideStatus result;
        lock (_sync)
        {
            step = default;
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationGuideStatus.Stale;
            result = NavigationGuideStatusMapper.ToPublic(TryGetCurrentStepUnderLock(out step));
            if (result != NavigationGuideStatus.Success)
                return result;
            if (_currentWaypointOrdinal != 0 || step.HasTransition)
                return NavigationGuideStatus.Unsupported;
            _connectorRequired = true;
            NavigationAStarConnectorProof? prior = _connector;
            if (prior != null && (prior.Anchor != step.Address || prior.Position != step.Position
                || prior.Medium != step.Medium || !prior.IsCurrent(_owner.World, _store!)))
                result = NavigationGuideStatus.Stale;
            // Drop before replacement allocation; failure must produce no heading.
            released = DropConnectorUnderLock();
        }
        _owner.ReleaseConnectorProof(released);
        return result;
    }

    internal NavigationGuideStatus InstallConnector(long generation, NavigationAStarConnectorProof proof,
        ref long reservation)
    {
        _owner.CommitConnectorProof(ref reservation);
        NavigationGuideStatus status = NavigationGuideStatus.Stale;
        lock (_sync)
        {
            if (IsGenerationActiveUnderLock(generation) && _connectorRequired && _connector == null
                && _currentWaypointOrdinal == 0)
            {
                status = NavigationGuideStatusMapper.ToPublic(TryGetCurrentStepUnderLock(out NavigationGuideStep step));
                if (status == NavigationGuideStatus.Success && !step.HasTransition && step.Address == proof.Anchor
                    && step.Position == proof.Position && step.Medium == proof.Medium
                    && proof.IsCurrent(_owner.World, _store!))
                {
                    _connector = proof;
                    return NavigationGuideStatus.Success;
                }
                if (status == NavigationGuideStatus.Success) status = NavigationGuideStatus.Stale;
            }
        }
        _owner.ReleaseConnectorProof(proof.RetainedBytes);
        return status;
    }

    private long DropConnectorUnderLock()
    {
        long bytes = _connector?.RetainedBytes ?? 0;
        _connector = null;
        return bytes;
    }

    internal NavigationAStarQueryStatus TryGetCurrentStep(long generation, out NavigationGuideStep step)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
            {
                step = default;
                return NavigationAStarQueryStatus.Stale;
            }
            return TryGetCurrentStepUnderLock(out step);
        }
    }

    internal bool TryGetNextOrdinaryPoint(long generation, out NavigationAStarGuidePoint point)
    {
        point = default;
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation)
                || ValidateCurrentPayloadUnderLock(out var payload) != NavigationAStarQueryStatus.Success)
                return false;
            int next = _currentWaypointOrdinal + 1;
            if (next >= payload.GuidePoints.Length || payload.GuidePoints[_currentWaypointOrdinal].HasTransition
                || payload.GuidePoints[next].HasTransition)
                return false;
            point = payload.GuidePoints[next];
            return true;
        }
    }

    internal NavigationAStarQueryStatus GetSideViewStage(long generation, out NavigationAStarGuidePoint source,
        out NavigationAStarGuidePoint target)
    {
        source = target = default;
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationAStarQueryStatus.Stale;
            var status = ValidateCurrentPayloadUnderLock(out var payload);
            if (status != NavigationAStarQueryStatus.Success)
                return status;
            target = payload.GuidePoints[_currentWaypointOrdinal];
            source = _currentWaypointOrdinal == 0 ? target : payload.GuidePoints[_currentWaypointOrdinal - 1];
            return NavigationAStarQueryStatus.Success;
        }
    }

    internal NavigationGuideStatus TryGetCurrentStep(
        long generation,
        Vector3d connectorOrigin,
        out NavigationGuideStep step)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
            {
                step = default;
                return NavigationGuideStatus.Stale;
            }
            NavigationGuideStatus status = NavigationGuideStatusMapper.ToPublic(TryGetCurrentStepUnderLock(out step));
            if (status == NavigationGuideStatus.Success && _connectorRequired
                && (_connector == null || _connector.Foot != connectorOrigin
                    || !_connector.IsCurrent(_owner.World, _store!)))
            {
                // A hook invalidated this heading's proof, not necessarily its shared route.
                // Keep ownership until the next charged connector validation turn.
                step = default;
                return NavigationGuideStatus.LocalRecoveryRequired;
            }
            return status;
        }
    }

    internal NavigationAStarQueryStatus CompletePendingTransition(
        long generation,
        in NavigationTransitionInstruction instruction)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
                return NavigationAStarQueryStatus.Stale;
            NavigationAStarQueryStatus status = ValidateCurrentPayloadUnderLock(
                out NavigationAStarPayload payload);
            if (status != NavigationAStarQueryStatus.Success)
                return status;
            NavigationAStarGuidePoint current =
                payload.GuidePoints[_currentWaypointOrdinal];
            if (!current.HasTransition
                || !instruction.MatchesCompletion(
                    this,
                    unchecked((ulong)generation),
                    _currentWaypointOrdinal))
            {
                return NavigationAStarQueryStatus.Stale;
            }
            _currentMedium = payload
                .TransitionInstructions[current.TransitionOrdinal]
                .DestinationMedium;
            if (_currentWaypointOrdinal + 1 < payload.GuidePoints.Length)
                _currentWaypointOrdinal++;
            return NavigationAStarQueryStatus.Success;
        }
    }

    internal bool TryDetach(
        long generation,
        out NavigationAStarPayloadLease payloadLease)
    {
        long released;
        lock (_sync)
        {
            payloadLease = null!;
            if (!IsGenerationActiveUnderLock(generation))
                return false;
            _store = null;
            payloadLease = _payloadLease!;
            _payloadLease = null;
            _currentWaypointOrdinal = 0;
            _currentMedium = TraversalMedium.Unknown;
            _status = NavigationAStarQueryStatus.Pending;
            released = DropConnectorUnderLock();
            _connectorRequired = false;
        }
        _owner.ReleaseConnectorProof(released);
        return true;
    }

    internal void Dispose(long generation) => _owner.ReturnGuide(this, generation);

    private bool IsGenerationActiveUnderLock(long generation) =>
        generation > 0
        && generation == _generation
        && _store != null
        && _payloadLease != null;

    private NavigationAStarQueryStatus TryGetCurrentStepUnderLock(
        out NavigationGuideStep step)
    {
        step = default;
        NavigationAStarQueryStatus status = ValidateCurrentPayloadUnderLock(
            out NavigationAStarPayload payload);
        if (status != NavigationAStarQueryStatus.Success)
            return status;
        NavigationAStarGuidePoint current = payload.GuidePoints[_currentWaypointOrdinal];
        if (!current.HasTransition)
        {
            step = new NavigationGuideStep(
                current.Address,
                current.Position,
                current.Medium,
                default,
                hasTransition: false);
            return NavigationAStarQueryStatus.Success;
        }
        NavigationTransitionInstruction stamped = payload
            .TransitionInstructions[current.TransitionOrdinal]
            .WithCompletionStamp(this, unchecked((ulong)_generation), _currentWaypointOrdinal);
        step = new NavigationGuideStep(
            current.Address,
            current.Position,
            current.Medium,
            stamped,
            hasTransition: true);
        return NavigationAStarQueryStatus.Success;
    }

    private NavigationAStarQueryStatus TryAdvanceWaypointUnderLock()
    {
        NavigationAStarQueryStatus status = ValidateCurrentPayloadUnderLock(
            out NavigationAStarPayload payload);
        if (status != NavigationAStarQueryStatus.Success)
            return status;
        NavigationAStarGuidePoint current = payload.GuidePoints[_currentWaypointOrdinal];
        if (current.HasTransition)
            return NavigationAStarQueryStatus.Pending;
        if (_currentWaypointOrdinal + 1 < payload.GuidePoints.Length)
            _currentWaypointOrdinal++;
        return NavigationAStarQueryStatus.Success;
    }

    private NavigationAStarQueryStatus ValidateCurrentPayloadUnderLock(
        out NavigationAStarPayload payload)
    {
        payload = null!;
        if (_status != NavigationAStarQueryStatus.Success)
            return _status;
        NavigationWorldGraphStore store = _store!;
        NavigationAStarPayloadLease payloadLease = _payloadLease!;
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        if (graphLease == null)
            return payloadLease.Payload is NavigationPlanarGuidePayload && store.IsDisposed
                ? MarkStaleUnderLock()
                : NavigationAStarQueryStatus.CapacityExceeded;
        using (graphLease)
        {
            payload = payloadLease.Payload;
            if (payload is NavigationPlanarGuidePayload topDown)
            {
                if (topDown.IsCurrent(_owner.World, store))
                    return NavigationAStarQueryStatus.Success;
                payload = null!;
                return MarkStaleUnderLock();
            }
            NavigationWorldGraph validatedGraph = graphLease.Graph;
            if (validatedGraph.IsDependencyCurrent(payload.Dependencies)
                && _owner.IsWorldCurrent(payload))
            {
                // Read after the first world check: it can wait while a new root
                // is published. Only an identical immutable root reuses this proof.
                NavigationWorldGraph currentGraph = store.Current;
                if ((ReferenceEquals(currentGraph, validatedGraph)
                        || currentGraph.IsDependencyCurrent(payload.Dependencies))
                    && NavigationEndpointProofWork.IsCurrent(store, payloadLease.EndpointProof)
                    && _owner.IsWorldCurrent(payload))
                {
                    return NavigationAStarQueryStatus.Success;
                }
            }
            payload = null!;
            return MarkStaleUnderLock();
        }
    }

    private NavigationAStarQueryStatus MarkStaleUnderLock()
    {
        _status = NavigationAStarQueryStatus.Stale;
        return _status;
    }
}
