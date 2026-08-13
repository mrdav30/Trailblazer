//=======================================================================
// NavSteering.Requests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Public Interface

    /// <summary>
    /// Starts or replaces the active steering request.
    /// </summary>
    /// <param name="pathRequest">The movement request that defines the desired origin and destination.</param>
    /// <param name="groupId">Optional shared group identifier used to preserve formation offsets between nearby members.</param>
    public virtual void ApplyPathRequest(IPathRequest? pathRequest, int groupId = -1)
    {
        // assume the object is being controlled
        if (pathRequest == null || !pathRequest.HasValidEndpoints)
        {
            TrailblazerLogger.Channel.Warn($"Invalid path request applied: {pathRequest}");
            Arrive();
            return;
        }

        if (_context == null)
            BindContext(pathRequest.Context);
        else if (!ReferenceEquals(_context, pathRequest.Context))
            throw new InvalidOperationException("NavSteering cannot accept a path request from a different TrailblazerWorldContext.");

        _hasLineOfSightPath = false;
        _isAtDestination = false;

        _stoppedFrameCount = 0;
        _isStuck = false;
        _stuckFrameCount = 0;

        _shouldMove = true;
        // NOTE: destination can be an exact point within a voxel, not neccesarily the voxel position
        _requestedDestination = pathRequest.TargetPosition;
        _destination = _requestedDestination;

        ReleaseTrailGuide();
        _currentRequest = pathRequest;
        _lastUnitSize = pathRequest.UnitSize;

        _repathTries = 0;
        _shouldRequestPathThisFrame = true;
        PublishRouteTopology(hasResolvedTopology: false, usesGuideTopology: false, requestsClimbIntent: false, force: true);

        AddToMovementGroup(groupId);
        UpdateMovementGroupState(pathRequest.Origin, true);

        Events.OnMoveRequestApplied?.Invoke();
    }

    /// <summary>
    /// Applies a short delay to prevent auto-stopping behavior for a few frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTimeForContext;

    /// <summary>
    /// Replaces the current guide used for guided steering.
    /// </summary>
    /// <param name="guide">The guide to follow, or <c>null</c> to clear guided movement.</param>
    public void SetTrailGuide(IGuide? guide)
    {
        _trailGuide = guide;
        _shouldRequestPathThisFrame = _trailGuide != null;
    }

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
    /// during <see cref="GetHeading(ISteer)"/>.
    /// </remarks>
    /// <param name="vessel">The current steering owner whose position, radius, and stable id should seed the coordinator.</param>
    public void PrewarmMovementGroup(ISteer vessel)
    {
        SwiftThrowHelper.ThrowIfNull(vessel, nameof(vessel));

        if (!ShouldMove || !IsInGroup || _currentRequest == null)
            return;

        MovementGroups.Prewarm(
            _movementGroupSession,
            vessel.GlobalId,
            _requestedDestination,
            vessel.Position,
            _agentRadius);
    }

    #endregion
}
