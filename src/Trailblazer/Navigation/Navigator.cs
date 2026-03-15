using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.Animation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Base class representing a navigator, responsible for handling movement, traversal state, and simulation flow.
/// </summary>
/// <remarks>
/// This class acts as a bridge between the simulation logic (`ScoutController`) and the entity's external representation.  
/// It defines common traversal behaviors and lifecycle methods that can be extended by concrete implementations.
/// </remarks>
[Serializable]
public abstract class Navigator : INavigate
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
    public bool IsGuideded => FrameRequest?.TargetPosition != null;

    public TrekCondition FrameCondition { get; protected set; } = new();

    public TrekRequest FrameRequest { get; protected set; } = new();

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
    public bool GuidedAllowUnwalkable { get; set; }

    /// <summary>
    /// Default heuristic used when the navigator builds A* requests.
    /// </summary>
    public HeuristicMethod GuidedAStarHeuristic { get; set; } = HeuristicMethod.Manhattan;

    /// <summary>
    /// Default max climb height used when the navigator builds A* requests.
    /// </summary>
    public Fixed64 GuidedAStarMaxClimbHeight { get; set; } = Fixed64.One;

    /// <summary>
    /// Default extra flood range used when the navigator builds flow-field requests.
    /// </summary>
    public int GuidedFlowFieldExtraFloodRange { get; set; } = FlowFieldPathRequest.DefaultExtraFloodRange;

    #endregion

    #region Voxel Occupancy

    public Guid GlobalId { get; protected set; }

    public byte OccupantGroupId { get; set; } = 1;

    public SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; protected set; } = new();

    #endregion

    #region Animation

    public INavAnimationHandler AnimationHandler { get; set; }

    public bool IsLockedOn { get; set; }

    public Fixed64 AnimDampTime = (Fixed64)0.1f;

    #endregion

    #region Setup / Initialization

    /// <summary>
    /// Sets the initial configuration of the navigator, including position, rotation, velocity, and size.
    /// </summary>
    /// <param name="position">Initial world-space position.</param>
    /// <param name="rotation">Optional starting rotation.</param>
    /// <param name="velocity">Optional initial velocity.</param>
    /// <param name="size">Optional grid size (defaults to 1).</param>
    public virtual void Setup(
        Vector3d position,
        FixedQuaternion? rotation = null,
        Vector3d? velocity = null,
        Fixed64? size = null)
    {
        GlobalId = GenerateGUID();

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
        FrameCondition = condition.Clone();

        Steering = NavSteering.CreateNew(Radius);

        Motor = NavMotor.CreateNew(FrameCondition);
        Motor.SetVelocity(Velocity);

        Turning = NavTurning.CreateNew(Radius);

        CheckVoxelOccupancy(true);

        _isInitialized = true;
    }

    /// <summary>
    /// Replaces the current traversal state with the given one.
    /// </summary>
    /// <param name="state">The new traversal condition to apply.</param>
    public virtual void ReplaceTrekCondition(TrekCondition state) => FrameCondition = state.Clone();

    public virtual void Reset()
    {
        FrameCondition.Reset();
        FrameRequest.Reset();

        // store copy since this will mutate the collection
        foreach (var idx in OccupyingIndexMap.Keys.ToArray())
        {
            if (!GlobalGridManager.TryGetGrid(idx.GridIndex, out VoxelGrid grid))
                continue;

            grid.TryRemoveVoxelOccupant(idx.VoxelIndex, this);
        }

        OccupyingIndexMap.Clear();

        _isSet = false;
        _isInitialized = false;
    }

    #endregion

    #region Input / Travel Requests

    /// <summary>
    /// Constructs and applies a traversal request using high-level navigation input values.
    /// </summary>
    /// <param name="direction">Desired direction of travel.</param>
    /// <param name="rate">Rate of travel (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the agent is requesting a jump action.</param>
    public virtual void ApplyInputTrekRequest(
        Vector3d? direction = null,
        TrekRate? rate = null,
        bool? isRequestingJump = null)
    {
        if (!IsActive) return;

        FrameRequest.TargetPosition = null;  // clear any existing target position since this is a direct input request
        FrameRequest.Direction = direction ?? Vector3d.Zero;
        FrameRequest.Rate = rate ?? TrekRate.Stationary;
        FrameRequest.IsRequestingJump = isRequestingJump ?? false;
    }

    /// <summary>
    /// Constructs and applies a guided traversal request toward a destination using navigator-owned path request defaults.
    /// </summary>
    /// <param name="targetPosition">The desired world-space target position.</param>
    /// <param name="pathMode">Optional override for the built-in path request mode. When omitted, <see cref="GuidedPathMode"/> is used.</param>
    /// <param name="rate">Desired movement rate (walk, run, etc.).</param>
    /// <param name="isRequestingJump">Whether the navigator intends to jump during traversal.</param>
    /// <param name="groupId">Optional shared group identifier used to preserve formation offsets between navigators.</param>
    public virtual void ApplyGuidedTrekRequest(
        Vector3d targetPosition,
        GuidedPathMode? pathMode = null,
        TrekRate? rate = null,
        bool? isRequestingJump = null,
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

        FrameRequest.TargetPosition = targetPosition;
        FrameRequest.Direction = Vector3d.Zero;
        FrameRequest.Rate = rate ?? TrekRate.Stationary;
        FrameRequest.IsRequestingJump = isRequestingJump ?? false;

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
        return NavigatorPathRequestFactory.TryCreate(
            origin: Position,
            targetPosition: targetPosition,
            unitSize: Size,
            pathMode: pathMode,
            allowUnwalkable: GuidedAllowUnwalkable,
            aStarHeuristic: GuidedAStarHeuristic,
            aStarMaxClimbHeight: GuidedAStarMaxClimbHeight,
            flowFieldExtraFloodRange: GuidedFlowFieldExtraFloodRange,
            out pathRequest);
    }

    /// <summary>
    /// Called to make the agent jump if allowed and in a valid state.
    /// </summary>
    public virtual void ToggleJumpStatus(bool status) => FrameRequest.IsRequestingJump = status;

    /// <summary>
    /// Changes the speed at which the navigator is currently traveling without altering direction.
    /// </summary>
    /// <param name="rate">New traversal rate to apply (walk, run, etc.).</param>
    public virtual void SetTraversalSpeed(TrekRate rate) => FrameRequest.Rate = rate;

    #endregion

    #region Simulation Lifecycle

    /// <summary>
    /// Runs simulation logic for this navigator (input handling, steering, etc.).
    /// </summary>
    public virtual void Simulate()
    {
        if (!IsActive)
            throw new InvalidOperationException("Navigator must be Setup and Initialized before Simulate().");

        FrameRequest.Origin = Position;
        FrameRequest.FootPosition = GetFootPosition();
        FrameRequest.Rotation = Rotation;

        if (IsGuideded)
            FrameRequest.Direction = Steering.GetHeading(this);

        Turning.RequestTurnDirection(Forward, FrameRequest.Direction);
        ConsumeMotorOutput(Motor.Traverse(FrameRequest));
        Turning.SimulateTurn(this);

        if (AnimationHandler is null) return;

        NavAnimationUpdater.UpdateAnimationParameters(
            AnimationHandler,
            FrameRequest.Direction,
            IsLockedOn,
            FrameRequest.Rate == TrekRate.Fast,
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

        Motor.FinalizeTraversal(Position, LastPosition, Rotation, FrameCondition, newFootPosition: GetFootPosition());

        // Reset travel request for next frame
        FrameRequest.Reset();
    }

    #endregion

    #region Traversal Condition Management

    /// <summary>
    /// Updates the scout’s traversal state, including its current medium and surface information.
    /// </summary>
    /// <remarks>
    /// Make sure to update this before the next <see cref="CommitFrameMotion"/> so <see cref="NavMotor.FinalizeTraversal"/> can update it's state.
    /// If intent is to update before next <see cref="Simulate"/>, ensure that <see cref="NavMotor.UpdateTraversal"/> is called to update state.
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
        if (!IsActive) return;

        FrameCondition.Medium = medium ?? FrameCondition.Medium;
        FrameCondition.SurfaceLevel = surfaceLevel ?? FrameCondition.SurfaceLevel;
        FrameCondition.GroundState = surfaceCondition ?? FrameCondition.GroundState;
        FrameCondition.CeilingLevel = ceilingLevel ?? FrameCondition.CeilingLevel;

        if (updateMotorState)
            Motor.UpdateTraversal(FrameCondition);
    }

    public abstract void CheckTrekCondition();

    #endregion

    #region Deltas - Position / Velocity / Rotation

    private void ConsumeMotorOutput(MotorOutput output)
    {
        if (output.VelocityDelta != Vector3d.Zero)
            AddVelocityDelta(output.VelocityDelta);
        if (output.PositionDelta != Vector3d.Zero)
            AddPositionDelta(output.PositionDelta);
        if (output.RotationDelta != FixedQuaternion.Identity)
            AddRotationDelta(output.RotationDelta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddPositionDelta(Vector3d delta)
    {
        _positionDelta += delta;
        // shift last position so it doesn't alter navigator's velocity
        LastPosition += delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddRotationDelta(FixedQuaternion delta)
    {
        _rotationDelta *= delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void AddVelocityDelta(Vector3d delta)
    {
        // assume a mass of 1...for now
        _velocityDelta += delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void ApplyRotation(FixedQuaternion rotation) => Rotation = rotation;

    #endregion

    #region Utilities

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Vector3d GetFootPosition()
    {
        return Position + Vector3d.Down * FootPositionAdjust;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual Guid GenerateGUID() => Guid.NewGuid();

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

        bool wasEmpty = OccupyingIndexMap.Count == 0;
        if (curGrid.TryAddVoxelOccupant(curVoxel, this))
            if (wasEmpty)
                return;  // assume agent has not occupied another voxel

        bool lastVoxelFound = GlobalGridManager.TryGetGridAndVoxel(
            LastPosition,
            out VoxelGrid lastGrid,
            out Voxel lastVoxel);

        // check if position is still within the same voxel
        if (!lastVoxelFound || curVoxel.SpawnToken == lastVoxel.SpawnToken)
            return;

        lastGrid.TryRemoveVoxelOccupant(lastVoxel, this);
    }

    public virtual void SetOccupancy(GlobalVoxelIndex index, int ticket)
    {
        if (!IsActive) return;
        OccupyingIndexMap[index] = ticket;
    }

    public virtual void RemoveOccupancy(GlobalVoxelIndex index)
    {
        if (!IsActive) return;
        OccupyingIndexMap.Remove(index);
    }

    #endregion
}
