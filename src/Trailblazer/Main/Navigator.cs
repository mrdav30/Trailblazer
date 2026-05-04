using Chronicler;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Base class representing a object, responsible for handling movement, traversal state, and simulation flow.
/// </summary>
/// <remarks>
/// This class acts as a bridge between the simulation logic and the entity's external representation.  
/// It defines common traversal behaviors and lifecycle methods that can be extended by concrete implementations.
/// </remarks>
[Serializable]
public abstract class Navigator : INavigate, IRecordable
{
    #region Constants

    /// <summary>
    /// Default vertical offset used to determine the object’s contact point with the ground.
    /// </summary>
    public static readonly Fixed64 DefaultFootPositionAdjust = new(0.25f);

    #endregion

    #region Fields

    /// <inheritdoc cref="Position"/>
    protected Vector3d _position;

    /// <inheritdoc cref="LastPosition"/>
    protected Vector3d _lastPosition;

    /// <inheritdoc cref="Rotation"/>
    protected FixedQuaternion _rotation = FixedQuaternion.Identity;

    /// <inheritdoc cref="Velocity"/>
    protected Vector3d _velocity;

    /// <inheritdoc cref="Speed"/>
    protected Fixed64 _speed;

    /// <inheritdoc cref="Acceleration"/>
    protected Vector3d _acceleration;   

    /// <inheritdoc cref="Size"/>
    protected Fixed64 _size = Fixed64.One;

    /// <summary>
    /// Adjustment factor for the foot position, used to determine ground contact points.
    /// </summary>
    protected Fixed64 _footPositionAdjust = DefaultFootPositionAdjust;

    private SolidPathAlgorithm _guidedPathMode = SolidPathAlgorithm.AStar;

    private bool _guidedAllowUnwalkableEndpoints;

    private bool _guidedAllowTraversalTransitions;

    private Fixed64 _guidedMaxClimbHeight = Fixed64.One;

    private HeuristicMethod _guidedAStarHeuristic = HeuristicMethod.Manhattan;

    private int _guidedFlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    /// <summary>
    /// Stable runtime identity used for occupancy and steering coordination.
    /// </summary>
    /// <remarks>
    /// By default this is allocated deterministically from object setup order.
    /// Hosts can override it during <see cref="Setup(Vector3d, FixedQuaternion?, Vector3d?, Fixed64?, Guid?)"/>
    /// when a broader simulation stack already owns stable agent ids.
    /// </remarks>
    protected Guid _globalId;

    private byte _occupantGroupId = 1;

    private bool _isLockedOn;

    /// <summary>
    /// The controller responsible for managing the object's desired movement direction.
    /// </summary>
    protected NavSteering? _steering;

    /// <summary>
    /// The controller responsible for managing the object's rotation towards the movement direction.
    /// </summary>
    protected NavTurning? _turning;

    /// <summary>
    /// The controller responsible for managing the object's movement and physics interactions.
    /// </summary>
    protected NavMotor? _motor;

    private Fixed64 _stuckThresholdSpeed;

    private bool _isGuideded;

    /// <summary>
    /// The change in position to apply during the current simulation frame.
    /// </summary>
    protected Vector3d _positionDelta;

    /// <summary>
    /// The change in rotation to apply during the current simulation frame.
    /// </summary>
    protected FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    /// <summary>
    /// The velocity change computed during the simulation frame.
    /// </summary>
    protected Vector3d _velocityDelta;

    /// <summary>
    /// Indicates whether the Navigator has been set.
    /// </summary>
    protected bool _isSet;

    /// <summary>
    /// Indicates whether the Navigator has been initialized.
    /// </summary>
    protected bool _isInitialized;

    /// <inheritdoc cref="TrekCondition"/>
    protected TrekCondition _frameCondition = new();

    /// <inheritdoc cref="TrekRequest"/>
    protected TrekRequest _frameRequest = new();

    private GuidedVolumeExitHandoff? _pendingGuidedVolumeExitHandoff;

    private bool _guidedClimbIntent;

    private GuidedClimbIntentMode _guidedClimbIntentMode;

    private int _lastSeenGuidedRouteTopologyVersion;

    #endregion

    #region State - Identity / Transform

    /// <inheritdoc/>
    public Vector3d Position => _position;

    /// <inheritdoc/>
    public Vector3d LastPosition => _lastPosition;

    /// <inheritdoc/>
    public FixedQuaternion Rotation => _rotation;

    /// <inheritdoc/>
    public Vector3d Forward { get; protected set; }

    /// <inheritdoc/>
    public Vector3d Velocity => _velocity;

    /// <inheritdoc/>
    public Fixed64 Speed => _speed;

    /// <inheritdoc/>
    public Vector3d Acceleration => _acceleration;

    /// <summary>
    /// Minimum velocity threshold used to determine if the object is considered stuck.
    /// </summary>
    public Fixed64 StuckThresholdSpeed => _stuckThresholdSpeed;

    /// <summary>
    /// Indicates whether the Navigator is currently active and ready for simulation.
    /// </summary>
    public bool IsActive => _isSet && _isInitialized;

