using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

/// <summary>
/// Handles agent steering and path navigation logic by coordinating pathfinding, movement, 
/// and group behaviors within a lockstep simulation. Supports both direct line-of-sight travel 
/// and guided path traversal using IGuide implementations like AStar or FlowField.
/// </summary>
public class NavSteering
{
    #region Constants & Defaults

    /// <summary>
    /// Default range to scan for other agents when calculating steering behaviors.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupFactor = (Fixed64)10;

    /// <summary>
    /// Default padding radius used to maintain space between nearby agents.
    /// </summary>
    protected static readonly Fixed64 DefaultAvoidFactor = (Fixed64)3;

    /// <summary>
    /// Default weights used for group-based steering calculations (separation, alignment, cohesion).
    /// </summary>
    protected static readonly GroupBehaviorWeights DefaultBehaviorWeights = new()
    {
        Separation = (Fixed64)2,
        Alignment = Fixed64.Half,
        Cohesion = (Fixed64)0.2f,
        Avoidance = Fixed64.One
    };

    /// <summary>
    /// Default multiplier used to determine proximity tolerance when stopping at a destination.
    /// </summary>
    public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;

    /// <summary>
    /// Number of frames between pathfinding LOS rechecks.
    /// </summary>
    protected const int DefaultPathRecheckCooldown = 16;

    /// <summary>
    /// Number of frames an agent must be below the movement threshold to be considered stuck.
    /// </summary>
    protected static int StuckFrameThreshold => TrailblazerManager.FrameRate / 4;

    /// <summary>
    /// Maximum number of repath attempts before declaring the agent fully stuck.
    /// </summary>
    protected const int StuckRepathTries = 4;

    /// <summary>
    /// Number of frames to wait before allowing auto-stop again.
    /// </summary>
    protected static int AutoPauseStopTime => TrailblazerManager.FrameRate / 8;

    /// <summary>
    /// Default braking factor applied when decelerating or stopping motion.
    /// </summary>
    public static readonly Fixed64 DefaultBrakingPower = (Fixed64)0.15d;

    /// <summary>
    /// Group fallback stop tolerance used when a formation breaks apart near the goal.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupIndividualStop = Fixed64.One;

    #endregion

    #region Runtime State - Pathfinding

    /// <summary>
    /// Disable if unit doesn't need to find path, i.e. flying
    /// </summary>
    public bool CanPathfind = true;

    /// <summary>
    /// The final destination this agent is attempting to reach.
    /// </summary>
    public Vector3d Destination { get; protected set; }

    private Vector3d _requestedDestination;

    private Fixed64 _lastUnitSize;

    /// <inheritdoc cref="DefaultPathRecheckCooldown"/>
    public int PathRecheckCooldownFrames = DefaultPathRecheckCooldown;

    public Vector3d TargetDirection { get; protected set; }

    public Vector3d LastTargetDirection { get; protected set; }

    /// <summary>
    /// The pathfinding configuration used for the current movement request, including size, and type.
    /// </summary>
    private IPathRequest _currentRequest;

    /// <inheritdoc cref="_currentRequest"/>
    public IPathRequest CurrentRequest => _currentRequest;

    /// <summary>
    /// Current guide used to compute the desired path or flow.
    /// </summary>
    private IGuide _trailGuide;

    /// <inheritdoc cref="_trailGuide"/>
    public IGuide TrailGuide => _trailGuide;

    /// <summary>
    /// Whether the navigator is following a path or guide to the destination.
    /// </summary>
    public bool ShouldMove { get; protected set; }

    /// <summary>
    /// Whether the agent has become stuck and exhausted repathing attempts.
    /// </summary>
    public bool IsStuck { get; protected set; }

    /// <summary>
    /// True if the agent can reach the destination without requiring a path.
    /// </summary>
    public bool HasLineOfSightPath { get; protected set; }

    /// <summary>
    /// Current pathfinding search status.
    /// </summary>
    protected bool _shouldRequestPathThisFrame;

    protected int _pathCheckCooldown;

    /// <summary>
    /// How far we move each update
    /// </summary>
    protected Fixed64 _distanceToTarget;

