using Chronicler;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Animation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Base class representing a navigator, responsible for handling movement, traversal state, and simulation flow.
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
    /// Default vertical offset used to determine the navigator’s contact point with the ground.
    /// </summary>
    public static readonly Fixed64 DefaultFootPositionAdjust = new(0.25f);

    #endregion

    #region State - Position / Rotation / Velocity

    public Vector3d Position { get; protected set; }

    public Vector3d LastPosition { get; protected set; }

    public FixedQuaternion Rotation { get; protected set; } = FixedQuaternion.Identity;

    public Vector3d Forward { get; protected set; }

    public Vector3d Velocity { get; protected set; }

    public Fixed64 Speed { get; protected set; }

    public Vector3d Acceleration { get; protected set; }

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

    protected bool _isSet;

    protected bool _isInitialized;

    public bool IsActive => _isSet && _isInitialized;

    #endregion

    #region State - Traversal / Steering

    /// <summary>
    /// The controller responsible for managing the navigator's desired movement direction.
    /// </summary>
    public NavSteering Steering { get; protected set; }

    /// <summary>
    /// The controller responsible for managing the navigator's rotation towards the movement direction.
    /// </summary>
    public NavTurning Turning { get; protected set; }

    /// <summary>
    /// The controller responsible for managing the navigator's movement and physics interactions.
    /// </summary>
    public NavMotor Motor { get; protected set; }

    /// <summary>
    /// Minimum velocity threshold used to determine if the navigator is considered stuck.
    /// </summary>
    public Fixed64 StuckThresholdSpeed { get; protected set; }

    /// <summary>
    /// Indicates whether the current traversal session is guided via a TrailGuide path (e.g., A* or flow field).
    /// </summary>
    public bool IsGuideded { get; protected set; }

    protected TrekCondition _frameCondition = new();

    protected TrekRequest _frameRequest = new();

    private GuidedVolumeExitHandoff _pendingGuidedVolumeExitHandoff;

    private bool _guidedClimbIntent;

    #endregion

    #region Settings

    public Fixed64 Size { get; set; } = Fixed64.One;

    public Fixed64 Radius => Size * Fixed64.Half;

    /// <summary>
    /// Adjustment factor for the foot position, used to determine ground contact points.
    /// </summary>
    public Fixed64 FootPositionAdjust { get; set; } = DefaultFootPositionAdjust;

    /// <summary>
    /// Default built-in path request mode used when guided travel does not specify an override.
    /// </summary>
    public GuidedPathMode GuidedPathMode { get; set; } = GuidedPathMode.AStar;

    /// <summary>
    /// Whether navigator-built guided requests may target unwalkable voxels.
    /// </summary>
    public bool GuidedAllowUnwalkableEndpoints { get; set; }

    /// <summary>
    /// Whether navigator-built guided requests may use authored traversal transitions for chart fallback,
    /// bounded swim exits, or bounded aerial landing handoffs.
    /// </summary>
    public bool GuidedAllowTraversalTransitions { get; set; }

    /// <summary>
    /// Default max climb height used when the navigator builds guided requests.
    /// </summary>
    public Fixed64 GuidedMaxClimbHeight { get; set; } = Fixed64.One;

    /// <summary>
    /// Default heuristic used when the navigator builds A* requests.
    /// </summary>
    public HeuristicMethod GuidedAStarHeuristic { get; set; } = HeuristicMethod.Manhattan;

    /// <summary>
    /// Default extra flood range used when the navigator builds flow-field requests.
    /// </summary>
    public int GuidedFlowFieldExtraFloodRange { get; set; } = FlowFieldPathRequest.DefaultExtraFloodRange;

    #endregion

    #region Voxel Occupancy

    /// <summary>
    /// Stable runtime identity used for occupancy and steering coordination.
    /// </summary>
    /// <remarks>
    /// By default this is allocated deterministically from navigator setup order.
    /// Hosts can override it during <see cref="Setup(Vector3d, FixedQuaternion?, Vector3d?, Fixed64?, Guid?)"/>
    /// when a broader simulation stack already owns stable agent ids.
    /// </remarks>
    public Guid GlobalId { get; protected set; }

    public byte OccupantGroupId { get; set; } = 1;

    #endregion

    #region Animation

    private INavAnimationHandler _animationHandler;

    public bool IsLockedOn { get; set; }

    public Fixed64 AnimDampTime = (Fixed64)0.1f;

    #endregion

    #region Setup / Initialization

    /// <summary>
    /// Sets the initial configuration of the navigator, including position, rotation, velocity, size, and optional stable identity.
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

        GlobalId = globalId ?? GenerateGUID();

        LastPosition = Position = position;
        Rotation = rotation ?? FixedQuaternion.Identity;
        if (Rotation != FixedQuaternion.Identity)
            Forward = Rotation.Rotate(Vector3d.Forward);
        else
            Forward = Vector3d.Forward;
        Velocity = velocity ?? Vector3d.Zero;
        Size = size ?? Fixed64.One;

        _isSet = true;
    }

    /// <summary>
    /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
    /// </summary>
    public virtual void Initialize(TrekCondition condition)
    {
        _frameCondition = condition.Clone();

        Steering = NavSteering.CreateNew(Radius);

        Motor = NavMotor.CreateNew(_frameCondition, CreateLocomotionProfile());
        Motor.SetVelocity(Velocity);

        Turning = NavTurning.CreateNew(Radius);

        CheckVoxelOccupancy(true);

        _isInitialized = true;
    }

    /// <summary>
    /// Creates the locomotion profile used when this navigator initializes its motor.
    /// </summary>
    /// <remarks>
    /// Override this to install a smaller or custom locomotion set per navigator type while preserving
    /// the default profile for callers that do not opt in.
    /// </remarks>
    protected virtual LocomotionProfile CreateLocomotionProfile()
    {
        return LocomotionProfile.CreateDefault();
    }

    public virtual void Reset()
    {
        _frameCondition.Reset();
        _frameRequest.Reset();
        IsGuideded = false;
        _pendingGuidedVolumeExitHandoff = null;
        _guidedClimbIntent = false;

        GridOccupantManager.TryDeregister(this);

        _isSet = false;
        _isInitialized = false;
    }

    #endregion

    #region Host Bindings

    /// <summary>
    /// Binds a host-owned animation handler to this navigator.
    /// </summary>
    public virtual void BindAnimationHandler(INavAnimationHandler handler)
    {
        _animationHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Unbinds any previously attached animation handler.
    /// </summary>
    public virtual void UnbindAnimationHandler()
    {
        _animationHandler = null;
    }

    /// <summary>
    /// Prewarms the steering movement-group coordinator from this navigator's currently loaded state.
    /// </summary>
    /// <remarks>
    /// This is primarily useful after loading several grouped navigators through Chronicler. Call it once
    /// for each loaded navigator before the next simulation frame if you want movement-group formation state
    /// available immediately. If it is skipped, grouped steering will still rejoin lazily on the next update.
    /// </remarks>
    public virtual void PrewarmMovementGroup()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before prewarming movement groups.");

        Steering.PrewarmMovementGroup(this);
    }

    #endregion

    #region Input / Travel Requests

    /// <summary>
    /// Constructs and applies a traversal request using high-level navigation input values.
    /// </summary>
    /// <param name="direction">Desired direction of travel.</param>
    /// <param name="rate">Rate of travel (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the agent is requesting a jump action.</param>
    /// <param name="isRequestingFlight">Whether the agent is requesting controlled flight.</param>
    /// <param name="isRequestingClimb">Whether the agent is requesting climb engagement or continuation.</param>
    /// <param name="facingDirection">Optional world-space facing direction to use instead of facing along the movement direction.</param>
    public virtual void ApplyInputTrekRequest(
        Vector3d? direction = null,
        TrekRate? rate = null,
        bool? isRequestingJump = null,
        bool? isRequestingFlight = null,
        bool? isRequestingClimb = null,
        Vector3d? facingDirection = null)
    {
        if (!IsActive) return;

        IsGuideded = false;
        _pendingGuidedVolumeExitHandoff = null;
        _guidedClimbIntent = false;
        _frameRequest.SetRequest(
                direction: direction ?? Vector3d.Zero,
                rate: rate ?? TrekRate.Stationary,
                isRequestingJump: isRequestingJump ?? false,
                isRequestingFlight: isRequestingFlight ?? false,
                isRequestingClimb: isRequestingClimb ?? false,
                facingDirection: facingDirection
        );
    }

    /// <summary>
    /// Constructs and applies a guided traversal request toward a destination using navigator-owned path request defaults.
    /// </summary>
    /// <param name="targetPosition">The desired world-space target position.</param>
    /// <param name="pathMode">Optional override for the built-in path request mode. When omitted, <see cref="GuidedPathMode"/> is used.</param>
    /// <param name="rate">Desired movement rate (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the navigator intends to jump during traversal.</param>
    /// <param name="isRequestingClimb">Whether the navigator intends to preserve climb engagement while guided travel is active.</param>
    /// <param name="groupId">Optional shared group identifier used to preserve formation offsets between navigators.</param>
    public virtual void ApplyGuidedTrekRequest(
        Vector3d targetPosition,
        GuidedPathMode? pathMode = null,
        TrekRate? rate = null,
        bool? isRequestingJump = null,
        bool? isRequestingClimb = null,
        int groupId = -1)
    {
        if (!IsActive) return;

        GuidedPathMode selectedPathMode = pathMode ?? GuidedPathMode;
        if (!TryCreateGuidedPathRequest(targetPosition, selectedPathMode, out IPathRequest pathRequest))
        {
            GridForgeLogger.Warn(
                $"Unable to create a {selectedPathMode} path request for navigator {GlobalId} at {Position} targeting {targetPosition}.");
            return;
        }

        if (_pendingGuidedVolumeExitHandoff != null)
            _pendingGuidedVolumeExitHandoff.MovementGroupId = groupId;

        _guidedClimbIntent = isRequestingClimb
            ?? ResolveGuidedClimbIntent(pathRequest, _pendingGuidedVolumeExitHandoff);

        IsGuideded = true;
        _frameRequest.SetRequest(
                direction: Vector3d.Zero,
                rate: rate ?? TrekRate.Stationary,
                isRequestingJump: isRequestingJump ?? false,
                isRequestingFlight: selectedPathMode == GuidedPathMode.Aerial,
                isRequestingClimb: _guidedClimbIntent,
                facingDirection: null
        );

        Steering.ApplyPathRequest(pathRequest, groupId);
    }

    /// <summary>
    /// Builds a concrete path request for guided travel from the navigator's current state and defaults.
    /// Subclasses can override this to support custom request types without changing steering.
    /// </summary>
    protected virtual bool TryCreateGuidedPathRequest(
        Vector3d targetPosition,
        GuidedPathMode pathMode,
        out IPathRequest pathRequest)
    {
        _pendingGuidedVolumeExitHandoff = null;

        bool success = NavigatorPathRequestFactory.TryCreate(
            origin: Position,
            targetPosition: targetPosition,
            unitSize: Size,
            pathMode: pathMode,
            fallbackChartPathMode: GuidedPathMode,
            allowUnwalkableEndpoints: GuidedAllowUnwalkableEndpoints,
            allowTraversalTransitions: GuidedAllowTraversalTransitions,
            maxClimbHeight: GuidedMaxClimbHeight,
            aStarHeuristic: GuidedAStarHeuristic,
            flowFieldExtraFloodRange: GuidedFlowFieldExtraFloodRange,
            traversalMedium: _frameCondition.Medium,
            out pathRequest,
            out GuidedVolumeExitHandoff handoff);
        if (success)
            _pendingGuidedVolumeExitHandoff = handoff;

        return success;
    }

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
    /// Called to toggle climb intent if supported by the installed locomotion profile.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ToggleGuidedClimb(bool status)
    {
        _guidedClimbIntent = status;
        _frameRequest.IsRequestingClimb = status;
    }

    /// <summary>
    /// Changes the speed at which the navigator is currently traveling without altering direction.
    /// </summary>
    /// <param name="rate">New traversal rate to apply (walk, run, etc.).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void SetGuidedTrekRate(TrekRate rate) => _frameRequest.Rate = rate;

    #endregion

    #region Simulation Lifecycle

    /// <summary>
    /// Attempts to resolve the direction the navigator should try to face for the current frame.
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
    /// Runs simulation logic for this navigator (input handling, steering, etc.).
    /// </summary>
    public virtual void Simulate()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before Simulate().");

        TryActivatePendingGuidedVolumeExitHandoff();
        RefreshGuidedIntentState();

        _frameRequest.SetTransientState(
             origin: Position,
             footPosition: GetFootPosition(),
             rotation: Rotation,
             direction: IsGuideded ? Steering.GetHeading(this) : null
        );

        if (TryGetTurnDirection(_frameRequest, out Vector3d turnDirection))
            Turning.RequestTurnDirection(Forward, turnDirection);

        if (Motor.TryTraversal(_frameRequest, out Vector3d vDelta, out Vector3d pDelta, out FixedQuaternion rDelta))
        {
            AddVelocityDelta(vDelta);
            AddPositionDelta(pDelta);
            ApplyRotationDelta(rDelta);
        }

        if (Turning.TrySimulateTurn(Position, LastPosition, Forward, Rotation, out FixedQuaternion appliedRotation))
            Rotation = appliedRotation;

        if (_animationHandler is null) return;

        NavAnimationUpdater.UpdateAnimationParameters(
            _animationHandler,
            _frameRequest.Direction,
            _frameRequest.Rotation,
            IsLockedOn,
            _frameRequest.Rate == TrekRate.Fast,
            AnimDampTime
        );
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

        LastPosition = Position;
        Position += _positionDelta + _velocityDelta;

        CheckVoxelOccupancy();

        if (_rotationDelta != FixedQuaternion.Identity)
        {
            Rotation *= _rotationDelta;
            _rotationDelta = FixedQuaternion.Identity;
        }

        if (Rotation != FixedQuaternion.Identity)
            Forward = Rotation.Rotate(Vector3d.Forward);
        else
            Forward = Vector3d.Forward;

        CheckTrekCondition();

        Vector3d previousVelocity = Velocity;
        Fixed64 invDelta = TrailblazerManager.InvDeltaTime;
        Velocity = (Position - LastPosition) * invDelta;
        Speed = Velocity != Vector3d.Zero ? Velocity.Magnitude : Fixed64.Zero;
        Acceleration = (Velocity - previousVelocity) * invDelta;

        if (Steering.ShouldMove && Acceleration != Vector3d.Zero)
            StuckThresholdSpeed = (Acceleration / TrailblazerManager.FrameRate).Magnitude;
        else
            StuckThresholdSpeed = Fixed64.Zero;

        _positionDelta = Vector3d.Zero;
        _velocityDelta = Vector3d.Zero;

        Motor.FinalizeTraversal(Position, LastPosition, Rotation, _frameCondition, newFootPosition: GetFootPosition());

        // If the navigator is currently following a guided path, 
        // reset only the transient request state to preserve path-following values.
        if (IsGuideded)
            _frameRequest.ResetTransient();
        else
            _frameRequest.Reset();
    }

    /// <summary>
    /// Notifies the navigator that a collision occurred so collision-driven subsystem responses can run on the next simulation step.
    /// </summary>
    public virtual void NotifyCollision()
    {
        if (!IsActive) return;

        Turning.NotifyCollision();
    }

    #endregion

    #region Traversal Condition Management

    /// <summary>
    /// Updates the navigator to a grounded state using a sampled surface snapshot.
    /// </summary>
    public virtual void SetGroundContact(
        Fixed64 surfaceLevel,
        GroundCondition surfaceCondition,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        ApplyTrekCondition(
            medium: TraversalMedium.Solid,
            surfaceLevel: surfaceLevel,
            surfaceCondition: surfaceCondition,
            replaceSurfaceCondition: true,
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Updates the navigator to a grounded state using a host-provided platform snapshot plus surface settings.
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
        SetGroundContact(
            surfaceLevel,
            new GroundCondition()
            {
                Platform = platform,
                SurfaceFriction = surfaceFriction ?? Fixed64.Zero,
                MotionTransferState = motionTransfer
            },
            ceilingLevel,
            updateMotorState);
    }

    /// <summary>
    /// Updates the navigator to an airborne state while preserving the last known ground condition unless an override is provided.
    /// </summary>
    public virtual void SetAirborne(
        Fixed64? surfaceLevel = null,
        GroundCondition? launchCondition = null,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        ApplyTrekCondition(
            medium: TraversalMedium.Gas,
            surfaceLevel: surfaceLevel,
            surfaceCondition: launchCondition,
            replaceSurfaceCondition: launchCondition.HasValue,
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Updates the navigator to a water-contact state and clears any grounded platform contact.
    /// </summary>
    public virtual void SetWaterContact(
        Fixed64 surfaceLevel,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        ApplyTrekCondition(
            medium: TraversalMedium.Liquid,
            surfaceLevel: surfaceLevel,
            surfaceCondition: null,
            replaceSurfaceCondition: true,
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
    /// <param name="ceilingLevel">The vertical ceiling level, if applicable.</param>
    /// <param name="updateMotorState">Flags whether or not to update the motor's internal surface state.  Otherwise, it should be updated at the end of the frame.</param>
    public virtual void SetTrekCondition(
        TraversalMedium? medium = null,
        Fixed64? surfaceLevel = null,
        GroundCondition? surfaceCondition = null,
        Fixed64? ceilingLevel = null,
        bool updateMotorState = false)
    {
        ApplyTrekCondition(
            medium: medium,
            surfaceLevel: surfaceLevel,
            surfaceCondition: surfaceCondition,
            replaceSurfaceCondition: surfaceCondition.HasValue,
            ceilingLevel: ceilingLevel,
            updateMotorState: updateMotorState);
    }

    /// <summary>
    /// Pushes the current traversal snapshot into the motor before the next traversal phase begins.
    /// </summary>
    public virtual void SyncCurrentTrekConditionToMotor()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before syncing traversal state to the motor.");

        Motor.SyncTraversalState(_frameCondition);
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

    private void ApplyTrekCondition(
        TraversalMedium? medium,
        Fixed64? surfaceLevel,
        GroundCondition? surfaceCondition,
        bool replaceSurfaceCondition,
        Fixed64? ceilingLevel,
        bool updateMotorState)
    {
        if (!IsActive)
            return;

        _frameCondition.Medium = medium ?? _frameCondition.Medium;
        _frameCondition.SurfaceLevel = surfaceLevel ?? _frameCondition.SurfaceLevel;
        if (replaceSurfaceCondition)
            _frameCondition.GroundState = surfaceCondition;
        _frameCondition.CeilingLevel = ceilingLevel ?? _frameCondition.CeilingLevel;

        if (updateMotorState)
            SyncCurrentTrekConditionToMotor();
    }

    #endregion

    #region Deltas - Position / Velocity / Rotation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddPositionDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        _positionDelta += delta;
        // shift last position so it doesn't alter navigator's velocity
        LastPosition += delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ApplyRotationDelta(FixedQuaternion delta)
    {
        if (delta == FixedQuaternion.Identity) return;

        _rotationDelta *= delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddVelocityDelta(Vector3d delta)
    {
        if (delta == Vector3d.Zero) return;

        // assume a mass of 1...for now
        _velocityDelta += delta;
    }

    #endregion

    #region Utilities

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector3d? GetFootPosition()
    {
        return Position + Vector3d.Down * FootPositionAdjust;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual Guid GenerateGUID() => NavigatorGlobalIdAllocator.Create();

    private void RefreshGuidedIntentState()
    {
        if (!IsGuideded)
            return;

        if (Steering?.CurrentRequest == null
            && _pendingGuidedVolumeExitHandoff == null)
        {
            _guidedClimbIntent = false;
            _frameRequest.IsRequestingClimb = false;
            return;
        }

        _frameRequest.IsRequestingClimb = _guidedClimbIntent;
    }

    private void TryActivatePendingGuidedVolumeExitHandoff()
    {
        if (!IsGuideded
            || _pendingGuidedVolumeExitHandoff == null
            || Steering == null
            || Steering.ShouldMove
            || Steering.CurrentRequest != null)
        {
            return;
        }

        if (!_pendingGuidedVolumeExitHandoff.TryCreateFollowupRequest(Position, Size, out IPathRequest followupRequest))
        {
            return;
        }

        GuidedVolumeExitHandoff handoff = _pendingGuidedVolumeExitHandoff;
        _pendingGuidedVolumeExitHandoff = null;

        Steering.ApplyPathRequest(followupRequest, handoff.MovementGroupId);
        _frameRequest.IsRequestingFlight = false;
        _guidedClimbIntent = handoff.IsRequestingClimb;
        _frameRequest.IsRequestingClimb = _guidedClimbIntent;
    }

    private static bool ResolveGuidedClimbIntent(
        IPathRequest pathRequest,
        GuidedVolumeExitHandoff handoff)
    {
        if (handoff != null
            && TraversalTransitionRegistry.TryGet(handoff.TransitionId, out TraversalTransition transition))
        {
            return transition.RequestsClimbIntent;
        }

        return pathRequest switch
        {
            AStarPathRequest aStar => RequestsClimbIntent(HybridPathRequest.CreateFromAStar(aStar)),
            FlowFieldPathRequest flowField => RequestsClimbIntent(HybridPathRequest.CreateFromFlowField(flowField)),
            HybridPathRequest hybrid => RequestsClimbIntent(hybrid),
            _ => false
        };
    }

    private static bool RequestsClimbIntent(HybridPathRequest request)
    {
        TraversalTransition[] directedTransitions = request?.RoutePlan?.DirectedTransitions;
        if (directedTransitions == null)
            return false;

        for (int i = 0; i < directedTransitions.Length; i++)
        {
            if (directedTransitions[i].RequestsClimbIntent)
                return true;
        }

        return false;
    }

    #endregion

    #region Occupancy Mangement

    protected virtual void CheckVoxelOccupancy(bool init = false)
    {
        if (!init && Position == LastPosition) return;

        bool voxelFound = GlobalGridManager.TryGetGridAndVoxel(
            Position,
            out VoxelGrid curGrid,
            out Voxel curVoxel);
        if (!voxelFound) return;

        if (!curGrid.TryAddVoxelOccupant(curVoxel, this))
        {
            GridForgeLogger.Warn($"Navigator {GlobalId} failed to register occupancy in voxel {curVoxel.Index} of grid {curGrid} at position {Position}.");
            return;
        }

        bool lastVoxelFound = GlobalGridManager.TryGetGridAndVoxel(
            LastPosition,
            out VoxelGrid lastGrid,
            out Voxel lastVoxel);

        // check if position is still within the same voxel
        if (!lastVoxelFound || curVoxel == lastVoxel)
            return;

        lastGrid.TryRemoveVoxelOccupant(lastVoxel, this);
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        Vector3d position = Position;
        Vector3d lastPosition = LastPosition;
        FixedQuaternion rotation = Rotation;
        Vector3d velocity = Velocity;
        Fixed64 speed = Speed;
        Vector3d acceleration = Acceleration;
        Fixed64 size = Size;
        Fixed64 footPositionAdjust = FootPositionAdjust;
        GuidedPathMode guidedPathMode = GuidedPathMode;
        bool guidedAllowUnwalkableEndpoints = GuidedAllowUnwalkableEndpoints;
        bool guidedAllowTraversalTransitions = GuidedAllowTraversalTransitions;
        Fixed64 guidedMaxClimbHeight = GuidedMaxClimbHeight;
        HeuristicMethod guidedAStarHeuristic = GuidedAStarHeuristic;
        int guidedFlowFieldExtraFloodRange = GuidedFlowFieldExtraFloodRange;
        Guid globalId = GlobalId;
        byte occupantGroupId = OccupantGroupId;
        bool isLockedOn = IsLockedOn;
        Fixed64 animDampTime = AnimDampTime;
        Fixed64 stuckThresholdSpeed = StuckThresholdSpeed;
        bool isGuideded = IsGuideded;
        bool guidedClimbIntent = _guidedClimbIntent;
        TrekCondition frameCondition = _frameCondition;
        TrekRequest frameRequest = _frameRequest;
        GuidedVolumeExitHandoff pendingGuidedVolumeExitHandoff = _pendingGuidedVolumeExitHandoff;
        if (chronicler.Mode == SerializationMode.Loading && pendingGuidedVolumeExitHandoff == null)
            pendingGuidedVolumeExitHandoff = new GuidedVolumeExitHandoff();
        NavSteering steering = Steering;
        NavTurning turning = Turning;
        NavMotor motor = Motor;

        RecordValues.Look(chronicler, ref position, "position", Vector3d.Zero);
        RecordValues.Look(chronicler, ref lastPosition, "lastPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref rotation, "rotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref velocity, "velocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref speed, "speed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref acceleration, "acceleration", Vector3d.Zero);
        RecordValues.Look(chronicler, ref size, "size", Fixed64.One);
        RecordValues.Look(chronicler, ref footPositionAdjust, "footPositionAdjust", DefaultFootPositionAdjust);
        RecordValues.Look(chronicler, ref guidedPathMode, "guidedPathMode", GuidedPathMode.AStar);
        RecordValues.Look(chronicler, ref guidedAllowUnwalkableEndpoints, "guidedAllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref guidedAllowTraversalTransitions, "guidedAllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref guidedMaxClimbHeight, "guidedMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref guidedAStarHeuristic, "guidedAStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref guidedFlowFieldExtraFloodRange, "guidedFlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref globalId, "globalId", Guid.Empty);
        RecordValues.Look(chronicler, ref occupantGroupId, "occupantGroupId", (byte)1);
        RecordValues.Look(chronicler, ref isLockedOn, "isLockedOn", false);
        RecordValues.Look(chronicler, ref animDampTime, "animDampTime", (Fixed64)0.1f);
        RecordValues.Look(chronicler, ref stuckThresholdSpeed, "stuckThresholdSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref isGuideded, "isGuideded", false);
        RecordValues.Look(chronicler, ref guidedClimbIntent, "guidedClimbIntent", false);
        RecordDeepStruct.Look(chronicler, ref frameCondition, "frameCondition");
        RecordDeepStruct.Look(chronicler, ref frameRequest, "frameRequest");
        RecordDeep.Look(chronicler, ref pendingGuidedVolumeExitHandoff, "pendingGuidedVolumeExitHandoff");
        RecordDeep.Look(chronicler, ref steering, "steering");
        RecordDeep.Look(chronicler, ref turning, "turning");
        RecordDeep.Look(chronicler, ref motor, "motor");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            Position = position;
            LastPosition = lastPosition;
            Rotation = rotation;
            Velocity = velocity;
            Speed = speed;
            Acceleration = acceleration;
            Size = size;
            FootPositionAdjust = footPositionAdjust;
            GuidedPathMode = guidedPathMode;
            GuidedAllowUnwalkableEndpoints = guidedAllowUnwalkableEndpoints;
            GuidedAllowTraversalTransitions = guidedAllowTraversalTransitions;
            GuidedMaxClimbHeight = guidedMaxClimbHeight;
            GuidedAStarHeuristic = guidedAStarHeuristic;
            GuidedFlowFieldExtraFloodRange = guidedFlowFieldExtraFloodRange;
            GlobalId = globalId;
            OccupantGroupId = occupantGroupId;
            IsLockedOn = isLockedOn;
            AnimDampTime = animDampTime;
            StuckThresholdSpeed = stuckThresholdSpeed;
            IsGuideded = isGuideded;
            _guidedClimbIntent = guidedClimbIntent;
            _frameCondition = frameCondition.Clone();
            _frameRequest = frameRequest.Clone();
            _pendingGuidedVolumeExitHandoff = pendingGuidedVolumeExitHandoff?.IsValid == true
                ? pendingGuidedVolumeExitHandoff
                : null;
            Steering = steering;
            Turning = turning;
            Motor = motor;

            Forward = Rotation != FixedQuaternion.Identity
                ? Rotation.Rotate(Vector3d.Forward)
                : Vector3d.Forward;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;
            _rotationDelta = FixedQuaternion.Identity;
            _isSet = true;
            _isInitialized = Motor != null;

            Steering?.UpdateOwnerRadius(Radius);

            Turning?.OnInitialize(Radius);

            CheckVoxelOccupancy(true);
        }
    }

    #endregion
}
