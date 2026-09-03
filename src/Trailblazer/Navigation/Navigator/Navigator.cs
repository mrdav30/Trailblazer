//=======================================================================
// Navigator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using Chronicler;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Base class for an object that owns movement, traversal state, and simulation flow.
/// </summary>
/// <remarks>
/// This class acts as a bridge between the simulation logic and the entity's external representation.
/// It defines common traversal behaviors and lifecycle methods that can be extended by concrete implementations.
/// </remarks>
[Serializable]
public abstract partial class Navigator : INavigate, IRecordable
{
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

    /// <inheritdoc cref="NavigationProfile"/>
    private NavigationAgentProfile _navigationProfile;

    /// <summary>
    /// Stable runtime identity used for occupancy and steering coordination.
    /// </summary>
    /// <remarks>
    /// By default this is allocated deterministically from object setup order.
    /// Hosts can override it during setup
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

    private bool _isGuided;

    /// <summary>
    /// The change in position to apply during the current simulation frame.
    /// </summary>
    protected Vector3d _positionDelta;

    /// <summary>
    /// The change in rotation to apply during the current simulation frame.
    /// </summary>
    protected FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

    /// <summary>
    /// The locomotion displacement accumulated for the current fixed frame, with the timestep already applied.
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

    private NavigationTransitionInstruction? _pendingTransition;

    private NavigationCommittedCellState? _lastCommittedCell;

    private TrailblazerWorldContext? _context;

    private NavigatorHeightmapGroundingSettings _heightmapGrounding = new();

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
    /// <remarks>Derived from committed world displacement divided by the context's fixed timestep.</remarks>
    public Vector3d Velocity => _velocity;

    /// <inheritdoc/>
    public Fixed64 Speed => _speed;

    /// <inheritdoc/>
    /// <remarks>Derived from the change in committed velocity divided by the context's fixed timestep.</remarks>
    public Vector3d Acceleration => _acceleration;

    /// <summary>
    /// Minimum velocity threshold used to determine if the object is considered stuck.
    /// </summary>
    public Fixed64 StuckThresholdSpeed => _stuckThresholdSpeed;

    /// <summary>
    /// Indicates whether the Navigator is currently active and ready for simulation.
    /// </summary>
    public bool IsActive => _isSet && _isInitialized;

    /// <summary>
    /// Gets the world context this navigator is bound to.
    /// </summary>
    public TrailblazerWorldContext? Context => _context;

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
    /// Indicates whether the current traversal session owns navigation guidance.
    /// </summary>
    public bool IsGuided => _isGuided;

    #endregion

    #region Settings

    /// <summary>
    /// Gets the exact immutable navigation profile supplied during setup.
    /// </summary>
    public NavigationAgentProfile NavigationProfile => _navigationProfile;

    /// <inheritdoc/>
    public KinematicBodyShape BodyShape => NavigationProfile.Shape;

    /// <inheritdoc/>
    public Fixed64 Radius => BodyShape.Radius;

    /// <summary>
    /// Gets the derived world-space foot point used by navigation and locomotion.
    /// </summary>
    public Vector3d FootPosition => Position + Vector3d.Down * BodyShape.RootToFootOffsetY;

    /// <summary>
    /// Gets the opt-in heightmap grounding settings owned by this navigator.
    /// </summary>
    public NavigatorHeightmapGroundingSettings HeightmapGrounding => _heightmapGrounding;

    /// <summary>
    /// Gets or sets a value indicating whether the object is currently locked on to a target.
    /// </summary>
    public bool IsLockedOn { get => _isLockedOn; set => _isLockedOn = value; }

    /// <summary>Gets the exact transition action currently awaiting host completion.</summary>
    public NavigationTransitionInstruction? PendingTransition => _pendingTransition;

    /// <summary>Gets the effective navigation cell resolved after the latest motion commit.</summary>
    public NavigationCommittedCellState? LastCommittedCell => _lastCommittedCell;

