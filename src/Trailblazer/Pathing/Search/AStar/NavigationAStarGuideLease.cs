//=======================================================================
// NavigationAStarGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
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
            if (_store != null || _payloadLease != null)
                throw new InvalidOperationException("The A* guide lease is already active.");
            _generation = NavigationGenerationCounter.Advance(
                _generation,
                "The A* guide lease generation is exhausted.");
            _store = store;
            _payloadLease = payloadLease;
            _currentWaypointOrdinal = 0;
            _currentMedium = payloadLease.Payload.Key.StartMedium;
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

    internal NavigationAStarQueryStatus TryAdvanceWaypoint(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? TryAdvanceWaypointUnderLock()
                : NavigationAStarQueryStatus.Stale;
        }
    }

    internal NavigationAStarQueryStatus TryGetCurrentStep(
        long generation,
        out NavigationGuideStep step)
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
            return true;
        }
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
            return NavigationAStarQueryStatus.CapacityExceeded;
        using (graphLease)
        {
            payload = payloadLease.Payload;
            if (!graphLease.Graph.IsDependencyCurrent(payload.Dependencies)
                || !_owner.IsWorldCurrent(payload)
                || !store.Current.IsDependencyCurrent(payload.Dependencies)
                || !_owner.IsWorldCurrent(payload))
            {
                payload = null!;
                return MarkStaleUnderLock();
            }
            return NavigationAStarQueryStatus.Success;
        }
    }

    private NavigationAStarQueryStatus MarkStaleUnderLock()
    {
        _status = NavigationAStarQueryStatus.Stale;
        return _status;
    }
}