    #endregion

    #region State - Controllers

    /// <summary>
    /// The controller responsible for managing the object's desired movement direction.
    /// </summary>
    public NavSteering? Steering => _steering;

    /// <summary>
    /// The controller responsible for managing the object's rotation towards the movement direction.
    /// </summary>
    public NavTurning? Turning => _turning;

    /// <summary>
    /// The controller responsible for managing the object's movement and physics interactions.
    /// </summary>
    public NavMotor? Motor => _motor;

    /// <summary>
    /// Indicates whether the current traversal session is guided via a TrailGuide path (e.g., A* or flow field).
    /// </summary>
    public bool IsGuideded => _isGuideded;

    #endregion

    #region Settings

    /// <inheritdoc/>
    public Fixed64 Size => _size;

    /// <inheritdoc/>
    public Fixed64 Radius => Size * Fixed64.Half;

    /// <inheritdoc cref="_footPositionAdjust"/>
    public Fixed64 FootPositionAdjust { get => _footPositionAdjust; set => _footPositionAdjust = value; }

    /// <summary>
    /// Gets or sets a value indicating whether the object is currently locked on to a target.
    /// </summary>
    public bool IsLockedOn { get => _isLockedOn; set => _isLockedOn = value; }

    /// <summary>
    /// Path request mode used for guided travel.
    /// </summary>
    public SolidPathAlgorithm GuidedPathMode => _guidedPathMode;

    /// <summary>
    /// Whether object-built guided requests may target unwalkable voxels.
    /// </summary>
    public bool GuidedAllowUnwalkableEndpoints => _guidedAllowUnwalkableEndpoints;

    /// <summary>
    /// Whether object-built guided requests may use authored traversal transitions for chart fallback,
    /// bounded swim exits, or bounded aerial landing handoffs.
    /// </summary>
    public bool GuidedAllowTraversalTransitions => _guidedAllowTraversalTransitions;

    /// <summary>
    /// Default max climb height used when the object builds guided requests.
    /// </summary>
    public Fixed64 GuidedMaxClimbHeight => _guidedMaxClimbHeight;

    /// <summary>
    /// Default heuristic used when the object builds A* requests.
    /// </summary>
    public HeuristicMethod GuidedAStarHeuristic => _guidedAStarHeuristic;

    /// <summary>
    /// Default extra flood range used when the object builds flow-field requests.
    /// </summary>
    public int GuidedFlowFieldExtraFloodRange => _guidedFlowFieldExtraFloodRange;

    #endregion

    #region Voxel Occupancy

    /// <inheritdoc cref="_globalId"/>
    public Guid GlobalId => _globalId;

    /// <inheritdoc />
    public byte OccupantGroupId { get => _occupantGroupId; set => _occupantGroupId = value; }

    #endregion

    #region Setup / Initialization

    /// <summary>
    /// Initializes and activates the object with the specified condition, position, and optional parameters.
    /// </summary>
    /// <param name="condition">The condition that determines how the object is initialized and activated.</param>
    /// <param name="position">The position in world coordinates where the object will be placed.</param>
    /// <param name="rotation">The optional rotation to apply to the object. If null, a default rotation is used.</param>
    /// <param name="velocity">The optional initial velocity of the object. If null, the object is initialized with zero velocity.</param>
    /// <param name="size">The optional size of the object. If null, a default size is used.</param>
    /// <param name="globalId">The optional global identifier for the object. If null, a new identifier may be generated.</param>
    public virtual void Activate(
        TrekCondition condition,
        Vector3d position,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Fixed64? size = null,
        Guid? globalId = null)
    {
        Setup(position, rotation, velocity, size, globalId);
        Initialize(condition);
    }

    /// <summary>
    /// Sets the initial configuration of the object, including position, rotation, velocity, size, and optional stable identity.
    /// </summary>
    /// <param name="position">Initial world-space position.</param>
    /// <param name="rotation">Optional starting rotation.</param>
    /// <param name="velocity">Optional initial velocity.</param>
    /// <param name="size">Optional grid size (defaults to 1).</param>
    /// <param name="globalId">Optional host-provided stable identity. When omitted, Trailblazer assigns one deterministically from setup order.</param>
    public virtual void Setup(
        Vector3d position,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Fixed64? size = null,
        Guid? globalId = null)
    {
        if (globalId.HasValue && globalId.Value == Guid.Empty)
            throw new ArgumentException("Navigator globalId cannot be Guid.Empty.", nameof(globalId));

        _globalId = globalId ?? GenerateGUID();

        _lastPosition = _position = position;
        _rotation = rotation ?? FixedQuaternion.Identity;
        if (_rotation != FixedQuaternion.Identity)
            Forward = _rotation.Rotate(Vector3d.Forward);
        else
            Forward = Vector3d.Forward;
        _velocity = velocity ?? Vector3d.Zero;
        _size = size ?? Fixed64.One;

        _isSet = true;
    }

