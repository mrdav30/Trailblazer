using Chronicler;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;
using Trailblazer.Serialization;

#if DEBUG
using System.Diagnostics;
#endif

namespace Trailblazer.Navigation.Steering;

/// <summary>
/// Handles agent steering and path navigation logic by coordinating pathfinding, movement, 
/// and group behaviors within a lockstep simulation. Supports both direct line-of-sight travel 
/// and guided path traversal using IGuide implementations like AStar or FlowField.
/// </summary>
public class NavSteering : IRecordable
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
    /// Disable if a unit never needs voxel-guide validation or repathing.
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
    /// <param name="pathRequest">The movement request that defines the desired origin and destination.</param>
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

        ReleaseTrailGuide();
        _currentRequest = pathRequest;
        _lastUnitSize = pathRequest.UnitSize;

        _repathTries = 0;
        _shouldRequestPathThisFrame = true;

        AddToMovementGroup(groupId);
        UpdateMovementGroupState(pathRequest.Origin, true);

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

    /// <summary>
    /// Rebuilds this steering session's shared movement-group membership from the current runtime owner state.
    /// </summary>
    /// <remarks>
    /// Call this after loading multiple grouped steering sessions when you want the coordinator warmed
    /// before the next simulation frame. If it is skipped, grouped steering will still recover lazily
    /// during <see cref="GetHeading(ISteer)"/>.
    /// </remarks>
    /// <param name="navigator">The current steering owner whose position, radius, and stable id should seed the coordinator.</param>
    public void PrewarmMovementGroup(ISteer navigator)
    {
        if (navigator == null)
            throw new ArgumentNullException(nameof(navigator));

        if (!ShouldMove || !IsInGroup || _currentRequest == null)
            return;

        MovementGroupCoordinator.Prewarm(
            _movementGroupSession,
            navigator.GlobalId,
            _requestedDestination,
            navigator.Position,
            _agentRadius);
    }

    #endregion

    #region Simulation Lifecycle

    /// <summary>
    /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    public virtual void OnInitialize(Fixed64 radius)
    {
        UpdateOwnerRadius(radius);

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

    internal virtual void UpdateOwnerRadius(Fixed64 radius)
    {
        // Fatter objects can afford to land imprecisely
        _agentRadius = radius;
        _closingDistance = FixedMath.Round(radius + GlobalGridManager.VoxelSize);
    }

    /// <summary>
    /// Called every simulation step to handle agent steering and movement logic.
    /// </summary>
    public virtual Vector3d GetHeading(ISteer navigator)
    {
        CacheOwner(navigator);

        if (!CanMove)
            return Vector3d.Zero;

        if (!ShouldMove || IsAtDestination)
            return FinalizeIdleHeading(navigator.Speed);

        if (!TryEnsureCurrentRequest(out Vector3d heading))
            return heading;

        bool usesVolumeGuidance = UsesVolumeGuidance();
        UpdateMovementGroupState(navigator.Position);

        if (!TryPrepareMovementPathForHeading(navigator.Position, usesVolumeGuidance))
            return Vector3d.Zero;

        UpdateTargetDirection(navigator);
        if (ShouldArriveWithoutTrailGuide())
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

        UpdateTrailGuideProgress(navigator.Acceleration, navigator.Speed);
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
        if (_currentRequest.UnitSize != _lastUnitSize)
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
#if DEBUG
            Debug.WriteLine("Path request is using invalid endpoints!");
#endif
            return false;
        }

        // shortcut if no path needed
        if (_currentRequest.HasZeroDisplacement)
            return _repathTries == 0;

        if (_currentRequest is VolumePathRequest volumeRequest)
        {
            HasLineOfSightPath = IsVolumeDestinationInSight(
                origin,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints,
                volumeRequest.Medium,
                _currentRequest.StartNode,
                _currentRequest.EndNode);

            _pathCheckCooldown = PathRecheckCooldownFrames;
            if (HasLineOfSightPath)
            {
                ReleaseTrailGuide();
                return true;
            }
        }
        else
        {
            HasLineOfSightPath = IsDestinationInSight(
                origin,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints);
            if (HasLineOfSightPath)
                return true;  // no path required
        }

        // request guide
        ReleaseTrailGuide();
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
                targetDirection = waypointGuide.GetCurrentWaypointDirection(position);
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

        if (stuckThreshold <= Fixed64.Zero || speed >= stuckThreshold)
            return ResetStuckStatus();

        _stuckFrameCount++;
        if (_stuckFrameCount <= StuckFrameThreshold)
            return true;

        return _repathTries < StuckRepathTries
            ? TryRecoverFromStuck(position)
            : DeclareHardStuck();
    }

    private Vector3d FinalizeIdleHeading(Fixed64 speed)
    {
        TargetDirection = Vector3d.Zero;
        if (speed <= Fixed64.Epsilon)
            StoppedFrameCount++;

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
            HasLineOfSightPath = IsVolumeDestinationInSight(
                position,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints,
                volumeRequest.Medium,
                _currentRequest.StartNode,
                _currentRequest.EndNode);

            if (HasLineOfSightPath)
                ReleaseTrailGuide();
        }
        else
        {
            HasLineOfSightPath = IsDestinationInSight(
                position,
                Destination,
                _currentRequest.UnitSize,
                _currentRequest.AllowUnwalkableEndpoints);
        }

        _pathCheckCooldown = PathRecheckCooldownFrames;
    }

    private void HandleInvalidPath(string debugMessage)
    {
#if DEBUG
        Debug.WriteLine(debugMessage);
#endif
        Events.OnInvalidPath?.Invoke();
        Arrive();
    }

    private void UpdateTargetDirection(ISteer navigator)
    {
        LastTargetDirection = TargetDirection;
        TargetDirection = FindTargetDirection(navigator.Position);
        TargetDirection += ComputeCombinedSteering(
            navigator.Position,
            navigator.Velocity,
            navigator.Speed,
            navigator.Radius,
            navigator.GlobalId);
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
        IsStuck = false;
        _stuckFrameCount = 0;
        _repathTries = 0;
        return true;
    }

    private bool TryRecoverFromStuck(Vector3d position)
    {
        HasLineOfSightPath = false;

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
        if (!HasTrailGuide || !_trailGuide.TryGetFallbackDirection(position, out Vector3d fallback))
            return false;

        TargetDirection = fallback;
        _repathTries++;
        _stuckFrameCount = 0;
        return true;
    }

    private void PreparePathRetry()
    {
        TargetDirection = Vector3d.Zero;
        _shouldRequestPathThisFrame = true;
        DisposeCurrentTrailGuide();
    }

    private bool DeclareHardStuck()
    {
        IsStuck = true;
        DisposeCurrentTrailGuide();
        Events.OnIsStuck?.Invoke();
        return false;
    }

    private void DisposeCurrentTrailGuide()
    {
        if (_trailGuide == null)
            return;

        PathGuideFactory.ReturnGuide(_trailGuide, true);
        _trailGuide = null;
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

        ReleaseTrailGuide();
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
    public static bool IsDestinationInSight(Vector3d position, Vector3d destination, Fixed64 unitSize, bool allowUnwalkableEndpoints)
    {
        bool result = false;
        if (!PathManager.NeedsPath(position, destination, unitSize, allowUnwalkableEndpoints))
            result = true;

        return result;
    }

    /// <summary>
    /// Whether the destination is currently visible and reachable for raw-volume travel.
    /// </summary>
    public static bool IsVolumeDestinationInSight(
        Vector3d position,
        Vector3d destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel startNode = null,
        Voxel endNode = null)
    {
        return VolumeVoxelFinder.IsDirectPathClear(
            position,
            destination,
            unitSize,
            allowUnwalkableEndpoints,
            medium,
            startNode,
            endNode);
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

    private void CacheOwner(ISteer navigator) =>
        MovementGroupCoordinator.CacheOwner(_movementGroupSession, navigator.GlobalId);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool UsesVolumeGuidance() => _currentRequest is VolumePathRequest;

    #endregion

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        bool canPathfind = CanPathfind;
        Vector3d destination = Destination;
        Vector3d requestedDestination = _requestedDestination;
        Fixed64 lastUnitSize = _lastUnitSize;
        int pathRecheckCooldownFrames = PathRecheckCooldownFrames;
        Vector3d targetDirection = TargetDirection;
        Vector3d lastTargetDirection = LastTargetDirection;
        bool shouldMove = ShouldMove;
        bool isStuck = IsStuck;
        bool hasLineOfSightPath = HasLineOfSightPath;
        bool shouldRequestPathThisFrame = _shouldRequestPathThisFrame;
        int pathCheckCooldown = _pathCheckCooldown;
        Fixed64 distanceToTarget = _distanceToTarget;
        bool isAtDestination = IsAtDestination;
        bool canMove = CanMove;
        int stoppedFrameCount = StoppedFrameCount;
        int autoStopFrameCount = _autoStopFrameCount;
        int repathTries = _repathTries;
        int stuckFrameCount = _stuckFrameCount;
        Fixed64 stopMultiplier = StopMultiplier;
        Fixed64 groupFactor = GroupFactor;
        Fixed64 avoidFactor = AvoidFactor;
        GroupBehaviorWeights behaviorWeights = BehaviorWeights;
        Fixed64 brakingPower = BrakingPower;
        int movementGroupId = MovementGroupID;
        MovementGroupTravelMode movementGroupMode = _movementGroupMode;
        var requestRecord = new PathRequestRecord();

        if (chronicler.Mode == SerializationMode.Saving)
            requestRecord.Capture(_currentRequest, _trailGuide);

        RecordValues.Look(chronicler, ref canPathfind, "canPathfind", true);
        RecordValues.Look(chronicler, ref destination, "destination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref requestedDestination, "requestedDestination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref lastUnitSize, "lastUnitSize", Fixed64.Zero);
        RecordValues.Look(chronicler, ref pathRecheckCooldownFrames, "pathRecheckCooldownFrames", DefaultPathRecheckCooldown);
        RecordValues.Look(chronicler, ref targetDirection, "targetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref lastTargetDirection, "lastTargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref shouldMove, "shouldMove", false);
        RecordValues.Look(chronicler, ref isStuck, "isStuck", false);
        RecordValues.Look(chronicler, ref hasLineOfSightPath, "hasLineOfSightPath", false);
        RecordValues.Look(chronicler, ref shouldRequestPathThisFrame, "shouldRequestPathThisFrame", false);
        RecordValues.Look(chronicler, ref pathCheckCooldown, "pathCheckCooldown", 0);
        RecordValues.Look(chronicler, ref distanceToTarget, "distanceToTarget", Fixed64.Zero);
        RecordValues.Look(chronicler, ref isAtDestination, "isAtDestination", false);
        RecordValues.Look(chronicler, ref canMove, "canMove", false);
        RecordValues.Look(chronicler, ref stoppedFrameCount, "stoppedFrameCount", 0);
        RecordValues.Look(chronicler, ref autoStopFrameCount, "autoStopFrameCount", 0);
        RecordValues.Look(chronicler, ref repathTries, "repathTries", 0);
        RecordValues.Look(chronicler, ref stuckFrameCount, "stuckFrameCount", 0);
        RecordValues.Look(chronicler, ref stopMultiplier, "stopMultiplier", DefaultDirectStop);
        RecordValues.Look(chronicler, ref groupFactor, "groupFactor", DefaultGroupFactor);
        RecordValues.Look(chronicler, ref avoidFactor, "avoidFactor", DefaultAvoidFactor);
        RecordValues.Look(chronicler, ref behaviorWeights, "behaviorWeights", DefaultBehaviorWeights);
        RecordValues.Look(chronicler, ref brakingPower, "brakingPower", DefaultBrakingPower);
        RecordValues.Look(chronicler, ref movementGroupId, "movementGroupId", 0);
        RecordValues.Look(chronicler, ref movementGroupMode, "movementGroupMode", MovementGroupTravelMode.None);
        RecordDeep.Look(chronicler, ref requestRecord, "pathRequest");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ReleaseTrailGuide();
            ResetMovementGroupSession();

            CanPathfind = canPathfind;
            Destination = destination;
            _requestedDestination = requestedDestination;
            _lastUnitSize = lastUnitSize;
            PathRecheckCooldownFrames = pathRecheckCooldownFrames;
            TargetDirection = targetDirection;
            LastTargetDirection = lastTargetDirection;
            ShouldMove = shouldMove;
            IsStuck = isStuck;
            HasLineOfSightPath = hasLineOfSightPath;
            _shouldRequestPathThisFrame = shouldRequestPathThisFrame;
            _pathCheckCooldown = pathCheckCooldown;
            _distanceToTarget = distanceToTarget;
            IsAtDestination = isAtDestination;
            CanMove = canMove;
            StoppedFrameCount = stoppedFrameCount;
            _autoStopFrameCount = autoStopFrameCount;
            _repathTries = repathTries;
            _stuckFrameCount = stuckFrameCount;
            StopMultiplier = stopMultiplier;
            GroupFactor = groupFactor;
            AvoidFactor = avoidFactor;
            BehaviorWeights = behaviorWeights;
            BrakingPower = brakingPower;
            _movementGroupMode = movementGroupMode;

            _currentRequest = null;
            if (!requestRecord.TryCreateRequest(out IPathRequest request))
            {
                ShouldMove = false;
                IsStuck = false;
                HasLineOfSightPath = false;
                _shouldRequestPathThisFrame = false;
                Destination = Vector3d.Zero;
                _requestedDestination = Vector3d.Zero;
                TargetDirection = Vector3d.Zero;
                LastTargetDirection = Vector3d.Zero;
                _distanceToTarget = Fixed64.Zero;
                _movementGroupMode = MovementGroupTravelMode.None;
            }
            else
            {
                _currentRequest = request;
            }

            MovementGroupID = movementGroupId;
            GroupIndex = -1;
            if (movementGroupId < 0)
                _movementGroupMode = MovementGroupTravelMode.None;

            if (_currentRequest != null
                && ShouldMove
                && requestRecord.HasGuide
                && !_shouldRequestPathThisFrame
                && !HasLineOfSightPath)
            {
                if (!requestRecord.TryCreateGuide(_currentRequest, out _trailGuide))
                    _shouldRequestPathThisFrame = ShouldMove;
            }
            else if (_currentRequest != null
                && ShouldMove
                && !HasLineOfSightPath)
            {
                _shouldRequestPathThisFrame = true;
            }
        }
    }

    private void ReleaseTrailGuide(bool dispose = false)
    {
        if (_trailGuide == null)
            return;

        PathGuideFactory.ReturnGuide(_trailGuide, dispose);
        _trailGuide = null;
    }

    private void ResetMovementGroupSession()
    {
        MovementGroupCoordinator.Remove(_movementGroupSession);
        _movementGroupSession.Reset();
    }

    #endregion
}
