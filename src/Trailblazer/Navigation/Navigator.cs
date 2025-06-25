using System;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
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

        /// <summary>
        /// Default braking factor applied when decelerating or stopping motion.
        /// </summary>
        public static readonly Fixed64 DefaultBrakingPower = (Fixed64)0.15d;

        #endregion

        #region State - Position / Rotation / Velocity

        /// <inheritdoc cref="INavigate.Position"/>
        public Vector3d Position { get; protected set; }

        /// <summary>
        /// The change in position to apply during the current simulation frame.
        /// </summary>
        protected Vector3d _positionDelta;

        /// <inheritdoc cref="INavigate.Rotation"/>
        public FixedQuaternion Rotation { get; protected set; } = FixedQuaternion.Identity;

        /// <summary>
        /// The change in rotation to apply during the current simulation frame.
        /// </summary>
        protected FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

        /// <inheritdoc cref="INavigate.Velocity"/>
        public Vector3d Velocity { get; protected set; }

        /// <summary>
        /// The velocity change computed during the simulation frame.
        /// </summary>
        protected Vector3d _velocityDelta;

        /// <inheritdoc cref="INavigate.Speed"/>
        public Fixed64 Speed { get; protected set; }

        /// <summary>
        /// The current acceleration vector of the navigator, updated each frame based on velocity change.
        /// </summary>
        public Vector3d Acceleration { get; protected set; }

        #endregion

        #region State - Traversal / Steering

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition SurfaceState { get; protected set; }

        /// <summary>
        /// The controller responsible for managing the navigator's desired movement direction.
        /// </summary>
        public NavSteering Steering { get; protected set; }

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
        private TraversalRequest _currentFrameRequest;

        #endregion

        #region Settings

        /// <summary>
        /// Friction-based deceleration rate used when slowing down on ground surfaces.
        /// </summary>
        public Fixed64 BrakingPower { get; set; } = DefaultBrakingPower;

        /// <inheritdoc cref="INavigate.UnitSize"/>
        public Fixed64 UnitSize { get; set; } = Fixed64.One;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust { get; set; } = DefaultFootPositionAdjust;

        /// <summary>
        /// Determines whether the navigator can rotate toward a movement direction when starting traversal.
        /// </summary>
        public bool CanTurn = true;

        /// <summary>
        /// Event callback triggered when a new traversal direction requires a turn to align the facing rotation.
        /// </summary>
        public Action<Vector3d> OnStartTurn;

        #endregion

        #region Computed Properties

        /// <inheritdoc cref="INavigate.UnitRadius"/>
        public Fixed64 UnitRadius => UnitSize * Fixed64.Half;

        /// <inheritdoc cref="INavigate.IsAvoidingLeft"/>
        public bool IsAvoidingLeft { get; set; }

        #endregion

        #region Setup / Initialization

        /// <summary>
        /// Sets the initial configuration of the navigator, including position, rotation, velocity, and size.
        /// </summary>
        /// <param name="startingPosition">Initial world-space position.</param>
        /// <param name="startingRotation">Optional starting rotation.</param>
        /// <param name="initialVelocity">Optional initial velocity.</param>
        /// <param name="gridSize">Optional grid size (defaults to 1).</param>
        public virtual void Setup(
            Vector3d startingPosition,
            FixedQuaternion? startingRotation = null,
            Vector3d? initialVelocity = null,
            Fixed64? gridSize = null)
        {
            Position = startingPosition;
            Rotation = startingRotation ?? FixedQuaternion.Identity;
            Velocity = initialVelocity ?? Vector3d.Zero;
            UnitSize = gridSize ?? Fixed64.One;
        }

        /// <summary>
        /// Initializes the navigator by setting up its defaults, events, traversal state, and movement controller.
        /// </summary>
        public virtual void Initialize(TraversalCondition surfaceState)
        {
            SurfaceState = surfaceState;

            Steering = NavSteering.CreateNew(this);

            Steering.Events.OnStartTraversal += HandlePathStart;

            Motor = NavMotor.CreateNew(this, SurfaceState);
            Motor.SetVelocity(Velocity);

            Motor.Events.OnAddPositionDelta += AddPositionDelta;
            Motor.Events.OnAddRotationDelta += AddRotationDelta;
            Motor.Events.OnAddVelocityDelta += AddVelocityDelta;
        }

        /// <summary>
        /// Replaces the current traversal state with the given one.
        /// </summary>
        /// <param name="state">The new traversal condition to apply.</param>
        public virtual void ReplaceTraversalState(TraversalCondition state) => SurfaceState = state;

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
            _currentFrameRequest = new TraversalRequest()
            {
                Direction = direction ?? Vector3d.Zero,
                Rate = rate ?? TrekRate.Stationary,
                IsRequestingJump = isRequestingJump ?? false
            };

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
            _currentFrameRequest = new TraversalRequest()
            {
                Rate = rate ?? TrekRate.Stationary,
                IsRequestingJump = isRequestingJump ?? false
            };

            IsManuallyControlled = false;

            if (!pathRequest.IsValid)
                pathRequest.TryPrepare(Position, destination, UnitSize);

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
            if (IsManuallyControlled)
            {
                _currentFrameRequest.CurrentPosition = Position;
                _currentFrameRequest.CurrentRotation = Rotation;
                StartTraversal(_currentFrameRequest);
                return;
            }

            Steering.OnSimulate(this);
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
            Vector3d lastPosition = Position;
            Position += _positionDelta + _velocityDelta;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                Rotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            CheckTraversalCondition();

            Vector3d previousVelocity = Velocity;
            Velocity = (Position - lastPosition) / TrailblazerManager.DeltaTime;
            Speed = Velocity != Vector3d.Zero ? Velocity.Magnitude : Fixed64.Zero;
            Acceleration = (Velocity - previousVelocity) / TrailblazerManager.DeltaTime;

            if (Steering.ShouldMove && Acceleration != Vector3d.Zero)
                StuckThresholdSpeed = (Acceleration / TrailblazerManager.FrameRate).Magnitude;
            else
                StuckThresholdSpeed = Fixed64.Zero;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;

            Motor.FinalizeTraversal(this, SurfaceState);

            // Reset travel request for next frame
            _currentFrameRequest = TraversalRequest.Empty;
        }

        /// <summary>
        /// Begins a new traversal session by forwarding the request to the motion controller.
        /// </summary>
        /// <param name="request">The traversal request to initiate.</param>
        protected virtual void StartTraversal(TraversalRequest request)
        {
            if (CanTurn)
                //TODO: integrate this...only when the angle to request.Direction is above a threshold (e.g., > 10 degrees).
                OnStartTurn?.Invoke(request.Direction);

            Motor.Traverse(request);
        }

        /// <summary>
        /// Called when a new guided path traversal begins, typically after a TrailGuide returns a direction to follow.
        /// </summary>
        /// <param name="direction">The direction vector produced by the pathfinding logic.</param>
        protected virtual void HandlePathStart(Vector3d direction)
        {
            _currentFrameRequest.CurrentPosition = Position;
            _currentFrameRequest.CurrentRotation = Rotation;

            if (direction != Vector3d.Zero && !Steering.HasTrailGuide)
            {
                // Scaling direction before passing to the motor lets us modulate movement before acceleration is applied
                Fixed64 deceleration = Acceleration != Vector3d.Zero ? Acceleration.Magnitude : BrakingPower;
                Fixed64 slowDistance = Speed / deceleration;
                if (Steering.DistanceToTarget > Fixed64.Epsilon && Steering.DistanceToTarget <= slowDistance)
                {
                    Fixed64 closingSpeed = Steering.DistanceToTarget / slowDistance;
                    direction *= closingSpeed; // reduce magnitude = slow down
                }
            }

            _currentFrameRequest.Direction = direction;
            StartTraversal(_currentFrameRequest);
        }

        #endregion

        #region Traversal Condition Management

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <remarks>
        /// Make sure to update this before the next <see cref="CommitFrameMotion"/> so <see cref="NavMotor.FinalizeTraversal(INavigate, TraversalCondition)"/> can update it's state.
        /// If intent is to update before next <see cref="Simulate"/>, ensure that <see cref="NavMotor.UpdateTraversal(TraversalCondition, bool)"/> is called to update state.
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
            SurfaceState.Medium = medium ?? SurfaceState.Medium;
            SurfaceState.SurfaceLevel = surfaceLevel ?? SurfaceState.SurfaceLevel;
            SurfaceState.GroundState = surfaceCondition ?? SurfaceState.GroundState;
            SurfaceState.CeilingLevel = ceilingLevel ?? SurfaceState.CeilingLevel;

            if (updateMotorState)
                Motor.UpdateTraversal(SurfaceState);
        }

        /// <summary>
        /// Performs a grounded surface check to determine the current traversal condition.
        /// Implementations should update the surface state based on collision or probe logic.
        /// </summary>
        public abstract void CheckTraversalCondition();

        #endregion

        #region Deltas - Position / Velocity / Rotation

        /// <summary>
        /// Adds the given delta to the current frame’s position offset.
        /// </summary>
        /// <param name="positionDelta">The offset to apply to position this frame.</param>
        protected virtual void AddPositionDelta(Vector3d positionDelta)
        {
            _positionDelta += positionDelta;
        }

        /// <summary>
        /// Adds the given delta to the current frame’s rotation offset.
        /// </summary>
        /// <param name="rotationDelta">The offset to apply to rotation this frame.</param>
        protected virtual void AddRotationDelta(FixedQuaternion rotationDelta)
        {
            _rotationDelta *= rotationDelta;
        }

        /// <summary>
        /// Adds the given delta to the current frame’s velocity offset.
        /// </summary>
        /// <param name="velocityDelta">The offset to apply to velocity this frame.</param>
        protected virtual void AddVelocityDelta(Vector3d velocityDelta)
        {
            // assume a mass of 1...for now
            _velocityDelta += velocityDelta;
        }

        #endregion

        #region Utilities

        /// <inheritdoc cref="INavigate.GetFootPosition"/>
        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * FootPositionAdjust;
        }

        #endregion
    }
}