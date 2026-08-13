//=======================================================================
// NavSteering.Simulation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Simulation Lifecycle

    /// <summary>
    /// Initializes the object by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    protected virtual void OnInitialize(Fixed64 radius)
    {
        UpdateOwnerRadius(radius);

        LeaveMovementGroup();

        _stoppedFrameCount = 0;
        _autoStopFrameCount = 0;

        StopMultiplier = DefaultDirectStop;

        _shouldRequestPathThisFrame = false;
        _hasLineOfSightPath = false;
        _shouldMove = false;

        _isStuck = false;
        _stuckFrameCount = 0;
        _repathTries = 0;

        _isAtDestination = false;

        _currentRequest = null;
        _trailGuide = null;
        _requestedDestination = Vector3d.Zero;
        _movementGroupSession.Reset();
        _movementGroupMode = MovementGroupTravelMode.None;
        _currentRouteHasResolvedTopology = false;
        _currentRouteUsesGuideTopology = false;
        _currentRouteRequestsClimbIntent = false;
        _currentRouteTopologyVersion = 0;
    }

    internal virtual void UpdateOwnerRadius(Fixed64 radius)
    {
        // Fatter objects can afford to land imprecisely
        _agentRadius = radius;
        _closingDistance = FixedMath.Round(radius + ResolveVoxelSize());
    }

    internal void Reset()
    {
        ReleaseTrailGuide();
        OnInitialize(_agentRadius);
    }

    private Fixed64 ResolveVoxelSize()
    {
        if (_context != null)
            return _context.VoxelSize;
        if (_currentRequest != null)
            return _currentRequest.Context.VoxelSize;

        return GridWorld.DefaultRectangularCellSize;
    }

    private TrailblazerWorldContext ResolveContext()
    {
        TrailblazerWorldContext? context = _context ?? _currentRequest?.Context;
        if (context != null)
        {
            PathRequestContextResolver.ThrowIfUnusable(context);
            return context;
        }

        throw new InvalidOperationException("NavSteering requires an explicit TrailblazerWorldContext.");
    }

    private MovementGroupCoordinatorState MovementGroups => ResolveContext().Navigation.MovementGroups;

    private void RemoveMovementGroupSession()
    {
        ResolveContext().Navigation.MovementGroups.Remove(_movementGroupSession);
    }

    private int StuckFrameThresholdForContext => ResolveFrameRate() / 4;

    private int AutoPauseStopTimeForContext => ResolveFrameRate() / 8;

    private int ResolveFrameRate()
    {
        if (_context != null)
            return _context.FrameRate;
        if (_currentRequest != null)
            return _currentRequest.Context.FrameRate;

        return TrailblazerClock.DefaultFrameRate;
    }

    /// <summary>
    /// Called every simulation step to handle agent steering and movement logic.
    /// </summary>
    public virtual Vector3d GetHeading(ISteer vessel)
    {
        CacheOwner(vessel);

        if (!CanMove)
            return Vector3d.Zero;

        if (!ShouldMove || IsAtDestination)
            return FinalizeIdleHeading(vessel.Speed);

        if (!TryEnsureCurrentRequest(out Vector3d heading))
            return heading;

        bool usesVolumeGuidance = UsesVolumeGuidance();
        UpdateMovementGroupState(vessel.Position);

        if (!TryPrepareMovementPathForHeading(vessel.Position, usesVolumeGuidance))
            return Vector3d.Zero;

        UpdateTargetDirection(vessel);
        if (ShouldArriveWithoutTrailGuide())
        {
            Arrive();
            return Vector3d.Zero;
        }

        if (!CheckStuckStatus(vessel.Position, vessel.Speed, vessel.StuckThresholdSpeed))
        {
            TrailblazerLogger.DebugChannel.Info($"Stuck agent arriving!");
            Arrive();
            return Vector3d.Zero;
        }

        UpdateTrailGuideProgress(vessel.Acceleration, vessel.Speed);
        return FinalizeHeadingFrame();
    }

    /// <summary>
    /// Periodically called to initiate a pathfinding query based on the current position and destination.
    /// Note: This will run once on the next `Simulate` call after calling `ApplyPathRequest`
    /// </summary>
    protected virtual bool ValidateMovementPath(Vector3d origin)
    {
        // Unit-size change detection must run before the shouldRequestPath gate. Without this,
        // external TrySetUnitSize calls between frames are silently ignored when
        // _shouldRequestPathThisFrame is already false, and no repath ever triggers.
        if (_currentRequest!.UnitSize != _lastUnitSize)
        {
            _lastUnitSize = _currentRequest.UnitSize;
            _shouldRequestPathThisFrame = true;
        }

        if (!_shouldRequestPathThisFrame)
            return true;
        _shouldRequestPathThisFrame = false;

        // update origin
        bool ok = _currentRequest.TrySetOrigin(origin);
        if (!ok || !_currentRequest.HasValidEndpoints)
        {
            PublishRouteTopology(hasResolvedTopology: false, usesGuideTopology: false, requestsClimbIntent: false);
            TrailblazerLogger.Channel.Warn($"Path request is using invalid endpoints.");
            return false;
        }

        // shortcut if no path needed
        if (_currentRequest.HasZeroDisplacement)
        {
            PublishRouteTopology(hasResolvedTopology: true, usesGuideTopology: false, requestsClimbIntent: false);
            return _repathTries == 0;
        }

        if (_currentRequest is VolumePathRequest volumeRequest)
        {
            _hasLineOfSightPath = IsVolumeDestinationInSight(
                _currentRequest.Context,
                origin,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints,
                volumeRequest.Medium,
                _currentRequest.StartNode,
                _currentRequest.EndNode);

            _pathCheckCooldown = PathRecheckCooldownFrames;
            if (_hasLineOfSightPath)
            {
                ReleaseTrailGuide();
                PublishRouteTopology(hasResolvedTopology: true, usesGuideTopology: false, requestsClimbIntent: false);
                return true;
            }
        }
        else
        {
            _hasLineOfSightPath = IsDestinationInSight(
                _currentRequest.Context,
                origin,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints);
            if (_hasLineOfSightPath)
            {
                PublishRouteTopology(hasResolvedTopology: true, usesGuideTopology: false, requestsClimbIntent: false);
                return true;  // no path required
            }
        }

        // request guide
        ReleaseTrailGuide();
        _pathCheckCooldown = PathRecheckCooldownFrames;
        if (!_currentRequest.IsValid || !_currentRequest.Context.Guides.RequestGuide(_currentRequest, out _trailGuide))
        {
            PublishRouteTopology(hasResolvedTopology: false, usesGuideTopology: false, requestsClimbIntent: false);
            TrailblazerLogger.Channel.Warn($"Unable to retrieve a guide from {origin} to {Destination}.");
            return false;
        }

        PublishRouteTopology(
            hasResolvedTopology: true,
            usesGuideTopology: true,
            requestsClimbIntent: GuidedClimbIntentResolver.Resolve(_currentRequest));
        return true;
    }

    /// <summary>
    /// Computes the steering direction toward the destination or along the path.
    /// </summary>
    protected virtual Vector3d FindTargetDirection(Vector3d position)
    {
        Vector3d targetDirection = Vector3d.Zero;
        if (HasLineOfSightPath)
            targetDirection = Destination - position;
        else if (HasTrailGuide)
        {
            if (_trailGuide is IWaypointGuide waypointGuide)
                targetDirection = waypointGuide.GetCurrentWaypointDirection(position);
            else
                _trailGuide!.TryGetMovementDirection(position, out targetDirection);
        }

        if (targetDirection == Vector3d.Zero)
        {
            TrailblazerLogger.DebugChannel.Info($"No viable movement direction found.");
            return Vector3d.Zero;
        }

        // This is now the direction we want to be travelling in
        return targetDirection.NormalizeInPlace(out _distanceToTarget);
    }

    /// <summary>
    /// Returns true if we’re within closing distance _and_ our heading has flipped,
    /// or if we’re very close relative to voxel size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldAdvanceToNextWaypoint()
    {
        return (_distanceToTarget < _closingDistance
                    && Vector3d.Dot(TargetDirection, LastTargetDirection) < Fixed64.Epsilon)
            || _distanceToTarget < _closingDistance * ResolveVoxelSize();
    }

    /// <summary>
    /// Evaluates the agent's current movement direction and velocity, updating stuck and arrival state.
    /// </summary>
    protected virtual bool CheckStuckStatus(
        Vector3d position,
        Fixed64 speed,
        Fixed64 stuckThreshold)
    {
        if (!CanAutoStop)
            return true;

        if (stuckThreshold <= Fixed64.Zero || speed >= stuckThreshold)
            return ResetStuckStatus();

        _stuckFrameCount++;
        if (_stuckFrameCount <= StuckFrameThresholdForContext)
            return true;

        return _repathTries < StuckRepathTries
            ? TryRecoverFromStuck(position)
            : DeclareHardStuck();
    }

    private Vector3d FinalizeIdleHeading(Fixed64 speed)
    {
        _targetDirection = Vector3d.Zero;
        if (speed <= Fixed64.Epsilon)
            _stoppedFrameCount++;

        return FinalizeHeadingFrame();
    }

    private bool TryEnsureCurrentRequest(out Vector3d heading)
    {
        if (_currentRequest != null)
        {
            heading = Vector3d.Zero;
            return true;
        }

        Arrive();
        heading = TargetDirection;
        return false;
    }

    private bool TryPrepareMovementPathForHeading(Vector3d position, bool usesVolumeGuidance)
    {
        if ((CanPathfind || usesVolumeGuidance) && !ValidateMovementPath(position))
        {
            HandleInvalidPath("Invalid path detected!");
            return false;
        }

        RefreshLineOfSightState(position);
        if (usesVolumeGuidance && !HasLineOfSightPath && !HasTrailGuide && !ValidateMovementPath(position))
        {
            HandleInvalidPath("Invalid volume path detected!");
            return false;
        }

        return true;
    }

    private void RefreshLineOfSightState(Vector3d position)
    {
        if (_pathCheckCooldown > 0)
            return;

        if (_currentRequest is VolumePathRequest volumeRequest)
        {
            _hasLineOfSightPath = IsVolumeDestinationInSight(
                _currentRequest.Context,
                position,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints,
                volumeRequest.Medium,
                _currentRequest.StartNode,
                _currentRequest.EndNode);

            if (_hasLineOfSightPath)
            {
                ReleaseTrailGuide();
                PublishRouteTopology(hasResolvedTopology: true, usesGuideTopology: false, requestsClimbIntent: false);
            }
        }
        else
        {
            IPathRequest currentRequest = _currentRequest!;
            _hasLineOfSightPath = IsDestinationInSight(
                currentRequest.Context,
                position,
                Destination,
                currentRequest.UnitSize,
                currentRequest.AllowUnwalkableEndpoints);

            if (_hasLineOfSightPath)
                PublishRouteTopology(hasResolvedTopology: true, usesGuideTopology: false, requestsClimbIntent: false);
        }

        _pathCheckCooldown = PathRecheckCooldownFrames;
    }

    private void HandleInvalidPath(string debugMessage)
    {
        TrailblazerLogger.DebugChannel.Info($"{debugMessage}");
        Events.OnInvalidPath?.Invoke();
        Arrive();
    }

    private void UpdateTargetDirection(ISteer vessel)
    {
        _lastTargetDirection = _targetDirection;
        _targetDirection = FindTargetDirection(vessel.Position);
        _targetDirection += ComputeCombinedSteering(
            vessel.Position,
            vessel.Velocity,
            vessel.Speed,
            vessel.Radius,
            vessel.GlobalId);
    }

    private bool ShouldArriveWithoutTrailGuide()
    {
        if (HasTrailGuide)
            return false;

        Fixed64 moveAmount = FixedMath.Clamp01(TargetDirection.Magnitude);
        bool reachedTarget = _distanceToTarget < _closingDistance * GetActiveStopMultiplier();
        bool noInput = moveAmount == Fixed64.Zero;
        return reachedTarget || (!IsStuck && noInput);
    }

    private void UpdateTrailGuideProgress(Vector3d acceleration, Fixed64 speed)
    {
        if (TargetDirection == Vector3d.Zero)
            return;

        if (_trailGuide is IWaypointGuide waypointGuide && ShouldAdvanceToNextWaypoint())
            waypointGuide.AdvanceWaypoint();

        if (HasTrailGuide)
            SetDeceleration(acceleration, speed);
    }

    private Vector3d FinalizeHeadingFrame()
    {
        _autoStopFrameCount--;
        _pathCheckCooldown--;

        Events.OnStartTraversal?.Invoke(TargetDirection);
        return TargetDirection;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ResetStuckStatus()
    {
        _isStuck = false;
        _stuckFrameCount = 0;
        _repathTries = 0;
        return true;
    }

    private bool TryRecoverFromStuck(Vector3d position)
    {
        _hasLineOfSightPath = false;

        if (IsInGroup)
            LeaveMovementGroup();

        if (TryApplyFallbackDirection(position))
            return true;

        PreparePathRetry();
        _repathTries++;
        return true;
    }

    private bool TryApplyFallbackDirection(Vector3d position)
    {
        if (!HasTrailGuide || _trailGuide!.TryGetFallbackDirection(position, out Vector3d fallback) == false)
            return false;

        _targetDirection = fallback;
        _repathTries++;
        _stuckFrameCount = 0;
        return true;
    }

    private void PreparePathRetry()
    {
        _targetDirection = Vector3d.Zero;
        _shouldRequestPathThisFrame = true;
        DisposeCurrentTrailGuide();
    }

    private bool DeclareHardStuck()
    {
        _isStuck = true;
        DisposeCurrentTrailGuide();
        Events.OnIsStuck?.Invoke();
        return false;
    }

    private void DisposeCurrentTrailGuide()
    {
        if (_trailGuide == null)
            return;

        (_currentRequest?.Context ?? ResolveContext()).Guides.ReturnGuide(_trailGuide, true);
        _trailGuide = null;
    }

    /// <summary>
    /// Adjusts the target direction to decelerate the object as it approaches its destination based on the specified
    /// acceleration and current speed.
    /// </summary>
    /// <remarks>
    /// This method is intended to be overridden in derived classes to customize deceleration behavior.
    /// It modulates the target direction to ensure smooth slowing as the object nears its target.
    /// </remarks>
    /// <param name="acceleration">
    /// The acceleration vector used to determine the deceleration rate.
    /// If the vector is zero, a default braking power is used.
    /// </param>
    /// <param name="speed">The current speed of the object, used to calculate the distance required to slow down.</param>
    protected virtual void SetDeceleration(Vector3d acceleration, Fixed64 speed)
    {
        // Scaling direction before passing to the motor lets us
        // modulate movement before acceleration is applied
        Fixed64 deceleration = acceleration != Vector3d.Zero
            ? acceleration.Magnitude
            : BrakingPower;
        Fixed64 slowDistance = speed / deceleration;
        if (DistanceToTarget > Fixed64.Epsilon && DistanceToTarget <= slowDistance)
        {
            Fixed64 closingSpeed = DistanceToTarget / slowDistance;
            _targetDirection *= closingSpeed; // reduce magnitude = slow down
        }
    }

    /// <summary>
    /// Triggers the arrival event and resets internal movement tracking.
    /// </summary>
    public void Arrive()
    {
        StopMove();

        ReleaseTrailGuide();
        _currentRequest = null;
        _requestedDestination = Vector3d.Zero;
        _distanceToTarget = Fixed64.Zero;
        _isAtDestination = true;
        _destination = Vector3d.Zero;
        _targetDirection = Vector3d.Zero;

        Events.OnArrive?.Invoke();
    }

    /// <summary>
    /// Resets the movement and pathfinding logic, halting the agent.
    /// </summary>
    public virtual void StopMove()
    {
        if (!_shouldMove)
            return;

        _autoStopFrameCount = 0;
        _stuckFrameCount = 0;
        _stoppedFrameCount = 0;

        _shouldMove = false;
        _shouldRequestPathThisFrame = false;
        _hasLineOfSightPath = false;
        PublishRouteTopology(hasResolvedTopology: false, usesGuideTopology: false, requestsClimbIntent: false, force: true);
        LeaveMovementGroup();

        Events.OnStopMove?.Invoke();
    }

    #endregion
}
