using Chronicler;
using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

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
    /// Maximum number of repath attempts before declaring the agent fully stuck.
    /// </summary>
    protected const int StuckRepathTries = 4;

    /// <summary>
    /// Default braking factor applied when decelerating or stopping motion.
    /// </summary>
    public static readonly Fixed64 DefaultBrakingPower = (Fixed64)0.15d;

    /// <summary>
    /// Group fallback stop tolerance used when a formation breaks apart near the goal.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupIndividualStop = Fixed64.One;

    #endregion

    #region Fields

    /// <summary>
    /// The final destination this agent is attempting to reach.
    /// </summary>
    protected Vector3d _destination;

    /// <summary>
    /// Gets the current target direction as a three-dimensional vector.
    /// </summary>
    protected Vector3d _targetDirection;

    /// <summary>
    /// Gets the direction vector of the most recent target interaction.
    /// </summary>
    protected Vector3d _lastTargetDirection;

    /// <summary>
    /// Whether the object is following a path or guide to the destination.
    /// </summary>
    protected bool _shouldMove;

    /// <summary>
    /// Whether the agent has become stuck and exhausted repathing attempts.
    /// </summary>
    protected bool _isStuck;

    /// <summary>
    /// True if the agent can reach the destination without requiring a path.
    /// </summary>
    protected bool _hasLineOfSightPath;

    /// <summary>
    /// Whether the currently resolved guide-backed route requires climb intent to remain engaged.
    /// </summary>
    protected bool _currentRouteRequestsClimbIntent;

    /// <summary>
    /// Version token that changes when the resolved route state relevant to guided climb intent changes.
    /// </summary>
    protected int _currentRouteTopologyVersion;

    /// <summary>
    /// Has this unit arrived at destination?
    /// </summary>
    protected bool _isAtDestination;

    /// <summary>
    /// Number of consecutive frames where movement failed and deceleration is occurring.
    /// </summary>
    protected int _stoppedFrameCount;

    #endregion

    #region Runtime State - Pathfinding

    /// <summary>
    /// Disable if a unit never needs voxel-guide validation or repathing.
    /// </summary>
    public bool CanPathfind = true;

    /// <inheritdoc cref="_destination"/>
    public Vector3d Destination => _destination;

    private Vector3d _requestedDestination;

    private Fixed64 _lastUnitSize;

    /// <inheritdoc cref="DefaultPathRecheckCooldown"/>
    public int PathRecheckCooldownFrames = DefaultPathRecheckCooldown;

    /// <inheritdoc cref="_targetDirection"/>
    public Vector3d TargetDirection => _targetDirection;

    /// <inheritdoc cref="_lastTargetDirection"/>
    public Vector3d LastTargetDirection => _lastTargetDirection;

    /// <summary>
    /// The pathfinding configuration used for the current movement request, including size, and type.
    /// </summary>
    private IPathRequest? _currentRequest;

    /// <inheritdoc cref="_currentRequest"/>
    public IPathRequest? CurrentRequest => _currentRequest;

    /// <summary>
    /// Current guide used to compute the desired path or flow.
    /// </summary>
    private IGuide? _trailGuide;

    /// <inheritdoc cref="_trailGuide"/>
    public IGuide? TrailGuide => _trailGuide;

    /// <inheritdoc cref="_shouldMove"/>
    public bool ShouldMove => _shouldMove;

    /// <inheritdoc cref="_isStuck"/>
    public bool IsStuck => _isStuck;

    /// <inheritdoc cref="_hasLineOfSightPath"/>
    public bool HasLineOfSightPath => _hasLineOfSightPath;

    /// <inheritdoc cref="_currentRouteRequestsClimbIntent"/>
    public bool CurrentRouteRequestsClimbIntent => _currentRouteRequestsClimbIntent;

    /// <inheritdoc cref="_currentRouteTopologyVersion"/>
    public int CurrentRouteTopologyVersion => _currentRouteTopologyVersion;

    /// <summary>
    /// Current pathfinding search status.
    /// </summary>
    protected bool _shouldRequestPathThisFrame;

    /// <summary>
    /// Represents the cooldown period, in milliseconds, before the next path check can be performed.
    /// </summary>
    protected int _pathCheckCooldown;

    private bool _currentRouteHasResolvedTopology;

    private bool _currentRouteUsesGuideTopology;

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

    /// <inheritdoc cref="_isAtDestination"/>
    public bool IsAtDestination => _isAtDestination;

    #endregion

    #region Runtime State - Steering & Motion

    /// <summary>
    /// Whether this agent can currently move.
    /// </summary>
    public bool CanMove = true;

    /// <inheritdoc cref="_stoppedFrameCount"/>
    public int StoppedFrameCount => _stoppedFrameCount;

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
    public Fixed64 StopMultiplier = DefaultDirectStop;

    /// <summary>
    /// How far to look for group neighbors (separation/alignment/cohesion).
    /// </summary>
    public Fixed64 GroupFactor = DefaultGroupFactor;

    /// <summary>
    /// How far to look for obstacles to avoid.
    /// </summary>
    public Fixed64 AvoidFactor = DefaultAvoidFactor;

    /// <summary>
    /// Weights for separating, aligning, and cohesion in group behavior.
    /// Avoidance weight is baked in here as well.
    /// </summary>
    public GroupBehaviorWeights BehaviorWeights = DefaultBehaviorWeights;

    /// <summary>
    /// Friction-based deceleration rate used when slowing down on ground surfaces.
    /// </summary>
    public Fixed64 BrakingPower = DefaultBrakingPower;

    private Fixed64 _agentRadius;

    private readonly MovementGroupSession _movementGroupSession = new();

    private readonly SwiftList<ISteer> _nearbySteerAgents = new();

    private readonly GridScanScratch _scanScratch = new();

    private MovementGroupTravelMode _movementGroupMode;

    private TrailblazerWorldContext? _context;

    #endregion

    #region Events

    /// <summary>
    /// Container for delegate events that fire on pathfinding state changes (start, stop, arrive).
    /// </summary>
    public NavSteeringEvents Events { get; protected set; } = new();

    /// <summary>
    /// Gets the world context this steering controller is bound to, when explicitly bound.
    /// </summary>
    public TrailblazerWorldContext? Context => _context;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new <see cref="NavSteering"/> instance bound to a world context.
    /// </summary>
    public static NavSteering CreateNew(TrailblazerWorldContext context, Fixed64 radius) => new(context, radius);

    private NavSteering() { }

    /// <summary>
    /// Initializes a new context-bound <see cref="NavSteering"/> instance.
    /// </summary>
    public NavSteering(TrailblazerWorldContext context, Fixed64 radius)
    {
        BindContext(context);
        OnInitialize(radius);
    }

    /// <summary>
    /// Binds this steering controller to a world context.
    /// </summary>
    public void BindContext(TrailblazerWorldContext context)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);

        if (ReferenceEquals(_context, context))
            return;

        if (_context != null)
            _context.Navigation.MovementGroups.Remove(_movementGroupSession);

        _context = context;
    }

    #endregion

    #region Group Properties

    /// <summary>
    /// Gets or sets the unique identifier for the movement group associated with the current session.
    /// </summary>
    public int MovementGroupID
    {
        get => _movementGroupSession.GroupId;
        set => _movementGroupSession.GroupId = value;
    }

    /// <summary>
    /// Gets the index of the group associated with the current movement session.
    /// </summary>
    public int GroupIndex
    {
        get => _movementGroupSession.GroupIndex;
        protected set => _movementGroupSession.GroupIndex = value;
    }

    /// <summary>
    /// Gets a value indicating whether the item is assigned to a movement group.
    /// </summary>
    public bool IsInGroup => MovementGroupID != -1;

    #endregion

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

        return GridWorld.DefaultVoxelSize;
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

    #region Line-of-Sight & Reachability

    /// <summary>
    /// Whether the destination is visible and reachable inside the supplied world context.
    /// </summary>
    public static bool IsDestinationInSight(
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints)
    {
        return !context.Pathing.NeedsPath(position, destination, unitSize, allowUnwalkableEndpoints);
    }

    /// <summary>
    /// Whether the destination is currently visible and reachable for raw-volume travel in the supplied context.
    /// </summary>
    public static bool IsVolumeDestinationInSight(
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel? startNode = null,
        Voxel? endNode = null)
    {
        return VolumeVoxelFinder.IsDirectPathClear(
            context,
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

        TrailblazerWorldContext context = ResolveContext();
        int currentFrame = context.FrameCount;

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

        ISteer? closest = null;
        Fixed64 closestDistSq = avoidRadius * avoidRadius;

        GridScanManager.ScanRadiusInto<ISteer>(
            context.World,
            position,
            scanRadius,
            _nearbySteerAgents,
            _scanScratch);

        for (int i = 0; i < _nearbySteerAgents.Count; i++)
        {
            ISteer other = _nearbySteerAgents[i];
            if (other.Radius <= Fixed64.Zero)
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

    private void CacheOwner(ISteer vessel)
    {
        if (IsInGroup)
            MovementGroups.CacheOwner(_movementGroupSession, vessel.GlobalId);
    }

    private void UpdateMovementGroupState(Vector3d position, bool resetFormationOffset = false)
    {
        var target = new MovementGroupTarget(
            travelMode: IsInGroup ? MovementGroupTravelMode.Individual : MovementGroupTravelMode.None,
            destination: _requestedDestination);

        if (IsInGroup && _currentRequest != null)
        {
            target = MovementGroups.UpdateTarget(
                _movementGroupSession,
                _requestedDestination,
                position,
                _agentRadius,
                resetFormationOffset);
        }

        _destination = target.Destination;
        _movementGroupMode = target.TravelMode;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetActiveStopMultiplier() =>
        _movementGroupMode == MovementGroupTravelMode.GroupIndividual
            ? DefaultGroupIndividualStop
            : StopMultiplier;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGroupNeighbor(Guid otherId, int currentFrame)
        => MovementGroups.IsNeighbor(_movementGroupSession, otherId, _requestedDestination, currentFrame);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool UsesVolumeGuidance() => _currentRequest is VolumePathRequest;

    private void PublishRouteTopology(
        bool hasResolvedTopology,
        bool usesGuideTopology,
        bool requestsClimbIntent,
        bool force = false)
    {
        if (!force
            && _currentRouteHasResolvedTopology == hasResolvedTopology
            && _currentRouteUsesGuideTopology == usesGuideTopology
            && _currentRouteRequestsClimbIntent == requestsClimbIntent)
        {
            return;
        }

        _currentRouteHasResolvedTopology = hasResolvedTopology;
        _currentRouteUsesGuideTopology = usesGuideTopology;
        _currentRouteRequestsClimbIntent = requestsClimbIntent;
        unchecked
        {
            _currentRouteTopologyVersion++;
        }
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        var requestRecord = new PathRequestRecord();
        if (chronicler.Mode == SerializationMode.Saving)
            requestRecord.Capture(_currentRequest, _trailGuide);

        int movementGroupId = _movementGroupSession.GroupId;

        RecordValues.Look(chronicler, ref CanPathfind, "CanPathfind", true);
        RecordValues.Look(chronicler, ref _destination, "Destination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _requestedDestination, "RequestedDestination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastUnitSize, "LastUnitSize", Fixed64.Zero);
        RecordValues.Look(chronicler, ref PathRecheckCooldownFrames, "PathRecheckCooldownFrames", DefaultPathRecheckCooldown);
        RecordValues.Look(chronicler, ref _targetDirection, "TargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastTargetDirection, "LastTargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _shouldMove, "ShouldMove", false);
        RecordValues.Look(chronicler, ref _isStuck, "IsStuck", false);
        RecordValues.Look(chronicler, ref _hasLineOfSightPath, "HasLineOfSightPath", false);
        RecordValues.Look(chronicler, ref _currentRouteHasResolvedTopology, "CurrentRouteHasResolvedTopology", false);
        RecordValues.Look(chronicler, ref _currentRouteUsesGuideTopology, "CurrentRouteUsesGuideTopology", false);
        RecordValues.Look(chronicler, ref _currentRouteRequestsClimbIntent, "CurrentRouteRequestsClimbIntent", false);
        RecordValues.Look(chronicler, ref _currentRouteTopologyVersion, "CurrentRouteTopologyVersion", 0);
        RecordValues.Look(chronicler, ref _shouldRequestPathThisFrame, "ShouldRequestPathThisFrame", false);
        RecordValues.Look(chronicler, ref _pathCheckCooldown, "PathCheckCooldown", 0);
        RecordValues.Look(chronicler, ref _distanceToTarget, "DistanceToTarget", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _isAtDestination, "IsAtDestination", false);
        RecordValues.Look(chronicler, ref CanMove, "CanMove", false);
        RecordValues.Look(chronicler, ref _stoppedFrameCount, "StoppedFrameCount", 0);
        RecordValues.Look(chronicler, ref _autoStopFrameCount, "AutoStopFrameCount", 0);
        RecordValues.Look(chronicler, ref _repathTries, "RepathTries", 0);
        RecordValues.Look(chronicler, ref _stuckFrameCount, "StuckFrameCount", 0);
        RecordValues.Look(chronicler, ref StopMultiplier, "StopMultiplier", DefaultDirectStop);
        RecordValues.Look(chronicler, ref GroupFactor, "GroupFactor", DefaultGroupFactor);
        RecordValues.Look(chronicler, ref AvoidFactor, "AvoidFactor", DefaultAvoidFactor);
        RecordValues.Look(chronicler, ref BehaviorWeights, "BehaviorWeights", DefaultBehaviorWeights);
        RecordValues.Look(chronicler, ref BrakingPower, "BrakingPower", DefaultBrakingPower);
        RecordValues.Look(chronicler, ref movementGroupId, "MovementGroupId", 0);
        RecordValues.Look(chronicler, ref _movementGroupMode, "MovementGroupMode", MovementGroupTravelMode.None);
        RecordDeep.Look(chronicler, ref requestRecord, "PathRequest");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ReleaseTrailGuide();
            ResetMovementGroupSession();

            _currentRequest = null;
            if (!requestRecord.TryCreateRequest(ResolveContext(), out IPathRequest? request))
            {
                _shouldMove = false;
                _isStuck = false;
                _hasLineOfSightPath = false;
                _shouldRequestPathThisFrame = false;
                _destination = Vector3d.Zero;
                _requestedDestination = Vector3d.Zero;
                _targetDirection = Vector3d.Zero;
                _lastTargetDirection = Vector3d.Zero;
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

        (_currentRequest?.Context ?? ResolveContext()).Guides.ReturnGuide(_trailGuide, dispose);
        _trailGuide = null;
    }

    private void ResetMovementGroupSession()
    {
        RemoveMovementGroupSession();
        _movementGroupSession.Reset();
    }

    #endregion
}
