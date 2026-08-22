//=======================================================================
// NavSteering.Requests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using FixedMathSharp;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Public Interface

    /// <summary>
    /// Starts or replaces one graph-backed A* or flow-field steering request.
    /// </summary>
    internal virtual void ApplyPathQuery(PathQuery query, int groupId = -1)
    {
        BeginPathSession(
            query.End.Position,
            query.Start.Position,
            groupId,
            query);
    }

    private void BeginPathSession(
        Vector3d destination,
        Vector3d origin,
        int groupId,
        PathQuery query)
    {
        _hasLineOfSightPath = false;
        _isAtDestination = false;
        _stoppedFrameCount = 0;
        _isStuck = false;
        _stuckFrameCount = 0;
        _shouldMove = true;
        _requestedDestination = destination;
        _destination = destination;

        ReleaseNavigationGuidance();
        _currentQuery = query;

        _repathTries = 0;
        _shouldRequestPathThisFrame = true;
        AddToMovementGroup(groupId);
        UpdateMovementGroupState(origin, query.Agent.Shape.Radius, true);
        Events.OnMoveRequestApplied?.Invoke();
    }

    /// <summary>
    /// Applies a short delay to prevent auto-stopping behavior for a few frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTimeForContext;

    /// <summary>
    /// Assigns this steering session to a movement group.
    /// </summary>
    /// <param name="groupId">A non-negative group identifier. Negative values remove the current group assignment.</param>
    public void AddToMovementGroup(int groupId)
    {
        if (groupId < 0)
        {
            LeaveMovementGroup();
            return;
        }

        if (MovementGroupID >= 0 && MovementGroupID != groupId)
            LeaveMovementGroup();

        MovementGroupID = groupId;
        _movementGroupMode = MovementGroupTravelMode.Individual;
    }

    /// <summary>
    /// Removes this steering session from its current movement group.
    /// </summary>
    public void LeaveMovementGroup()
    {
        RemoveMovementGroupSession();
        MovementGroupID = -1;
        GroupIndex = -1;
        _movementGroupMode = MovementGroupTravelMode.None;
        _destination = _requestedDestination;
    }

    /// <summary>
    /// Rebuilds this steering session's shared movement-group membership from the current runtime owner state.
    /// </summary>
    /// <remarks>
    /// Call this after loading multiple grouped steering sessions when you want the coordinator warmed
    /// before the next simulation frame. If it is skipped, grouped steering will still recover lazily
    /// during <see cref="GetHeading"/>.
    /// </remarks>
    /// <param name="vessel">The current steering owner whose position, radius, and stable id should seed the coordinator.</param>
    public void PrewarmMovementGroup(ISteer vessel)
    {
        SwiftThrowHelper.ThrowIfNull(vessel, nameof(vessel));

        if (!ShouldMove || !IsInGroup || !_currentQuery.HasValue)
            return;

        MovementGroups.Prewarm(
            _movementGroupSession,
            vessel.GlobalId,
            _requestedDestination,
            vessel.Position,
            vessel.Radius);
    }

    #endregion
}