    /// <summary>Occurs after committed motion enters a different effective cell or leaves navigation data.</summary>
    public event Action<NavigationCommittedCellState?>? CommittedCellChanged;

    #endregion

    #region Voxel Occupancy

    /// <inheritdoc cref="_globalId"/>
    public Guid GlobalId => _globalId;

    /// <inheritdoc />
    public byte OccupantGroupId { get => _occupantGroupId; set => _occupantGroupId = value; }

    #endregion

    #region Setup / Initialization

    /// <summary>
    /// Initializes a new unbound navigator. Bind it to a context before setup.
    /// </summary>
    protected Navigator() { }

    /// <summary>
    /// Initializes a new navigator bound to the supplied world context.
    /// </summary>
    protected Navigator(TrailblazerWorldContext context)
    {
        BindContext(context);
    }

    /// <summary>
    /// Binds this navigator to a world context before setup.
    /// </summary>
    public virtual void BindContext(TrailblazerWorldContext context)
    {
        TrailblazerWorldContext.ThrowIfUnusable(context);

        if (ReferenceEquals(_context, context))
            return;

        SwiftThrowHelper.ThrowIfTrue(
            _isSet || _isInitialized,
            message: "Navigator context cannot be changed after setup. Call Reset() before rebinding.");

        _context = context;
        _steering?.BindContext(context);
        _motor?.BindContext(context);
        _turning?.BindContext(context);
    }

    /// <summary>
    /// Initializes and activates the object with the specified condition, position, and optional parameters.
    /// </summary>
    /// <param name="context">The world context that owns this navigator.</param>
    /// <param name="condition">The condition that determines how the object is initialized and activated.</param>
    /// <param name="position">The position in world coordinates where the object will be placed.</param>
    /// <param name="rotation">The optional rotation to apply to the object. If null, a default rotation is used.</param>
    /// <param name="velocity">The optional initial velocity of the object. If null, the object is initialized with zero velocity.</param>
    /// <param name="navigationProfile">The exact body geometry and traversal capabilities owned by the object.</param>
    /// <param name="globalId">The optional global identifier for the object. If null, a new identifier may be generated.</param>
    public virtual void Activate(
        TrailblazerWorldContext context,
        TrekCondition condition,
        Vector3d position,
        NavigationAgentProfile navigationProfile,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Guid? globalId = null)
    {
        BindContext(context);
        Activate(condition, position, navigationProfile, rotation, velocity, globalId);
    }

    /// <summary>
    /// Initializes and activates the object with the specified condition, position, and optional parameters.
    /// </summary>
    /// <param name="condition">The condition that determines how the object is initialized and activated.</param>
    /// <param name="position">The position in world coordinates where the object will be placed.</param>
    /// <param name="rotation">The optional rotation to apply to the object. If null, a default rotation is used.</param>
    /// <param name="velocity">The optional initial velocity of the object. If null, the object is initialized with zero velocity.</param>
    /// <param name="navigationProfile">The exact body geometry and traversal capabilities owned by the object.</param>
    /// <param name="globalId">The optional global identifier for the object. If null, a new identifier may be generated.</param>
    public virtual void Activate(
        TrekCondition condition,
        Vector3d position,
        NavigationAgentProfile navigationProfile,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Guid? globalId = null)
    {
        Setup(position, navigationProfile, rotation, velocity, globalId);
        Initialize(condition);
    }

    /// <summary>
    /// Sets the initial position, exact navigation profile, rotation, velocity, and optional stable identity.
    /// </summary>
    /// <param name="context">The world context that owns this navigator.</param>
    /// <param name="position">Initial world-space position.</param>
    /// <param name="rotation">Optional starting rotation.</param>
    /// <param name="velocity">Optional initial velocity.</param>
    /// <param name="navigationProfile">The exact body geometry and traversal capabilities owned by the object.</param>
    /// <param name="globalId">Optional host-provided stable identity. When omitted, Trailblazer assigns one deterministically from setup order.</param>
    public virtual void Setup(
        TrailblazerWorldContext context,
        Vector3d position,
        NavigationAgentProfile navigationProfile,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Guid? globalId = null)
    {
        BindContext(context);
        Setup(position, navigationProfile, rotation, velocity, globalId);
    }