    /// <inheritdoc cref="_distanceToTarget"/>
    public Fixed64 DistanceToTarget => _distanceToTarget;

    /// <summary>
    /// How far away the agent stops from the target
    /// </summary>
    private Fixed64 _closingDistance;

    /// <summary>
    /// Indicates whether the agent is actively following a guide path with queued waypoints.
    /// </summary>
    public bool HasTrailGuide => !HasLineOfSightPath && _trailGuide != null;

    /// <summary>
    /// Has this unit arrived at destination?
    /// </summary>
    public bool IsAtDestination { get; protected set; }

    #endregion

    #region Runtime State - Steering & Motion

    /// <summary>
    /// Whether this agent can currently move.
    /// </summary>
    public bool CanMove = true;

    /// <summary>
    /// Number of consecutive frames where movement failed and deceleration is occurring.
    /// </summary>
    public int StoppedFrameCount { get; protected set; }

    /// <summary>
    /// Internal cooldown before the agent can automatically stop again (used for bursty movement).
    /// </summary>
    protected int _autoStopFrameCount;

    /// <summary>
    /// Indicates whether the agent is currently eligible for automatic stopping logic.
    /// </summary>
    public bool CanAutoStop => _autoStopFrameCount <= 0;

    /// <summary>
    /// Number of attempts to repath after getting stuck.
    /// </summary>
    protected int _repathTries;

    /// <summary>
    /// Number of frames the agent has failed movement checks (used for stuck detection).
    /// </summary>
    protected int _stuckFrameCount;

    /// <summary>
    /// Multiplier used to determine how close the agent must be to its target before stopping.
    /// </summary>
    public Fixed64 StopMultiplier { get; set; } = DefaultDirectStop;

    /// <summary>
    /// How far to look for group neighbors (separation/alignment/cohesion).
    /// </summary>
    public Fixed64 GroupFactor { get; set; } = DefaultGroupFactor;

    /// <summary>
    /// How far to look for obstacles to avoid.
    /// </summary>
    public Fixed64 AvoidFactor { get; set; } = DefaultAvoidFactor;

    /// <summary>
    /// Weights for separating, aligning, and cohesion in group behavior.
    /// Avoidance weight is baked in here as well.
    /// </summary>
    public GroupBehaviorWeights BehaviorWeights { get; set; } = DefaultBehaviorWeights;

    /// <summary>
    /// Friction-based deceleration rate used when slowing down on ground surfaces.
    /// </summary>
    public Fixed64 BrakingPower { get; set; } = DefaultBrakingPower;

    private Fixed64 _agentRadius;

    private readonly MovementGroupSession _movementGroupSession = new();

    private MovementGroupTravelMode _movementGroupMode;

    #endregion

    #region Events

    /// <summary>
    /// Container for delegate events that fire on pathfinding state changes (start, stop, arrive).
    /// </summary>
    public NavSteeringEvents Events { get; protected set; } = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new <see cref="NavSteering"/> instance and initializes it with the provided navigator.
    /// </summary>
    /// <param name="radius">The radius of the navigator entity that this controller will manage.</param>
    /// <returns>A new instance of <see cref="NavSteering"/>.</returns>
    public static NavSteering CreateNew(Fixed64 radius) => new(radius);

    /// <summary>
    /// Initializes a new, empty instance of the <see cref="NavSteering"/> class.
    /// </summary>
    public NavSteering() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavSteering"/> class.
    /// </summary>
    /// <param name="radius">The radius of the navigator entity that this controller will manage.</param>
    public NavSteering(Fixed64 radius) => OnInitialize(radius);

    #endregion

    #region Group Properties

    public int MovementGroupID
    {
        get => _movementGroupSession.GroupId;
        set => _movementGroupSession.GroupId = value;
    }

    public int GroupIndex
    {
        get => _movementGroupSession.GroupIndex;
        protected set => _movementGroupSession.GroupIndex = value;
    }

    public bool IsInGroup => MovementGroupID != -1;

    #endregion

    #region Public Interface

