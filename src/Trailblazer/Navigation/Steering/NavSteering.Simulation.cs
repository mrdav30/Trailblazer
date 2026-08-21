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

        _currentQuery = null;
        _navigationGuideLease = null;
        _navigationFlowFieldLease = null;
        _requestedDestination = Vector3d.Zero;
        _movementGroupSession.Reset();
        _movementGroupMode = MovementGroupTravelMode.None;
    }

    internal virtual void UpdateOwnerRadius(Fixed64 radius)
    {
        // Fatter objects can afford to land imprecisely
        _agentRadius = radius;
        _closingDistance = FixedMath.Round(_agentRadius + ResolveVoxelSize());
    }

    internal void Reset()
    {
        ReleaseNavigationGuidance();
        OnInitialize(_agentRadius);
    }

    private Fixed64 ResolveVoxelSize()
    {
        if (_context != null)
            return _context.VoxelSize;
        return GridWorld.DefaultRectangularCellSize;
    }

    private TrailblazerWorldContext ResolveContext()
    {
        TrailblazerWorldContext? context = _context;
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
        return TrailblazerClock.DefaultFrameRate;
    }

    /// <summary>
    /// Called every simulation step to handle agent steering and movement logic.
    /// </summary>
    public virtual Vector3d GetHeading(
        ISteer vessel,
        out NavigationTransitionInstruction? pendingTransition)
    {
        pendingTransition = null;
        CacheOwner(vessel);

        if (!CanMove)
            return Vector3d.Zero;

        if (!ShouldMove || IsAtDestination)
            return FinalizeIdleHeading(vessel.Speed);

        if (!TryEnsureCurrentRequest(out Vector3d heading))
            return heading;

        Vector3d pathPosition = vessel.Position
            + Vector3d.Down * vessel.BodyShape.RootToFootOffsetY;
        UpdateMovementGroupState(vessel.Position);

        if (!TryPrepareMovementPathForHeading(pathPosition))
            return Vector3d.Zero;

        UpdateTargetDirection(
            vessel,
            pathPosition,
            out pendingTransition,
            out bool isAStarTransitionApproach);
        if (!ShouldMove || IsAtDestination)
            return Vector3d.Zero;
        if (_currentQuery.HasValue && _shouldRequestPathThisFrame)
            return FinalizeHeadingFrame();

        if (ShouldArriveWithoutNavigationGuidance())
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

        if (UpdateNavigationGuidanceProgress(
                vessel.Acceleration,
                vessel.Speed,
                isAStarTransitionApproach))
        {
            Arrive();
            return Vector3d.Zero;
        }
        return FinalizeHeadingFrame();
    }

    /// <summary>
    /// Periodically called to initiate a pathfinding query based on the current position and destination.
    /// Note: This runs once on the next simulation after a guided trek request is applied.
    /// </summary>
    protected virtual bool ValidateMovementPath(Vector3d origin)
        => ValidateGraphMovementPath(origin);

    private bool ValidateGraphMovementPath(Vector3d origin)
    {
        if (_hasLineOfSightPath && !_shouldRequestPathThisFrame)
            return true;
        if (_currentQuery!.Value.Algorithm == PathAlgorithm.FlowField)
            return ValidateGraphFlowMovementPath(origin);

        if (_navigationGuideLease?.Status == NavigationGuideStatus.Stale)
            PreparePathRetry();

        if (!_shouldRequestPathThisFrame)
            return _navigationGuideLease?.Status == NavigationGuideStatus.Success;

        _shouldRequestPathThisFrame = false;
        PathQuery currentQuery = _currentQuery!.Value;
        PathQuery query = currentQuery.WithStartState(
            origin,
            currentQuery.Traversal.StartMedium);
        _currentQuery = query;
        _requestedDestination = query.End.Position;
        _destination = _requestedDestination;
        if (TryHandleInitialGraphDirectTravel(query, origin))
            return true;
        ReleaseNavigationGuidance();
        _pathCheckCooldown = PathRecheckCooldownFrames;

        NavigationGuideStatus status = ResolveContext().Guides.RequestGuide(
            query,
            out NavigationGuideLease? lease);
        if (status != NavigationGuideStatus.Success || lease == null)
        {
            lease?.Dispose();
            if (status is NavigationGuideStatus.Stale
                or NavigationGuideStatus.CapacityExceeded)
            {
                PreparePathRetry();
                return true;
            }

            return false;
        }

        _navigationGuideLease = lease;
        return true;
    }

    private bool ValidateGraphFlowMovementPath(Vector3d origin)
    {
        PathQuery currentQuery = _currentQuery!.Value;
        NavigationGuideStatus currentStatus = _navigationFlowFieldLease?.Status
            ?? NavigationGuideStatus.Stale;
        if (_navigationFlowFieldLease != null
            && currentStatus is NavigationGuideStatus.Stale
                or NavigationGuideStatus.CapacityExceeded)
        {
            PreparePathRetry();
        }

        if (!_shouldRequestPathThisFrame)
            return currentStatus == NavigationGuideStatus.Success;

        _shouldRequestPathThisFrame = false;
        PathQuery query = currentQuery.WithStartState(
            origin,
            currentQuery.Traversal.StartMedium);
        _currentQuery = query;
        _requestedDestination = query.End.Position;
        _destination = _requestedDestination;
        if (TryHandleInitialGraphDirectTravel(query, origin))
            return true;
        ReleaseNavigationGuidance();
        _pathCheckCooldown = PathRecheckCooldownFrames;

        NavigationGuideStatus status = ResolveContext().Guides.RequestFlowField(
            query,
            out NavigationFlowFieldLease? lease);
        if (status != NavigationGuideStatus.Success || lease == null)
        {
            lease?.Dispose();
            if (status is NavigationGuideStatus.Stale
                or NavigationGuideStatus.CapacityExceeded)
            {
                PreparePathRetry();
                return true;
            }

            return false;
        }

        _navigationFlowFieldLease = lease;
        return true;
    }

    private bool TryHandleInitialGraphDirectTravel(PathQuery query, Vector3d origin)
    {
        if ((query.Traversal.TargetMedia
                & NavigationCell.ToMedia(query.Traversal.StartMedium)) == 0)
        {
            _hasLineOfSightPath = false;
            return false;
        }
        NavigationRayStatus status = ResolveContext().Guides.TryGetDirectHeading(
            query,
            origin,
            out _);
        if (status == NavigationRayStatus.Success)
        {
            _hasLineOfSightPath = true;
            ReleaseNavigationGuidance();
            _pathCheckCooldown = PathRecheckCooldownFrames;
            return true;
        }

        _hasLineOfSightPath = false;
        if (status != NavigationRayStatus.Stale)
            return false;
        PreparePathRetry();
        return true;
    }

    /// <summary>
    /// Computes the steering direction toward the destination or along the path.
    /// </summary>
    protected virtual Vector3d FindTargetDirection(
        Vector3d position,
        out NavigationTransitionInstruction? pendingTransition)
    {
        pendingTransition = null;
        Vector3d targetDirection = Vector3d.Zero;
        if (HasLineOfSightPath)
            targetDirection = Destination - position;
        else if (_navigationFlowFieldLease is NavigationFlowFieldLease flowGuide)
        {
            NavigationGuideStatus status = flowGuide.TrySample(
                position,
                ResolveContext().Settings.GuideSampleBudget,
                out NavigationFlowSample sample);
            targetDirection = sample.Heading;
            if (status is NavigationGuideStatus.Stale
                or NavigationGuideStatus.CapacityExceeded)
            {
                PreparePathRetry();
                return Vector3d.Zero;
            }
            if (status == NavigationGuideStatus.BudgetExceeded)
                return Vector3d.Zero;
            if (status == NavigationGuideStatus.LocalRecoveryRequired)
                return Vector3d.Zero;
            else if (status != NavigationGuideStatus.Success)
            {
                HandleInvalidPath("Invalid graph flow path detected!");
                return Vector3d.Zero;
            }
            else
            {
                if (sample.HasTransition)
                {
                    pendingTransition = sample.Transition;
                    return Vector3d.Zero;
                }
                if (targetDirection == Vector3d.Zero)
                {
                    Arrive();
                    return Vector3d.Zero;
                }
            }
        }
        else if (_navigationGuideLease != null)
        {
            NavigationGuideLease guide = _navigationGuideLease.Value;
            NavigationGuideStatus status = guide.TryGetCurrentStep(
                out NavigationGuideStep step);
            if (status == NavigationGuideStatus.Stale)
            {
                PreparePathRetry();
                return Vector3d.Zero;
            }
            if (status != NavigationGuideStatus.Success)
                return Vector3d.Zero;

            if (step.HasTransition)
            {
                targetDirection = step.Position - position;
                if (targetDirection == Vector3d.Zero)
                {
                    pendingTransition = step.Transition;
                    return Vector3d.Zero;
                }
            }
            else
            {
                targetDirection = step.Position - position;
            }
            if (targetDirection == Vector3d.Zero)
            {
                if (IsAtFinalWaypoint(guide))
                {
                    ReleaseNavigationGuidance();
                    return Vector3d.Zero;
                }

                status = guide.TryAdvanceStep();
                if (status == NavigationGuideStatus.Stale)
                {
                    PreparePathRetry();
                    return Vector3d.Zero;
                }
                if (status != NavigationGuideStatus.Success)
                    return Vector3d.Zero;

                status = guide.TryGetCurrentStep(out step);
                if (status == NavigationGuideStatus.Stale)
                {
                    PreparePathRetry();
                    return Vector3d.Zero;
                }
                if (status != NavigationGuideStatus.Success)
                    return Vector3d.Zero;

                if (step.HasTransition)
                {
                    targetDirection = step.Position - position;
                    if (targetDirection == Vector3d.Zero)
                    {
                        pendingTransition = step.Transition;
                        return Vector3d.Zero;
                    }
                }
                else
                {
                    targetDirection = step.Position - position;
                }
            }
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
        if (_currentQuery.HasValue)
        {
            heading = Vector3d.Zero;
            return true;
        }

        Arrive();
        heading = TargetDirection;
        return false;
    }

    private bool TryPrepareMovementPathForHeading(Vector3d position)
    {
        if (CanPathfind && !ValidateMovementPath(position))
        {
            HandleInvalidPath("Invalid path detected!");
            return false;
        }

        RefreshLineOfSightState(position);
        return true;
    }

    private void RefreshLineOfSightState(Vector3d position)
    {
        if (_pathCheckCooldown > 0)
            return;

        if (_currentQuery is not PathQuery currentQuery)
            return;
        bool wasDirect = _hasLineOfSightPath;
        PathQuery query = currentQuery.WithStartState(
            position,
            currentQuery.Traversal.StartMedium);
        if ((query.Traversal.TargetMedia
                & NavigationCell.ToMedia(query.Traversal.StartMedium)) == 0)
        {
            _hasLineOfSightPath = false;
            _pathCheckCooldown = PathRecheckCooldownFrames;
            return;
        }
        NavigationRayStatus status = ResolveContext().Guides.TryGetDirectHeading(
            query,
            position,
            out _);
        if (status == NavigationRayStatus.Success)
        {
            _currentQuery = query;
            _hasLineOfSightPath = true;
            ReleaseNavigationGuidance();
        }
        else if (status == NavigationRayStatus.Stale || wasDirect)
        {
            _hasLineOfSightPath = false;
            PreparePathRetry();
        }
        _pathCheckCooldown = PathRecheckCooldownFrames;
    }

    private void HandleInvalidPath(string debugMessage)
    {
        TrailblazerLogger.DebugChannel.Info($"{debugMessage}");
        Events.OnInvalidPath?.Invoke();
        Arrive();
    }

    private void UpdateTargetDirection(
        ISteer vessel,
        Vector3d pathPosition,
        out NavigationTransitionInstruction? pendingTransition,
        out bool isAStarTransitionApproach)
    {
        _lastTargetDirection = _targetDirection;
        _targetDirection = FindTargetDirection(
            pathPosition,
            out pendingTransition);
        isAStarTransitionApproach = pendingTransition == null
            && IsCurrentAStarTransitionStep();
        if (_targetDirection == Vector3d.Zero || !ShouldMove || IsAtDestination)
            return;
        _targetDirection += ComputeCombinedSteering(
            vessel.Position,
            vessel.Velocity,
            vessel.Speed,
            vessel.Radius,
            vessel.GlobalId);
    }

    private bool IsCurrentAStarTransitionStep()
    {
        if (_navigationGuideLease is not NavigationGuideLease guide)
            return false;

        return guide.TryGetCurrentStep(out NavigationGuideStep step)
            == NavigationGuideStatus.Success
            && step.HasTransition;
    }

    private bool ShouldArriveWithoutNavigationGuidance()
    {
        if (HasNavigationGuidance)
            return false;

        Fixed64 moveAmount = FixedMath.Clamp01(TargetDirection.Magnitude);
        bool reachedTarget = _distanceToTarget < _closingDistance * GetActiveStopMultiplier();
        bool noInput = moveAmount == Fixed64.Zero;
        return reachedTarget || (!IsStuck && noInput);
    }

    private bool UpdateNavigationGuidanceProgress(
        Vector3d acceleration,
        Fixed64 speed,
        bool isAStarTransitionApproach)
    {
        if (TargetDirection == Vector3d.Zero)
            return false;

        if (!isAStarTransitionApproach && ShouldAdvanceToNextWaypoint())
        {
            if (_navigationGuideLease != null)
            {
                NavigationGuideLease guide = _navigationGuideLease.Value;
                if (IsAtFinalWaypoint(guide))
                {
                    if (_distanceToTarget
                        < _closingDistance * GetActiveStopMultiplier())
                    {
                        ReleaseNavigationGuidance();
                        return true;
                    }
                }
                else
                {
                    NavigationGuideStatus status = guide.TryAdvanceStep();
                    if (status == NavigationGuideStatus.Stale)
                        PreparePathRetry();
                }
            }
        }

        if (HasNavigationGuidance)
            SetDeceleration(acceleration, speed);

        return false;
    }

    private static bool IsAtFinalWaypoint(NavigationGuideLease guide) =>
        guide.StepCount > 0
        && guide.CurrentStepIndex == guide.StepCount - 1;

    internal NavigationGuideStatus CompletePendingTransition(
        in NavigationTransitionInstruction instruction)
    {
        NavigationGuideStatus status = _navigationGuideLease is NavigationGuideLease guide
            ? guide.CompletePendingTransition(instruction)
            : _navigationFlowFieldLease is NavigationFlowFieldLease flow
                ? flow.CompletePendingTransition(instruction)
                : NavigationGuideStatus.Stale;
        if (status == NavigationGuideStatus.Success && _currentQuery is PathQuery query)
        {
            _currentQuery = query.WithStartState(
                query.Start.Position,
                instruction.DestinationMedium);
        }
        return status;
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

        PreparePathRetry();
        _repathTries++;
        return true;
    }

    private void PreparePathRetry()
    {
        _targetDirection = Vector3d.Zero;
        _shouldRequestPathThisFrame = true;
        DisposeCurrentNavigationGuidance();
    }

    private bool DeclareHardStuck()
    {
        _isStuck = true;
        DisposeCurrentNavigationGuidance();
        Events.OnIsStuck?.Invoke();
        return false;
    }

    private void DisposeCurrentNavigationGuidance()
    {
        ReleaseNavigationGuidance();
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

        _distanceToTarget = Fixed64.Zero;
        _isAtDestination = true;
        _targetDirection = Vector3d.Zero;

        Events.OnArrive?.Invoke();
    }

    /// <summary>
    /// Resets the movement and pathfinding logic, halting the agent.
    /// </summary>
    public virtual void StopMove()
    {
        bool wasMoving = _shouldMove;
        ReleaseNavigationGuidance();
        _pendingTransitionOwner?.NotifySteeringSessionEnded();
        _currentQuery = null;
        _requestedDestination = Vector3d.Zero;
        _destination = Vector3d.Zero;
        _shouldMove = false;
        _shouldRequestPathThisFrame = false;
        _hasLineOfSightPath = false;
        LeaveMovementGroup();
        if (!wasMoving)
            return;

        _autoStopFrameCount = 0;
        _stuckFrameCount = 0;
        _stoppedFrameCount = 0;

        Events.OnStopMove?.Invoke();
    }

    #endregion
}