    /// <summary>
    /// Sets the initial position, exact navigation profile, rotation, velocity, and optional stable identity.
    /// </summary>
    /// <param name="position">Initial world-space position.</param>
    /// <param name="rotation">Optional starting rotation.</param>
    /// <param name="velocity">Optional initial velocity.</param>
    /// <param name="navigationProfile">The exact body geometry and traversal capabilities owned by the object.</param>
    /// <param name="globalId">Optional host-provided stable identity. When omitted, Trailblazer assigns one deterministically from setup order.</param>
    public virtual void Setup(
        Vector3d position,
        NavigationAgentProfile navigationProfile,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Guid? globalId = null)
    {
        SwiftThrowHelper.ThrowIfTrue(
            _isSet || _isInitialized,
            message: "Navigator is already set up. Call Reset() before setting it up again.");

        EnsureContextForSetup();
        navigationProfile.Validate(nameof(navigationProfile));

        SwiftThrowHelper.ThrowIfArgument(
            globalId.HasValue && globalId.Value == Guid.Empty,
            paramName: nameof(globalId),
            message: "Navigator globalId cannot be Guid.Empty.");

        _globalId = globalId ?? GenerateGUID();

        _lastPosition = _position = position;
        _rotation = rotation ?? FixedQuaternion.Identity;
        if (_rotation != FixedQuaternion.Identity)
            Forward = _rotation.Rotate(Vector3d.Forward);
        else
            Forward = Vector3d.Forward;
        _velocity = velocity ?? Vector3d.Zero;
        _navigationProfile = navigationProfile;

        _isSet = true;
    }