    /// <summary>
    /// Initializes the object by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    public virtual void Initialize(TrekCondition condition)
    {
        _frameCondition = condition.Clone();

        _steering = NavSteering.CreateNew(Radius);

        _motor = NavMotor.CreateNew(_frameCondition, CreateLocomotionProfile());
        _motor.SetVelocity(Velocity);

        _turning = NavTurning.CreateNew(Radius);

        CheckVoxelOccupancy(true);

        _isInitialized = true;
    }

    /// <summary>
    /// Creates the locomotion profile used when this object initializes its motor.
    /// </summary>
    /// <remarks>
    /// Override this to install a smaller or custom locomotion set per object type while preserving
    /// the default profile for callers that do not opt in.
    /// </remarks>
    protected virtual LocomotionProfile CreateLocomotionProfile()
    {
        return LocomotionProfile.CreateDefault();
    }

    /// <summary>
    /// Resets the state of the object to its initial configuration, clearing any active conditions, requests, and internal flags.
    /// </summary>
    /// <remarks>
    /// Call this method to reinitialize the object for reuse or to clear any ongoing operations.
    /// After calling this method, the object will be in the same state as after construction, and any previous state or
    /// intent will be lost. 
    /// This method is intended to be overridden in derived classes to extend the reset behavior as needed.
    /// </remarks>
    public virtual void Reset()
    {
        _frameCondition.Reset();
        _frameRequest.Reset();
        _isGuideded = false;
        _pendingGuidedVolumeExitHandoff = null;
        ResetGuidedClimbIntentState();

        if (TrailblazerWorldManager.IsActive)
            GridOccupantManager.TryDeregister(TrailblazerWorldManager.World, this);

        _isSet = false;
        _isInitialized = false;
    }

    #endregion

    #region Host Bindings

    /// <summary>
    /// Prewarms the steering movement-group coordinator from this object's currently loaded state.
    /// </summary>
    /// <remarks>
    /// This is primarily useful after loading several grouped navigators through Chronicler. Call it once
    /// for each loaded object before the next simulation frame if you want movement-group formation state
    /// available immediately. If it is skipped, grouped steering will still rejoin lazily on the next update.
    /// </remarks>
    public virtual void PrewarmMovementGroup()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before prewarming movement groups.");