    /// <summary>
    /// Starts or replaces the active steering request.
    /// </summary>
    /// <param name="pathRequest">The path request that defines the traversable start and end voxels.</param>
    /// <param name="groupId">Optional shared group identifier used to preserve formation offsets between nearby members.</param>
    public virtual void ApplyPathRequest(IPathRequest pathRequest, int groupId = -1)
    {
        // assume the navigator is being controlled
        if (pathRequest == null || !pathRequest.HasValidEndpoints)
        {
            GridForgeLogger.Warn($"Invalid path request applied: {pathRequest}");
            Arrive();
            return;
        }

        HasLineOfSightPath = false;
        IsAtDestination = false;

        StoppedFrameCount = 0;
        IsStuck = false;
        _stuckFrameCount = 0;

        ShouldMove = true;
        // NOTE: destination can be an exact point within a voxel, not neccesarily the voxel position
        _requestedDestination = pathRequest.TargetPosition;
        Destination = _requestedDestination;

        _currentRequest = pathRequest;
        _lastUnitSize = pathRequest.UnitSize;

        _repathTries = 0;
        _shouldRequestPathThisFrame = true;

        AddToMovementGroup(groupId);
        UpdateMovementGroupState(pathRequest.StartNode.WorldPosition, true);

        Events.OnMoveRequestApplied?.Invoke();
    }

    /// <summary>
    /// Applies a short delay to prevent auto-stopping behavior for a few frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PauseAutoStop() => _autoStopFrameCount = AutoPauseStopTime;