    /// <summary>
    /// Initializes the object by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    public virtual void Initialize(TrekCondition condition)
    {
        TrailblazerWorldContext context = RequireContext();

        _frameCondition = condition.Clone();

        _steering = new NavSteering(context);
        _steering.BindPendingTransitionOwner(this);

        _motor = new NavMotor(context, _frameCondition, CreateLocomotionProfile());
        _motor.SetVelocity(Velocity);

        _turning = new NavTurning(context, Radius);

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
        _isGuided = false;
        _pendingTransition = null;
        _lastCommittedCell = null;
        _heightmapGrounding.Reset();
        _steering?.UnbindPendingTransitionOwner(this);
        _steering?.Reset();

        if (_context != null && !_context.IsDisposed && _context.World.IsActive)
            GridOccupantManager.TryDeregister(_context.World, this);

        _isSet = false;
        _isInitialized = false;
        _context = null;
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
        SwiftThrowHelper.ThrowIfTrue(
            !IsActive,
            message: "Navigator must be Setup and Initialized before prewarming movement groups.");

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

        Steering!.StopMove();
        _isGuided = false;
        _pendingTransition = null;
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
    /// Applies complete immutable graph-backed A* or flow-field intent.
    /// </summary>
    /// <param name="query">The exact query intent whose start point equals the current derived foot position.</param>
    /// <param name="rate">Desired movement rate.</param>
    /// <param name="isRequestingFlight">Whether the object intends to fly or glide during traversal.</param>
    /// <param name="isRequestingSwim">Whether the object intends to actively swim during traversal.</param>
    /// <param name="isRequestingClimb">Whether the object explicitly intends to climb during traversal.</param>
    /// <param name="isRequestingJump">Whether the object intends to jump during traversal.</param>
    /// <param name="canAffordJump">Frame-owned jump affordability answer for this request.</param>
    /// <param name="groupId">Optional shared movement-group identifier.</param>
    /// <exception cref="ArgumentException">Thrown when the query does not match the Navigator's exact navigation profile.</exception>
    public virtual void ApplyGuidedTrekRequest(
        PathQuery query,
        TrekRate? rate = null,
        bool? isRequestingFlight = null,
        bool? isRequestingSwim = null,
        bool? isRequestingClimb = null,
        bool? isRequestingJump = null,
        bool canAffordJump = true,
        int groupId = -1)
    {
        if (!IsActive)
            return;

        ValidateGuidedSurfaceQuery(query);
        _pendingTransition = null;
        _isGuided = true;
        _frameRequest.SetRequest(
            direction: Vector3d.Zero,
            rate: rate ?? TrekRate.Stationary,
            isRequestingJump: isRequestingJump ?? false,
            isRequestingFlight: isRequestingFlight ?? false,
            isRequestingSwim: isRequestingSwim ?? false,
            isRequestingClimb: isRequestingClimb ?? false,
            facingDirection: null,
            canAffordJump: canAffordJump);

        Steering!.ApplyPathQuery(query, groupId);
    }

    /// <summary>
    /// Configures explicit heightmap grounding for concrete navigators that opt in during traversal checks.
    /// </summary>
    /// <param name="mode">Heightmap grounding behavior to use when <see cref="TryApplyHeightmapGrounding"/> is called.</param>
    /// <param name="layerName">Optional initial layer preference used before an active layer is established.</param>
    /// <param name="groundOffset">Optional extra root offset above the sampled ground Y.</param>
    /// <param name="snapTolerance">Optional maximum root-Y correction allowed for positional projection.</param>
    public virtual void ConfigureHeightmapGrounding(
        HeightmapGroundingMode mode,
        string? layerName = null,
        Fixed64? groundOffset = null,
        Fixed64? snapTolerance = null)
    {
        _heightmapGrounding.Configure(mode, layerName, groundOffset, snapTolerance);
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
        => _frameRequest.IsRequestingClimb = status;

    /// <summary>Completes the exact pending semantic action owned by the active guide.</summary>
    public virtual NavigationGuideStatus CompletePendingTransition(
        in NavigationTransitionInstruction instruction)
    {
        if (_pendingTransition == null || Steering == null)
            return NavigationGuideStatus.Stale;

        NavigationGuideStatus status = Steering.CompletePendingTransition(instruction);
        if (status != NavigationGuideStatus.Success)
            return status;

        TraversalTransitionLocomotionHints hints = _pendingTransition.Value.LocomotionHints;
        _pendingTransition = null;
        ApplyTransitionLocomotionHints(hints, pending: false);
        return NavigationGuideStatus.Success;
    }

    internal void NotifySteeringSessionEnded()
    {
        _isGuided = false;
        if (_pendingTransition == null)
            return;

        _pendingTransition = null;
        ApplyTransitionLocomotionHints(TraversalTransitionLocomotionHints.None, pending: false);
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

        // Lock-on strafing/backpedaling keeps the current facing unless the host
        // explicitly supplies a facing override or the request is treated as sprinting.
        if (!IsGuided
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
        SwiftThrowHelper.ThrowIfTrue(
            !IsActive,
            message: "Navigator must be Setup and Initialized before Simulate().");

        Vector3d heading = Vector3d.Zero;
        if (IsGuided)
        {
            if (_pendingTransition is NavigationTransitionInstruction pending)
            {
                if (Steering!.PendingTransitionGuideStatus == NavigationGuideStatus.Stale)
                    Steering.StopMove();
                else
                    ApplyTransitionLocomotionHints(pending.LocomotionHints, pending: true);
            }
            else
            {
                heading = Steering!.GetHeading(this, out NavigationTransitionInstruction? transition);
                if (transition.HasValue)
                {
                    _pendingTransition = transition;
                    ApplyTransitionLocomotionHints(transition.Value.LocomotionHints, pending: true);
                    heading = Vector3d.Zero;
                }
            }
        }

        _frameRequest.SetTransientState(
             origin: Position,
             footPosition: FootPosition,
             rotation: Rotation,
             direction: IsGuided ? heading : null
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
    /// Commits queued motion and finalizes controller and motor state for one authoritative fixed step.
    /// </summary>
    /// <remarks>
    /// Call once per fixed simulation frame after <see cref="Simulate"/>, not once per rendering frame.
    /// The queued locomotion and platform values are displacements with the timestep already applied.
    /// After traversal-state corrections, velocity and acceleration are derived from committed motion
    /// using the context's fixed timestep. This method does not resolve engine physics or semantic actions.
    /// </remarks>
    public virtual void CommitFrameMotion()
    {
        SwiftThrowHelper.ThrowIfTrue(
            !IsActive,
            message: "Navigator must be Setup and Initialized before CommitFrameMotion().");

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
        TrailblazerWorldContext context = RequireContext();
        Fixed64 invDelta = context.InvDeltaTime;
        _velocity = (Position - LastPosition) * invDelta;
        _speed = _velocity != Vector3d.Zero ? _velocity.Magnitude : Fixed64.Zero;
        _acceleration = (_velocity - previousVelocity) * invDelta;

        if (Steering!.ShouldMove && _acceleration != Vector3d.Zero)
            _stuckThresholdSpeed = (_acceleration / context.FrameRate).Magnitude;
        else
            _stuckThresholdSpeed = Fixed64.Zero;

        _positionDelta = Vector3d.Zero;
        _velocityDelta = Vector3d.Zero;

        Motor!.FinalizeTraversal(Position, LastPosition, Rotation, _frameCondition, newFootPosition: FootPosition);

        // If the object is currently following a guided path,
        // reset only the transient request state to preserve path-following values.
        if (IsGuided)
            _frameRequest.ResetTransient();
        else
            _frameRequest.Reset();

        RebuildCommittedCellState(emitChange: true);
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
        SwiftThrowHelper.ThrowIfTrue(
            !IsActive,
            message: "Navigator must be Setup and Initialized before syncing traversal state to the motor.");

        Motor!.SyncTraversalState(_frameCondition);
    }

    /// <summary>
    /// Replaces the current traversal state with the given one.
    /// </summary>
    /// <param name="state">The new traversal condition to apply.</param>
    /// <param name="updateMotorState">Flags whether or not to immediately sync the new traversal snapshot into the motor before the next traversal step.</param>
    public virtual void ReplaceTrekCondition(TrekCondition state, bool updateMotorState)
    {
        SwiftThrowHelper.ThrowIfTrue(
            updateMotorState && !IsActive,
            message: "Navigator must be Setup and Initialized before syncing traversal state to the motor.");

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
    /// Queues an additional world-space displacement for the next <see cref="CommitFrameMotion"/>.
    /// </summary>
    /// <remarks>
    /// The base commit implementation includes this displacement when deriving velocity from the resulting position.
    /// The supplied value is already a displacement; no timestep or inverse-mass conversion is applied here.
    /// </remarks>
    /// <param name="delta">
    /// The vector representing the positional change to apply. If the vector is <see cref="Vector3d.Zero"/>, no change is made.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddPositionDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        _positionDelta += delta;
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
    /// Queues a world-space locomotion displacement for the next <see cref="CommitFrameMotion"/>.
    /// </summary>
    /// <remarks>
    /// Despite the method name, delta is displacement in world units, not a velocity change, force, or impulse.
    /// Motor traversal has already applied the fixed timestep; do not apply another timestep or inverse-mass conversion.
    /// </remarks>
    /// <param name="delta">The locomotion displacement to add. Zero has no effect.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddVelocityDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        _velocityDelta += delta;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Generates a new globally unique identifier (GUID) for use in object identification or tracking.
    /// </summary>
    /// <remarks>Override this method to customize GUID generation logic if a different strategy is required by derived classes.</remarks>
    /// <returns>A new <see cref="Guid"/> value that is guaranteed to be unique across space and time.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual Guid GenerateGUID()
    {
        TrailblazerWorldContext context = RequireContext();
        return context.Navigation.CreateNavigatorId();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TrailblazerWorldContext EnsureContextForSetup()
    {
        if (_context != null)
            return RequireContext();

        throw new InvalidOperationException(
            "Navigator requires a TrailblazerWorldContext before setup. Pass a context to the constructor, " +
            "call BindContext(context), or use Setup(context, ...).");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TrailblazerWorldContext RequireContext()
    {
        SwiftThrowHelper.ThrowIfTrue(
            _context == null,
            message: "Navigator requires a TrailblazerWorldContext before simulation.");

        TrailblazerWorldContext.ThrowIfUnusable(_context);
        return _context;
    }

    private void RebuildCommittedCellState(bool emitChange)
    {
        NavigationCommittedCellResolveStatus status = TryResolveCommittedCellState(
            out NavigationCommittedCellState resolved);
        if (status == NavigationCommittedCellResolveStatus.Unavailable)
            return;

        NavigationCommittedCellState? previous = _lastCommittedCell;
        NavigationCommittedCellState? current = status == NavigationCommittedCellResolveStatus.Resolved
            ? resolved
            : null;
        _lastCommittedCell = current;
        _steering?.ConsumePendingCommittedAreaPolicy();

        if (emitChange && !RepresentsSameCellEntry(previous, current))
            CommittedCellChanged?.Invoke(current);
    }

    private NavigationCommittedCellResolveStatus TryResolveCommittedCellState(
        out NavigationCommittedCellState state)
    {
        TrailblazerWorldContext context = RequireContext();
        if (!NavigatorOccupancyTracker.TryResolveVoxel(
                context.World,
                Position,
                out VoxelGrid? grid,
                out Voxel? voxel))
        {
            state = default;
            return NavigationCommittedCellResolveStatus.NoCell;
        }

        NavigationCommittedCellResolveStatus status = context.Pathing.TryResolveCommittedCell(
            grid!.Configuration.ToGridKey(),
            voxel!.WorldIndex,
            out NavigationCellAddress address,
            out NavigationAreaId area,
            out long graphVersion);
        if (status != NavigationCommittedCellResolveStatus.Resolved)
        {
            state = default;
            return status;
        }

        state = new NavigationCommittedCellState(
            address,
            area,
            _frameCondition.Medium,
            graphVersion,
            _steering?.AreaPolicyForCommit,
            context.FrameCount);
        return NavigationCommittedCellResolveStatus.Resolved;
    }

    private static bool RepresentsSameCellEntry(
        NavigationCommittedCellState? first,
        NavigationCommittedCellState? second)
    {
        if (!first.HasValue || !second.HasValue)
            return first.HasValue == second.HasValue;

        NavigationCommittedCellState left = first.Value;
        NavigationCommittedCellState right = second.Value;
        return left.Address == right.Address
            && left.Area == right.Area
            && left.Medium == right.Medium;
    }

    private void ValidateGuidedSurfaceQuery(PathQuery query)
    {
        SwiftThrowHelper.ThrowIfArgument(
            query.Agent != NavigationProfile
            || query.Start.Position != FootPosition
            || query.Traversal.StartMedium != _frameCondition.Medium,
            paramName: nameof(query),
            message: "Guided queries must use the Navigator profile, current foot position, and current traversal medium.");
    }

    private void ApplyTransitionLocomotionHints(
        TraversalTransitionLocomotionHints hints,
        bool pending)
    {
        _frameRequest.IsRequestingClimb = pending
            ? (hints & TraversalTransitionLocomotionHints.RequestClimb) != 0
            : (hints & TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion) != 0;
    }

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
        NavigatorOccupancyTracker.Update(
            RequireContext().World,
            this,
            Position,
            init ? Position : LastPosition,
            init);
    }

    #endregion
}