        Steering!.PrewarmMovementGroup(this);
    }

    #endregion

    #region Input / Travel Requests

    /// <summary>
    /// Constructs and applies a traversal request using high-level navigation input values.
    /// </summary>
    /// <param name="direction">Desired direction of travel.</param>
    /// <param name="rate">Rate of travel (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the agent is requesting a jump action.</param>
    /// <param name="isRequestingFlight">Whether the agent is requesting to fly or glide.</param>
    /// <param name="isRequestingSwim">Whether the agent is requesting active swim control while in liquid.</param>
    /// <param name="isRequestingClimb">Whether the agent is requesting to climb or maintain climb intent.</param>
    /// <param name="facingDirection">Optional world-space facing direction to use instead of facing along the movement direction.</param>
    /// <param name="canAffordJump">Frame-owned jump affordability answer for this request.</param>
    public virtual void ApplyInputTrekRequest(
        Vector3d? direction = null,
        TrekRate? rate = null,
        Vector3d? facingDirection = null,
        bool? isRequestingFlight = null,
        bool? isRequestingSwim = null,
        bool? isRequestingClimb = null,
        bool? isRequestingJump = null,
        bool canAffordJump = true)
    {
        if (!IsActive) return;

        _isGuideded = false;
        _pendingGuidedVolumeExitHandoff = null;
        ResetGuidedClimbIntentState();
        _frameRequest.SetRequest(
                direction: direction ?? Vector3d.Zero,
                rate: rate ?? TrekRate.Stationary,
                isRequestingJump: isRequestingJump ?? false,
                isRequestingFlight: isRequestingFlight ?? false,
                isRequestingSwim: isRequestingSwim ?? false,
                isRequestingClimb: isRequestingClimb ?? false,
                facingDirection: facingDirection,
                canAffordJump: canAffordJump
        );
    }

    /// <summary>
    /// Constructs and applies a guided traversal request toward a destination using object-owned path request defaults.
    /// </summary>
    /// <param name="targetPosition">The desired world-space target position.</param>
    /// <param name="rate">Desired movement rate (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the object intends to jump during traversal.</param>
    /// <param name="isRequestingFlight">Whether the object intends to fly or glide during traversal.</param>
    /// <param name="isRequestingSwim">Whether the object intends to actively swim while traversing liquid.</param>
    /// <param name="isRequestingClimb">Whether the object intends to climb during traversal.</param>
    /// <param name="groupId">Optional shared group identifier used to preserve formation offsets between navigators.</param>
    /// <param name="canAffordJump">Frame-owned jump affordability answer for this request.</param>
    public virtual void ApplyGuidedTrekRequest(
        Vector3d targetPosition,
        TrekRate? rate = null,
        bool? isRequestingFlight = null,
        bool? isRequestingSwim = null,
        bool? isRequestingClimb = null,
        bool? isRequestingJump = null,
        bool canAffordJump = true,
        int groupId = -1)
    {
        if (!IsActive) return;

        if (!TryCreateGuidedPathRequest(targetPosition, out IPathRequest pathRequest))
        {
            TrailblazerLogger.Channel.Warn(
                $"Unable to create a {GuidedPathMode} path request for object {GlobalId} at {Position} targeting {targetPosition}.");
            return;
        }

        if (_pendingGuidedVolumeExitHandoff != null)
            _pendingGuidedVolumeExitHandoff.MovementGroupId = groupId;

        SetGuidedClimbIntent(
            isRequestingClimb ?? GuidedClimbIntentResolver.Resolve(pathRequest, _pendingGuidedVolumeExitHandoff),
            isRequestingClimb.HasValue
                ? GuidedClimbIntentMode.Explicit
                : GuidedClimbIntentMode.Auto);

        _isGuideded = true;
        _frameRequest.SetRequest(
                direction: Vector3d.Zero,
                rate: rate ?? TrekRate.Stationary,
                isRequestingJump: isRequestingJump ?? false,
                isRequestingFlight: isRequestingFlight ?? false,
                isRequestingSwim: isRequestingSwim ?? false,
                isRequestingClimb: _guidedClimbIntent,
                facingDirection: null,
                canAffordJump: canAffordJump
        );

        Steering!.ApplyPathRequest(pathRequest, groupId);
        CaptureGuidedRouteTopologyVersion();
    }

    /// <summary>
    /// Configures the object-owned defaults used when building guided path requests from a target position.
    /// </summary>
    /// <param name="pathAlgorithm">The pathfinding algorithm to use for guided requests.</param>
    /// <param name="allowUnwalkableEndpoints">Whether to allow object-built guided requests to target unwalkable voxels.</param>
    /// <param name="allowTraversalTransitions">Whether to allow object-built guided requests to use authored traversal transitions for chart fallback, bounded swim exits, or bounded aerial landing handoffs.</param>
    /// <param name="aStarHeuristic">The default heuristic to use when building A* guided requests.</param>
    /// <param name="flowFieldExtraFloodRange">The default extra flood range to use when building flow field guided requests.</param>
    /// <param name="maxClimbHeight">The default max climb height to use when building guided requests.</param>
    public virtual void ConfigureForGuidedTraversal(
        SolidPathAlgorithm? pathAlgorithm = null,
        bool? allowUnwalkableEndpoints = null,
        bool? allowTraversalTransitions = null,
        HeuristicMethod? aStarHeuristic = null,
        int? flowFieldExtraFloodRange = null,
        Fixed64? maxClimbHeight = null)
    {
        _guidedPathMode = pathAlgorithm ?? _guidedPathMode;
        _guidedAllowUnwalkableEndpoints = allowUnwalkableEndpoints ?? _guidedAllowUnwalkableEndpoints;
        _guidedAllowTraversalTransitions = allowTraversalTransitions ?? _guidedAllowTraversalTransitions;
        _guidedAStarHeuristic = aStarHeuristic ?? _guidedAStarHeuristic;
        _guidedFlowFieldExtraFloodRange = flowFieldExtraFloodRange ?? _guidedFlowFieldExtraFloodRange;
        _guidedMaxClimbHeight = maxClimbHeight ?? _guidedMaxClimbHeight;
    }

    /// <summary>
    /// Builds a concrete path request for guided travel from the object's current state and defaults.
    /// Subclasses can override this to support custom request types without changing steering.
    /// </summary>
    protected virtual bool TryCreateGuidedPathRequest(
        Vector3d targetPosition,
        out IPathRequest pathRequest)
    {
        _pendingGuidedVolumeExitHandoff = null;

        bool success = NavigatorPathRequestFactory.TryCreate(
            origin: Position,
            targetPosition: targetPosition,
            unitSize: Size,
            pathMode: GuidedPathMode,
            allowUnwalkableEndpoints: GuidedAllowUnwalkableEndpoints,
            allowTraversalTransitions: GuidedAllowTraversalTransitions,
            maxClimbHeight: GuidedMaxClimbHeight,
            traversalMedium: _frameCondition.Medium,
            aStarHeuristic: GuidedAStarHeuristic,
            flowFieldExtraFloodRange: GuidedFlowFieldExtraFloodRange,
            out IPathRequest? createdRequest,
            out GuidedVolumeExitHandoff? handoff);
        if (!success || createdRequest == null)
        {
            pathRequest = null!;
            return false;
        }

        pathRequest = createdRequest;
        if (handoff != null)
            _pendingGuidedVolumeExitHandoff = handoff;

        return true;
    }

    /// <summary>
    /// Sets the frame-owned jump affordability snapshot used by <see cref="NavMotor"/> on the next traversal step.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void SetFrameJumpAffordability(bool canAffordJump) => _frameRequest.CanAffordJump = canAffordJump;

    /// <summary>
    /// Called to make the agent jump if allowed and in a valid state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ToggleGuidedJump(bool status) => _frameRequest.IsRequestingJump = status;

    /// <summary>
    /// Called to toggle controlled flight if supported by the installed locomotion profile.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ToggleGuidedFlight(bool status) => _frameRequest.IsRequestingFlight = status;

    /// <summary>
    /// Called to toggle active swim control if supported by the installed locomotion profile.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ToggleGuidedSwim(bool status) => _frameRequest.IsRequestingSwim = status;

    /// <summary>
    /// Called to toggle climb intent if supported by the installed locomotion profile.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ToggleGuidedClimb(bool status)
    {
        SetGuidedClimbIntent(status, GuidedClimbIntentMode.Explicit);
    }

    /// <summary>
    /// Changes the speed at which the object is currently traveling without altering direction.
    /// </summary>
    /// <param name="rate">New traversal rate to apply (walk, run, etc.).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void SetGuidedTrekRate(TrekRate rate) => _frameRequest.Rate = rate;

    #endregion

    #region Simulation Lifecycle

    /// <summary>
    /// Attempts to resolve the direction the object should try to face for the current frame.
    /// </summary>
    protected virtual bool TryGetTurnDirection(TrekRequest request, out Vector3d turnDirection)
    {
        if (request.FacingDirection.HasValue && request.FacingDirection.Value != Vector3d.Zero)
        {
            turnDirection = request.FacingDirection.Value;
            return true;
        }

        // Match the legacy controlled-movement behavior: lock-on strafing/backpedaling
        // keeps the current facing unless the host explicitly supplies a facing override
        // or the request is treated as sprinting.
        if (!IsGuideded
            && IsLockedOn
            && request.Rate != TrekRate.Fast)
        {
            turnDirection = Vector3d.Zero;
            return false;
        }

        turnDirection = request.Direction;
        return turnDirection != Vector3d.Zero;
    }

    /// <summary>
    /// Runs simulation logic for this object (input handling, steering, etc.).
    /// </summary>
    public virtual void Simulate()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before Simulate().");

        bool activatedGuidedHandoff = TryActivatePendingGuidedVolumeExitHandoff(out bool handoffRequestedClimb);
        PrepareGuidedIntentState();

        Vector3d heading = Vector3d.Zero;
        if (IsGuideded)
        {
            heading = Steering!.GetHeading(this);
            SyncGuidedIntentStateFromSteering(activatedGuidedHandoff, handoffRequestedClimb);
        }

        _frameRequest.SetTransientState(
             origin: Position,
             footPosition: GetFootPosition(),
             rotation: Rotation,
             direction: IsGuideded ? heading : null
        );

        if (TryGetTurnDirection(_frameRequest, out Vector3d turnDirection))
            Turning!.RequestTurnDirection(Forward, turnDirection);

        if (Motor!.TryTraversal(_frameRequest, out Vector3d vDelta, out Vector3d pDelta, out FixedQuaternion rDelta))
        {
            AddVelocityDelta(vDelta);
            AddPositionDelta(pDelta);
            ApplyRotationDelta(rDelta);
        }

        if (Turning!.TrySimulateTurn(_position, _lastPosition, Forward, _rotation, out FixedQuaternion appliedRotation))
            _rotation = appliedRotation;
    }

    /// <summary>
    /// Finalizes traversal by updating movement calculations and applying corrections.
    /// </summary>
    /// <remarks>
    /// Should be called once every rendering/player interfacing frame, 
    /// after physics bodies apply velocity changes.
    /// </remarks>
    public virtual void CommitFrameMotion()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before CommitFrameMotion().");

        _lastPosition = _position;
        _position += _positionDelta + _velocityDelta;

        CheckVoxelOccupancy();

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            _rotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        if (_rotation != FixedQuaternion.Identity)
            Forward = _rotation.Rotate(Vector3d.Forward);
        else
            Forward = Vector3d.Forward;

        CheckTrekCondition();

        Vector3d previousVelocity = _velocity;
        Fixed64 invDelta = TrailblazerManager.InvDeltaTime;
        _velocity = (Position - LastPosition) * invDelta;
        _speed = _velocity != Vector3d.Zero ? _velocity.Magnitude : Fixed64.Zero;
        _acceleration = (_velocity - previousVelocity) * invDelta;

        if (Steering!.ShouldMove && _acceleration != Vector3d.Zero)
            _stuckThresholdSpeed = (_acceleration / TrailblazerManager.FrameRate).Magnitude;
        else
            _stuckThresholdSpeed = Fixed64.Zero;

        _positionDelta = Vector3d.Zero;
        _velocityDelta = Vector3d.Zero;

        Motor!.FinalizeTraversal(Position, LastPosition, Rotation, _frameCondition, newFootPosition: GetFootPosition());

        // If the object is currently following a guided path, 
        // reset only the transient request state to preserve path-following values.
        if (IsGuideded)
            _frameRequest.ResetTransient();
        else
            _frameRequest.Reset();
    }

    /// <summary>
    /// Notifies the object that a collision occurred so collision-driven subsystem responses can run on the next simulation step.
    /// </summary>
    public virtual void NotifyCollision()
    {
        if (!IsActive) return;

        Turning!.NotifyCollision();
    }

    #endregion

    #region Traversal Condition Management

    /// <summary>
    /// Updates the object to a grounded state using a host-provided platform snapshot plus surface settings.
    /// Inert snapshots still describe the contacted surface but opt out of moving-platform carry logic.
    /// </summary>
    public virtual void SetGroundContact(
        Fixed64 surfaceLevel,
        PlatformSnapshot platform = default,
        Fixed64? surfaceFriction = null,
        MotionTransfer motionTransfer = MotionTransfer.None,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        SetTrekCondition(
            medium: TraversalMedium.Solid,
            surfaceLevel: surfaceLevel,
            surfaceCondition: new GroundCondition()
            {
                Platform = platform,
                SurfaceFriction = surfaceFriction ?? Fixed64.Zero,
                MotionTransferState = motionTransfer
            },
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Updates the object to an airborne state while preserving the last known ground condition unless an override is provided.
    /// </summary>
    public virtual void SetAirborne(
        Fixed64? surfaceLevel = null,
        GroundCondition? launchCondition = null,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        SetTrekCondition(
            medium: TraversalMedium.Gas,
            surfaceLevel: surfaceLevel,
            surfaceCondition: launchCondition,
            replaceGroundContact: launchCondition.HasValue,
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Updates the object to a water-contact state and clears any grounded platform contact.
    /// </summary>
    public virtual void SetWaterContact(
        Fixed64 surfaceLevel,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        SetTrekCondition(
            medium: TraversalMedium.Liquid,
            surfaceLevel: surfaceLevel,
            surfaceCondition: null,
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Updates the scout’s traversal state, including its current medium and surface information.
    /// </summary>
    /// <remarks>
    /// Make sure to update this before the next <see cref="CommitFrameMotion"/> so <see cref="NavMotor.FinalizeTraversal"/> can update its state.
    /// If the motor must see the new snapshot before the next <see cref="Simulate"/>, either pass
    /// <paramref name="updateMotorState"/> as <c>true</c> or call <see cref="SyncCurrentTrekConditionToMotor"/>.
    /// </remarks>
    /// <param name="medium">The traversal medium (e.g., ground, air, water).</param>
    /// <param name="surfaceLevel">The vertical surface level, if applicable.</param>
    /// <param name="surfaceCondition">The ground state data, if applicable.</param>
    /// <param name="replaceGroundContact">Whether to replace the current ground contact platform.  This should be true when entering water or jumping off a platform, but false when maintaining contact with the same surface across frames.</param>
    /// <param name="ceilingLevel">The vertical ceiling level, if applicable.</param>
    /// <param name="updateMotorState">Flags whether or not to update the motor's internal surface state.  Otherwise, it should be updated at the end of the frame.</param>
    public virtual void SetTrekCondition(
        TraversalMedium? medium = null,
        Fixed64? surfaceLevel = null,
        GroundCondition? surfaceCondition = null,
        bool replaceGroundContact = true,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        if (!IsActive)
            return;

        _frameCondition.Medium = medium ?? _frameCondition.Medium;
        _frameCondition.SurfaceLevel = surfaceLevel ?? _frameCondition.SurfaceLevel;
        if (replaceGroundContact)
            _frameCondition.GroundState = surfaceCondition;
        _frameCondition.CeilingLevel = ceilingLevel ?? _frameCondition.CeilingLevel;

        if (updateMotorState)
            SyncCurrentTrekConditionToMotor();
    }

    /// <summary>
    /// Pushes the current traversal snapshot into the motor before the next traversal phase begins.
    /// </summary>
    public virtual void SyncCurrentTrekConditionToMotor()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before syncing traversal state to the motor.");

        Motor!.SyncTraversalState(_frameCondition);
    }

    /// <summary>
    /// Replaces the current traversal state with the given one.
    /// </summary>
    /// <param name="state">The new traversal condition to apply.</param>
    /// <param name="updateMotorState">Flags whether or not to immediately sync the new traversal snapshot into the motor before the next traversal step.</param>
    public virtual void ReplaceTrekCondition(TrekCondition state, bool updateMotorState)
    {
        if (updateMotorState && !IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before syncing traversal state to the motor.");

        _frameCondition = state.Clone();
        if (updateMotorState)
            SyncCurrentTrekConditionToMotor();
    }

    /// <summary>
    /// Checks and updates the current traversal condition.
    /// </summary>
    public abstract void CheckTrekCondition();

    #endregion

    #region Deltas - Position / Velocity / Rotation

    /// <summary>
    /// Applies a positional delta to the current position and updates the last known position accordingly.
    /// </summary>
    /// <remarks>
    /// This method adjusts both the current and last positions to maintain consistent velocity calculations. 
    /// Use this method to apply external position changes without affecting velocity tracking.
    /// </remarks>
    /// <param name="delta">
    /// The vector representing the positional change to apply. If the vector is <see cref="Vector3d.Zero"/>, no change is made.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddPositionDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        _positionDelta += delta;
        // shift last position so it doesn't alter object's velocity
        _lastPosition += delta;
    }

    /// <summary>
    /// Applies the specified rotation delta to the current rotation state.
    /// </summary>
    /// <param name="delta">
    /// The rotation delta to apply. Must not be <see cref="FixedQuaternion.Identity"/>; otherwise, no change is made.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ApplyRotationDelta(FixedQuaternion delta)
    {
        if (delta == FixedQuaternion.Identity) return;

        _rotationDelta *= delta;
    }

    /// <summary>
    /// Adds the specified velocity change to the current velocity delta.
    /// </summary>
    /// <param name="delta">The velocity change to add. If this value is <see cref="Vector3d.Zero"/>, no change is applied.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddVelocityDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        // assume a mass of 1...for now
        _velocityDelta += delta;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Calculates the world-space position of the object's foot, adjusted by the configured foot position offset.
    /// </summary>
    /// <remarks>
    /// Use this method to obtain the precise ground contact point for the object, 
    /// which may be offset from its origin depending on the foot adjustment value.
    /// </remarks>
    /// <returns>
    /// A <see cref="Vector3d"/> representing the foot position in world coordinates, 
    /// or <see langword="null"/> if the position is undefined.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector3d? GetFootPosition()
    {
        return Position + Vector3d.Down * FootPositionAdjust;
    }

    /// <summary>
    /// Generates a new globally unique identifier (GUID) for use in object identification or tracking.
    /// </summary>
    /// <remarks>Override this method to customize GUID generation logic if a different strategy is required by derived classes.</remarks>
    /// <returns>A new <see cref="Guid"/> value that is guaranteed to be unique across space and time.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual Guid GenerateGUID() => NavigatorGlobalIdAllocator.Create();

    private void PrepareGuidedIntentState()
    {
        if (!IsGuideded)
            return;

        if (TryClearInactiveGuidedClimbIntent())
            return;

        _frameRequest.IsRequestingClimb = _guidedClimbIntent;
    }

    private void SyncGuidedIntentStateFromSteering(bool activatedGuidedHandoff, bool handoffRequestedClimb)
    {
        if (!IsGuideded)
            return;

        if (TryClearInactiveGuidedClimbIntent())
            return;

        if (_guidedClimbIntentMode == GuidedClimbIntentMode.Auto
            && Steering != null
            && Steering.CurrentRouteTopologyVersion != _lastSeenGuidedRouteTopologyVersion)
        {
            bool resolvedRouteRequestsClimb = Steering.CurrentRouteRequestsClimbIntent;
            bool shouldDeferHandoffBootstrapClear =
                activatedGuidedHandoff
                && handoffRequestedClimb
                && !resolvedRouteRequestsClimb;
            if (!shouldDeferHandoffBootstrapClear)
            {
                _guidedClimbIntent = resolvedRouteRequestsClimb;
                _lastSeenGuidedRouteTopologyVersion = Steering.CurrentRouteTopologyVersion;
            }
        }

        _frameRequest.IsRequestingClimb = _guidedClimbIntent;
    }

    private bool TryClearInactiveGuidedClimbIntent()
    {
        if (Steering?.CurrentRequest != null
            || _pendingGuidedVolumeExitHandoff != null)
        {
            return false;
        }

        ResetGuidedClimbIntentState();
        _frameRequest.IsRequestingClimb = false;
        return true;
    }

    private bool TryActivatePendingGuidedVolumeExitHandoff(out bool handoffRequestedClimb)
    {
        handoffRequestedClimb = false;
        if (!IsGuideded
            || _pendingGuidedVolumeExitHandoff == null
            || Steering == null
            || Steering.ShouldMove
            || Steering.CurrentRequest != null)
        {
            return false;
        }

        if (!_pendingGuidedVolumeExitHandoff.TryCreateFollowupRequest(Position, Size, out IPathRequest? followupRequest)
            || followupRequest == null)
        {
            return false;
        }

        GuidedVolumeExitHandoff handoff = _pendingGuidedVolumeExitHandoff;
        _pendingGuidedVolumeExitHandoff = null;

        Steering.ApplyPathRequest(followupRequest, handoff.MovementGroupId);
        CaptureGuidedRouteTopologyVersion();
        _frameRequest.IsRequestingFlight = false;
        _frameRequest.IsRequestingSwim = false;
        handoffRequestedClimb = handoff.IsRequestingClimb;
        if (_guidedClimbIntentMode == GuidedClimbIntentMode.Auto)
            _guidedClimbIntent = handoffRequestedClimb;

        _frameRequest.IsRequestingClimb = _guidedClimbIntent;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetGuidedClimbIntent(bool status, GuidedClimbIntentMode mode)
    {
        _guidedClimbIntent = status;
        _guidedClimbIntentMode = mode;
        _frameRequest.IsRequestingClimb = status;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetGuidedClimbIntentState()
    {
        _guidedClimbIntent = false;
        _guidedClimbIntentMode = GuidedClimbIntentMode.Auto;
        _lastSeenGuidedRouteTopologyVersion = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CaptureGuidedRouteTopologyVersion() =>
        _lastSeenGuidedRouteTopologyVersion = Steering?.CurrentRouteTopologyVersion ?? 0;

    #endregion

    #region Occupancy Mangement

    /// <summary>
    /// Checks and updates the occupancy status of the current and previous voxels based on the object's position.
    /// </summary>
    /// <remarks>
    /// This method ensures that the object is registered as an occupant in the correct voxel and
    /// removed from the previous voxel if the position has changed. 
    /// It should be called whenever the object's position may have changed to maintain accurate occupancy tracking.
    /// </remarks>
    /// <param name="init">Indicates whether the occupancy check is being performed during initialization. 
    /// If set to <see langword="true"/>, the check is performed regardless of position changes.
    /// </param>
    protected virtual void CheckVoxelOccupancy(bool init = false)
    {
        if (!init && Position == LastPosition) return;

        bool voxelFound = TrailblazerWorldManager.TryGetGridAndVoxel(
            Position,
            out VoxelGrid? curGrid,
            out Voxel? curVoxel);
        if (!voxelFound) return;

        if (curGrid!.TryAddVoxelOccupant(curVoxel!, this) == false)
        {
            TrailblazerLogger.Channel.Warn($"Navigator {GlobalId} failed to register occupancy in voxel {curVoxel!.Index} of grid {curGrid} at position {Position}.");
            return;
        }

        bool lastVoxelFound = TrailblazerWorldManager.TryGetGridAndVoxel(
            LastPosition,
            out VoxelGrid? lastGrid,
            out Voxel? lastVoxel);

        // check if position is still within the same voxel
        if (!lastVoxelFound || curVoxel == lastVoxel)
            return;

        lastGrid!.TryRemoveVoxelOccupant(lastVoxel!, this);
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        GuidedVolumeExitHandoff? pendingGuidedVolumeExitHandoff = _pendingGuidedVolumeExitHandoff;
        if (chronicler.Mode == SerializationMode.Loading && pendingGuidedVolumeExitHandoff == null)
            pendingGuidedVolumeExitHandoff = new GuidedVolumeExitHandoff();

        RecordValues.Look(chronicler, ref _position, "Position", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastPosition, "LastPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _rotation, "Rotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref _velocity, "Velocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _speed, "Speed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _acceleration, "Acceleration", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _size, "Size", Fixed64.One);
        RecordValues.Look(chronicler, ref _footPositionAdjust, "FootPositionAdjust", DefaultFootPositionAdjust);
        RecordValues.Look(chronicler, ref _guidedPathMode, "GuidedPathMode", SolidPathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref _guidedAllowUnwalkableEndpoints, "GuidedAllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref _guidedAllowTraversalTransitions, "GuidedAllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref _guidedMaxClimbHeight, "GuidedMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref _guidedAStarHeuristic, "GuidedAStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref _guidedFlowFieldExtraFloodRange, "GuidedFlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref _globalId, "GlobalId", Guid.Empty);
        RecordValues.Look(chronicler, ref _occupantGroupId, "OccupantGroupId", (byte)1);
        RecordValues.Look(chronicler, ref _isLockedOn, "IsLockedOn", false);
        RecordValues.Look(chronicler, ref _stuckThresholdSpeed, "StuckThresholdSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _isGuideded, "IsGuideded", false);
        RecordValues.Look(chronicler, ref _guidedClimbIntent, "GuidedClimbIntent", false);
        RecordValues.Look(chronicler, ref _guidedClimbIntentMode, "GuidedClimbIntentMode", GuidedClimbIntentMode.Auto);
        RecordValues.Look(chronicler, ref _lastSeenGuidedRouteTopologyVersion, "LastSeenGuidedRouteTopologyVersion", 0);
        RecordDeepStruct.Look(chronicler, ref _frameCondition, "FrameCondition");
        RecordDeepStruct.Look(chronicler, ref _frameRequest, "FrameRequest");
        RecordDeep.Look(chronicler, ref pendingGuidedVolumeExitHandoff!, "PendingGuidedVolumeExitHandoff");
        if (_steering != null)
            RecordDeep.Look(chronicler, ref _steering, "Steering");
        if (_turning != null)
            RecordDeep.Look(chronicler, ref _turning, "Turning");
        if (_motor != null)
            RecordDeep.Look(chronicler, ref _motor, "Motor");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            _pendingGuidedVolumeExitHandoff = pendingGuidedVolumeExitHandoff?.IsValid == true
                ? pendingGuidedVolumeExitHandoff
                : null;

            Forward = Rotation != FixedQuaternion.Identity
                ? Rotation.Rotate(Vector3d.Forward)
                : Vector3d.Forward;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;
            _rotationDelta = FixedQuaternion.Identity;
            _isSet = true;
            _isInitialized = Motor != null;

            _steering?.UpdateOwnerRadius(Radius);

            _turning?.OnInitialize(Radius);

            CheckVoxelOccupancy(true);
        }
    }

    #endregion
}
