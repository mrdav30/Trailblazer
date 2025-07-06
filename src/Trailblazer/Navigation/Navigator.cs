using System;
using System.Linq;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation
{
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

        public TraversalCondition TraversalState { get; protected set; }

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
        public bool IsManuallyControlled { get; protected set; }

        /// <summary>
        /// The traversal request for the current frame, containing directional intent and travel mode.
        /// </summary>
        private TraversalRequest _currentFrameRequest = new();

        #endregion

        #region Settings

        public Fixed64 Size { get; set; } = Fixed64.One;

        public Fixed64 Radius => Size * Fixed64.Half;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust { get; set; } = DefaultFootPositionAdjust;

        #endregion

        #region Voxel Occupancy

        public Guid GlobalId { get; protected set; }

        public byte OccupantGroupId { get; set; } = 1;

        public SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; protected set; } = new();

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
        public virtual void Initialize(TraversalCondition condition)
        {
            TraversalState = condition;

            Steering = NavSteering.CreateNew(Radius);

            Motor = NavMotor.CreateNew(Position, TraversalState);
            Motor.SetVelocity(Velocity);

            Turning = NavTurning.CreateNew(Radius);

            CheckVoxelOccupancy(true);

            _isInitialized = true;
        }

        /// <summary>
        /// Replaces the current traversal state with the given one.
        /// </summary>
        /// <param name="state">The new traversal condition to apply.</param>
        public virtual void ReplaceTraversalState(TraversalCondition state) => TraversalState = state;

        public virtual void Reset()
        {
            TraversalState = TraversalCondition.Empty;
            _currentFrameRequest = TraversalRequest.Empty;

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
        public virtual void ApplyInputTravelRequest(
            Vector3d? direction = null,
            TrekRate? rate = null,
            bool? isRequestingJump = null)
        {
            if (!IsActive) return;

            _currentFrameRequest.Direction = direction ?? Vector3d.Zero;
            _currentFrameRequest.Rate = rate ?? TrekRate.Stationary;
            _currentFrameRequest.IsRequestingJump = isRequestingJump ?? false;

            IsManuallyControlled = true;
        }

        /// <summary>
        /// Constructs and applies a guided traversal request toward a destination using a pathfinding paradigm.
        /// </summary>
        /// <param name="destination">The target destination.</param>
        /// <param name="pathRequest">The configuration for the type of path to request (e.g., A*, FlowField).</param>
        /// <param name="rate">Desired movement rate (walk, run, etc.).</param>
        /// <param name="isRequestingJump">Whether the navigator intends to jump during traversal.</param>
        /// <param name="allowUnwalkable">Whether the navigator can traverse to an unwalkable voxel.</param>
        public virtual void ApplyGuidedTravelRequest(
            IPathRequest pathRequest,
            Vector3d destination,
            TrekRate? rate = null,
            bool? isRequestingJump = null,
            bool? allowUnwalkable = null)
        {
            if (!IsActive) return;

            _currentFrameRequest.Direction = Vector3d.Zero;
            _currentFrameRequest.Rate = rate ?? TrekRate.Stationary;
            _currentFrameRequest.IsRequestingJump = isRequestingJump ?? false;

            IsManuallyControlled = false;

            if (!pathRequest.IsValid)
                pathRequest.TryPrepare(Position, destination, Size);

            Steering.ApplyPathRequest(pathRequest, destination);
        }

        /// <summary>
        /// Called to make the agent jump if allowed and in a valid state.
        /// </summary>
        public virtual void ToggleJumpStatus(bool status) => _currentFrameRequest.IsRequestingJump = status;

        /// <summary>
        /// Changes the speed at which the navigator is currently traveling without altering direction.
        /// </summary>
        /// <param name="rate">New traversal rate to apply (walk, run, etc.).</param>
        public virtual void SetTraversalSpeed(TrekRate rate) => _currentFrameRequest.Rate = rate;

        #endregion

        #region Simulation Lifecycle

        /// <summary>
        /// Runs simulation logic for this navigator (input handling, steering, etc.).
        /// </summary>
        public virtual void Simulate()
        {
            if (!IsActive)
                throw new InvalidOperationException("Navigator must be Setup and Initialized before Simulate().");

            _currentFrameRequest.Origin = Position;
            _currentFrameRequest.Rotation = Rotation;

            if (!IsManuallyControlled)
                _currentFrameRequest.Direction = Steering.GetHeading(this);

            StartTraversal(_currentFrameRequest);
            Turning.SimulateTurn(this);
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

            CheckTraversalCondition();

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

            Motor.FinalizeTraversal(this);

            // Reset travel request for next frame
            _currentFrameRequest = TraversalRequest.Empty;
        }

        /// <summary>
        /// Begins a new traversal session by forwarding the request to the motion controller.
        /// </summary>
        /// <param name="request">The traversal request to initiate.</param>
        protected virtual void StartTraversal(TraversalRequest request)
        {
            Turning.RequestTurnDirection(Forward, request.Direction);
            Motor.Traverse(this, request);
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
        public virtual void SetTraversalCondition(
            TraversalMedium? medium = null,
            Fixed64? surfaceLevel = null,
            GroundCondition? surfaceCondition = null,
            Fixed64? ceilingLevel = null,
            bool updateMotorState = false)
        {
            if (!IsActive) return;

            TraversalState.Medium = medium ?? TraversalState.Medium;
            TraversalState.SurfaceLevel = surfaceLevel ?? TraversalState.SurfaceLevel;
            TraversalState.GroundState = surfaceCondition ?? TraversalState.GroundState;
            TraversalState.CeilingLevel = ceilingLevel ?? TraversalState.CeilingLevel;

            if (updateMotorState)
                Motor.UpdateTraversal(TraversalState);
        }

        /// <summary>
        /// Performs a grounded surface check to determine the current traversal condition.
        /// Implementations should update the surface state based on collision or probe logic.
        /// </summary>
        protected abstract void CheckTraversalCondition();
      
        #endregion

        #region Deltas - Position / Velocity / Rotation

        /// <summary>
        /// Adds the given delta to the current frame’s position offset.
        /// </summary>
        /// <param name="positionDelta">The offset to apply to position this frame.</param>
        public virtual void AddPositionDelta(Vector3d positionDelta)
        {
            _positionDelta += positionDelta;
        }

        /// <summary>
        /// Adds the given delta to the current frame’s rotation offset.
        /// </summary>
        /// <param name="rotationDelta">The offset to apply to rotation this frame.</param>
        public virtual void AddRotationDelta(FixedQuaternion rotationDelta)
        {
            _rotationDelta *= rotationDelta;
        }

        /// <summary>
        /// Adds the given delta to the current frame’s velocity offset.
        /// </summary>
        /// <param name="velocityDelta">The offset to apply to velocity this frame.</param>
        public virtual void AddVelocityDelta(Vector3d velocityDelta)
        {
            // assume a mass of 1...for now
            _velocityDelta += velocityDelta;
        }

        public virtual void ApplyRotation(FixedQuaternion rotation) => Rotation = rotation;

        #endregion

        #region Utilities

        /// <inheritdoc cref="IMotor.GetFootPosition"/>
        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * FootPositionAdjust;
        }

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
}