    /// <summary>
    /// Replaces the current guide used for guided steering.
    /// </summary>
    /// <param name="guide">The guide to follow, or <c>null</c> to clear guided movement.</param>
    public void SetTrailGuide(IGuide guide)
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
        MovementGroupCoordinator.Remove(_movementGroupSession);
        MovementGroupID = -1;
        GroupIndex = -1;
        _movementGroupMode = MovementGroupTravelMode.None;
        Destination = _requestedDestination;
    }

    #endregion

    #region Simulation Lifecycle

    /// <summary>
    /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    public virtual void OnInitialize(Fixed64 radius)
    {
        // Fatter objects can afford to land imprecisely
        _agentRadius = radius;
        _closingDistance = FixedMath.Round(radius + GlobalGridManager.VoxelSize);

        LeaveMovementGroup();

        StoppedFrameCount = 0;
        _autoStopFrameCount = 0;

        StopMultiplier = DefaultDirectStop;

        _shouldRequestPathThisFrame = false;
        HasLineOfSightPath = false;
        ShouldMove = false;

        IsStuck = false;
        _stuckFrameCount = 0;
        _repathTries = 0;

        IsAtDestination = false;

        _currentRequest = null;
        _trailGuide = null;
        _requestedDestination = Vector3d.Zero;
        _movementGroupSession.Reset();
        _movementGroupMode = MovementGroupTravelMode.None;
    }

    /// <summary>
    /// Called every simulation step to handle agent steering and movement logic.
    /// </summary>
    public virtual Vector3d GetHeading(ISteer navigator)
    {
        CacheOwner(navigator);

        if (!CanMove)
            return Vector3d.Zero;

        if (ShouldMove && !IsAtDestination)
        {
            if (_currentRequest == null)
            {
                Arrive();
                return TargetDirection;
            }

            UpdateMovementGroupState(navigator.Position);

            // check if agent has to pathfind, otherwise straight path to rely on destination
            if (CanPathfind)
            {
                if (!ValidateMovementPath(navigator.Position))
                {
#if DEBUG
                    Debug.WriteLine("Invalid path detected!");
#endif
                    Events.OnInvalidPath?.Invoke();
                    Arrive();
                    return Vector3d.Zero;
                }
            }

            if (_pathCheckCooldown <= 0)
            {
                HasLineOfSightPath = IsDestinationInSight(
                    navigator.Position,
                    Destination,
                    _currentRequest.UnitSize,
                    _currentRequest.AllowUnwalkable);

                _pathCheckCooldown = PathRecheckCooldownFrames;
            }

            LastTargetDirection = TargetDirection;
            TargetDirection = FindTargetDirection(navigator.Position);

            // Calculate steering, flocking, and avoidance forces
            TargetDirection += ComputeCombinedSteering(
                navigator.Position,
                navigator.Velocity,
                navigator.Speed,
                navigator.Radius,
                navigator.GlobalId);

            // Check if we're close enough to stop moving
            Fixed64 moveAmount = FixedMath.Clamp01(TargetDirection.x.Abs() + TargetDirection.z.Abs());
            bool reachedTarget = _distanceToTarget < _closingDistance * GetActiveStopMultiplier();
            bool noInput = moveAmount == Fixed64.Zero;
            if (!HasTrailGuide && (reachedTarget || (!IsStuck && noInput)))
            {
                Arrive();
                return Vector3d.Zero;
            }

            if (!CheckStuckStatus(navigator.Position, navigator.Speed, navigator.StuckThresholdSpeed))
            {
#if DEBUG
                Debug.WriteLine("Stuck agent arriving!");
#endif
                Arrive();
                return Vector3d.Zero;
            }

            if (TargetDirection != Vector3d.Zero)
            {
                if (_trailGuide is IWaypointGuide waypointGuide && ShouldAdvanceToNextWaypoint())
                    waypointGuide.AdvanceWaypoint();

                if (HasTrailGuide)
                    SetDeceleration(navigator.Acceleration, navigator.Speed);
            }
        }
        else
        {
            TargetDirection = Vector3d.Zero;

            if (navigator.Speed <= Fixed64.Epsilon)
                StoppedFrameCount++;
        }

        _autoStopFrameCount--;
        _pathCheckCooldown--;

        Events.OnStartTraversal?.Invoke(TargetDirection);
        return TargetDirection;
    }

    /// <summary>
    /// Periodically called to initiate a pathfinding query based on the current position and destination.
    /// Note: This will run once on the next `Simulate` call after calling `ApplyPathRequest`
    /// </summary>
    protected virtual bool ValidateMovementPath(Vector3d origin)
    {
        if (!_shouldRequestPathThisFrame)
            return true;
        _shouldRequestPathThisFrame = false;

        // detect size-change
        if (_currentRequest.UnitSize != _lastUnitSize)
        {
            _lastUnitSize = _currentRequest.UnitSize;
            _shouldRequestPathThisFrame = true;
        }

        // update origin
        bool ok = _currentRequest.TrySetOrigin(origin);
        if (!ok || !_currentRequest.HasValidEndpoints)
        {
#if DEBUG
            Debug.WriteLine("Path request is using invalid endpoints!");
#endif
            return false;
        }

        // shortcut if no path needed
        if (_currentRequest.HasZeroDisplacement)
            return _repathTries == 0;

        HasLineOfSightPath = IsDestinationInSight(
            origin,
            Destination,
            _currentRequest.UnitSize,
            _currentRequest.AllowUnwalkable);
        if (HasLineOfSightPath)
            return true;  // no path required

        // request guide
        _pathCheckCooldown = PathRecheckCooldownFrames;
        if (!_currentRequest.IsValid || !PathGuideFactory.RequestGuide(_currentRequest, out _trailGuide))
        {
#if DEBUG
            Debug.WriteLine($"Unable to retrieve a guide from {origin} to {Destination}");
#endif
            return false;
        }

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
                targetDirection = waypointGuide.GetMovementDirection(position);
            else
                _trailGuide.TryGetMovementDirection(position, out targetDirection);
        }

        if (targetDirection == Vector3d.Zero)
        {
#if DEBUG
            Debug.WriteLine("No vialable movement direction found.");
#endif
            return Vector3d.Zero;
        }

        // This is now the direction we want to be travelling in 
        return targetDirection.Normalize(out _distanceToTarget);
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
            || _distanceToTarget < _closingDistance * GlobalGridManager.VoxelSize;
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

        // If unit has not moved stuckThreshold in a frame, it's stuck
        if (stuckThreshold > Fixed64.Zero && speed < stuckThreshold)
        {
            _stuckFrameCount++;

            if (_stuckFrameCount > StuckFrameThreshold)
            {
                if (_repathTries < StuckRepathTries)
                {
                    HasLineOfSightPath = false;

                    if (IsInGroup)
                        LeaveMovementGroup();  // Attempt to repath agent by themselves

                    if (HasTrailGuide && _trailGuide.TryGetFallbackDirection(position, out Vector3d fallback))
                    {
                        TargetDirection = fallback;
                        _repathTries++;
                        _stuckFrameCount = 0;
                        return true;
                    }

                    // Attempt to find another path on the next frame
                    TargetDirection = Vector3d.Zero;
                    _shouldRequestPathThisFrame = true;

                    // Reset the guide and have them try a new path (don't pool a bad path)
                    if (_trailGuide != null)
                    {
                        PathGuideFactory.ReturnGuide(_trailGuide, true);
                        _trailGuide = null;
                    }

                    _repathTries++;
                    return true;
                }

                // we've tried to many times, we stuck stuck
                IsStuck = true;
                // Reset the guide and have them try a new path (don't pool a bad path)
                if (_trailGuide != null)
                {
                    PathGuideFactory.ReturnGuide(_trailGuide, true);
                    _trailGuide = null;
                }

                Events.OnIsStuck?.Invoke();

                return false;
            }

            // Keep trying
            return true;
        }

        // agent isn't stuck
        IsStuck = false;
        _stuckFrameCount = 0;
        _repathTries = 0;

        return true;
    }

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
            TargetDirection *= closingSpeed; // reduce magnitude = slow down
        }
    }

    /// <summary>
    /// Triggers the arrival event and resets internal movement tracking.
    /// </summary>
    public void Arrive()
    {
        StopMove();

        if (_trailGuide != null)
            PathGuideFactory.ReturnGuide(_trailGuide);

        _trailGuide = null;
        _currentRequest = null;
        _requestedDestination = Vector3d.Zero;
        _distanceToTarget = Fixed64.Zero;
        IsAtDestination = true;
        Destination = Vector3d.Zero;
        TargetDirection = Vector3d.Zero;

        Events.OnArrive?.Invoke();
    }

    /// <summary>
    /// Resets the movement and pathfinding logic, halting the agent.
    /// </summary>
    public virtual void StopMove()
    {
        if (!ShouldMove)
            return;

        _autoStopFrameCount = 0;
        _stuckFrameCount = 0;
        StoppedFrameCount = 0;

        ShouldMove = false;
        _shouldRequestPathThisFrame = false;
        HasLineOfSightPath = false;
        LeaveMovementGroup();

        Events.OnStopMove?.Invoke();
    }

    #endregion

    #region Line-of-Sight & Reachability

    /// <summary>
    /// Whether the destination is currently visible and reachable from the agent's position.
    /// </summary>
    public static bool IsDestinationInSight(Vector3d position, Vector3d destination, Fixed64 unitSize, bool allowUnwalkable)
    {
        bool result = false;
        if (!PathManager.NeedsPath(position, destination, unitSize, allowUnwalkable))
            result = true;

        return result;
    }

    #endregion

    #region Steering Behaviors (Group & Avoidance)

    /// <summary>
    /// Computes a combined steering vector—
    /// Separation, Alignment, Cohesion, plus single-nearest obstacle avoidance.
    /// </summary>
    public Vector3d ComputeCombinedSteering(
        Vector3d position,
        Vector3d velocity,
        Fixed64 speed,
        Fixed64 radius,
        Guid id)
    {
        if (speed <= Fixed64.Zero)
            return Vector3d.Zero;

        int currentFrame = TrailblazerManager.FrameCount;

        // we need to see everybody who might influence us—either for group or for avoidance
        Fixed64 groupRadius = radius * GroupFactor;
        Fixed64 invGR = Fixed64.One / groupRadius;
        Fixed64 avoidRadius = radius * AvoidFactor;
        Fixed64 scanRadius = FixedMath.Max(groupRadius, avoidRadius);
        Fixed64 groupRadiusSq = groupRadius * groupRadius;

        // Accumulators
        Vector3d separation = Vector3d.Zero;
        Vector3d alignment = Vector3d.Zero;
        Vector3d cohesionCM = Vector3d.Zero;
        int groupCount = 0;

        ISteer closest = null;
        Fixed64 closestDistSq = avoidRadius * avoidRadius;

        bool condition(IVoxelOccupant other) =>
            other.GlobalId != id;
        foreach (IVoxelOccupant entity in GridScanManager.ScanRadius(position, scanRadius, condition))
        {
            if (entity is not ISteer other || other.Radius <= Fixed64.Zero)
                continue;

            if (other.GlobalId == id)
                continue;

            Vector3d offset = other.Position - position;
            Fixed64 distSq = offset.SqrMagnitude;
            if (distSq <= Fixed64.Epsilon)
                continue;

            // Group behaviors
            if (IsGroupNeighbor(other.GlobalId, currentFrame) && distSq < groupRadiusSq)
            {
                groupCount++;
                Fixed64 d = FixedMath.Sqrt(distSq);
                Fixed64 invD = Fixed64.One / d;
                Vector3d norm = offset * invD;  // offset.Normal
                // stronger separation the closer they are
                Fixed64 push = (groupRadius - d) * invGR;
                separation -= norm * push;
                alignment += other.Velocity.Normal;
                cohesionCM += other.Position;
            }

            // Track nearest for avoidance
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closest = other;
            }
        }

        Vector3d groupForce = Vector3d.Zero;
        // Finalize group forces
        if (groupCount > 0)
        {
            Vector3d sep = separation * BehaviorWeights.Separation;
            Vector3d align = (alignment / groupCount).Normal * BehaviorWeights.Alignment;
            Vector3d coh = ((cohesionCM / groupCount - position).Normal) * BehaviorWeights.Cohesion;
            groupForce = sep + align + coh;
        }

        // Compute avoidance
        Vector3d avoidance = Vector3d.Zero;
        if (closest != null)
        {
            Vector3d dir = closest.Position - position;
            // pick left/right dodge
            bool dodgeLeft = Vector3d.Dot(velocity, dir) >= Fixed64.Zero;
            Vector3d perp = dodgeLeft
                ? new(-dir.z, Fixed64.Zero, dir.x)
                : new(dir.z, Fixed64.Zero, -dir.x);

            // prioritize evasive action when facing direct collision(dot ~ ±1),
            // and de-emphasize near misses(dot ~0)
            Fixed64 dynamicAvoidWeight = Vector3d.Dot(velocity.Normal, dir.Normal);
            Fixed64 totalAvoidWeight = BehaviorWeights.Avoidance * dynamicAvoidWeight;
            avoidance = perp.Normal
                * ((radius + closest.Radius) / FixedMath.Sqrt(closestDistSq))
                * totalAvoidWeight;
        }

        return groupForce + avoidance;
    }

    #endregion

    #region Movement Groups

    private void CacheOwner(ISteer navigator)
    {
        MovementGroupCoordinator.CacheOwner(_movementGroupSession, navigator.GlobalId);
    }

    private void UpdateMovementGroupState(Vector3d position, bool resetFormationOffset = false)
    {
        var target = new MovementGroupTarget(
            travelMode: IsInGroup ? MovementGroupTravelMode.Individual : MovementGroupTravelMode.None,
            destination: _requestedDestination);

        if (IsInGroup && _currentRequest != null)
        {
            target = MovementGroupCoordinator.UpdateTarget(
                _movementGroupSession,
                _requestedDestination,
                position,
                _agentRadius,
                resetFormationOffset);
        }

        Destination = target.Destination;
        _movementGroupMode = target.TravelMode;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetActiveStopMultiplier() =>
        _movementGroupMode == MovementGroupTravelMode.GroupIndividual
            ? DefaultGroupIndividualStop
            : StopMultiplier;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGroupNeighbor(Guid otherId, int currentFrame)
        => MovementGroupCoordinator.IsNeighbor(_movementGroupSession, otherId, _requestedDestination, currentFrame);

    #endregion
}
