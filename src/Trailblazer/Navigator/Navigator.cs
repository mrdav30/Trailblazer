using System;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;

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
    public abstract class Navigator : INavigate, IAvoidanceBody
    {
        public static readonly Fixed64 DefaultFootPositionAdjust = new Fixed64(0.25f);

        /// <inheritdoc cref="INavigate.Position"/>
        public Vector3d Position { get; protected set; }

        protected Vector3d _positionDelta;

        /// <inheritdoc cref="INavigate.Rotation"/>
        public FixedQuaternion Rotation { get; protected set; } = FixedQuaternion.Identity;

        protected FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

        public Fixed64 Speed { get; protected set; }

        protected Vector3d _velocity;

        public Vector3d Velocity { 
            get => _velocity;
            protected set
            {
                _velocity = value;
                Speed = _velocity != Vector3d.Zero ? _velocity.Magnitude : Fixed64.Zero;
            }
        }

        protected Vector3d _velocityDelta;

        public Fixed64 UnitSize { get; set; } = Fixed64.One;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust { get; set; } = DefaultFootPositionAdjust;

        public Fixed64 UnitRadius => UnitSize * Fixed64.Half;

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition SurfaceState { get; protected set; }

        /// <inheritdoc cref="INavigate.Steering"/>
        public NavSteering Steering { get; protected set; }

        public bool IsAvoidingLeft { get; set; }

        /// <inheritdoc cref="INavigate.Motor"/>
        public NavMotor Motor { get; protected set; }

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

            Steering = new NavSteering();
            Steering.OnInitialize(this);

            Steering.Events.OnStartTraversal += StartTraversal;

            Motor = NavMotor.CreateNew(this, SurfaceState);
            Motor.SetVelocity(Velocity);

            Motor.Events.OnAddPositionDelta += (deltaPos) =>
            {
                _positionDelta += deltaPos;
            };
            Motor.Events.OnAddRotationDelta += (rot) =>
            {
                _rotationDelta *= rot;
            };
            Motor.Events.OnAddLinearForce += (force) =>
            {
                // assume a mass of 1...for now
                _velocityDelta += force;
            };
        }

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <remarks>
        /// Make sure to update this before the next <see cref="Visualize"/> so <see cref="NavMotor.FinalizeTraversal(INavigate, TraversalCondition)"/> can update it's state.
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

        public virtual void ReplaceTraversalState(TraversalCondition state) => SurfaceState = state;

        public virtual void SetTravelRequest(
            Vector3d? direction = null,
            Vector3d? destination = null,
            TrekRate? rate = null,
            bool? isRequestingJump = null)
        {
            SetTravelRequest(new TraversalRequest()
            {
                CurrentPosition = Position,
                CurrentRotation = Rotation,
                Direction = direction ?? Vector3d.Zero,
                Destination = destination,
                Rate = rate ?? TrekRate.Stationary,
                IsRequestingJump = isRequestingJump ?? false
            });
        }

        public virtual void SetTravelRequest(TraversalRequest request) {
            Steering.RequestMovement(request);
        }

        public virtual void Simulate()
        {
            Steering.OnSimulate(this);
        }

        protected virtual void StartTraversal(TraversalRequest request)
        {
            Motor.Traverse(request);
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        /// <remarks>
        /// Should be called after physics bodies apply velocity changes
        /// </remarks>
        public virtual void Visualize()
        {
            Vector3d LastPosition = Position;
            Position += _positionDelta + _velocityDelta;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                Rotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            CheckTraversalCondition();

            Velocity = (Position - LastPosition) / TrailblazerManager.DeltaTime;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;

            Motor.FinalizeTraversal(this, SurfaceState);
            Steering.UpdateTimeScaledValues(Motor.Locomotions.Move.Speed, Motor.Locomotions.Move.Acceleration);
        }

        public abstract void CheckTraversalCondition();

        /// <summary>
        /// Returns the world-space position of the navigator’s foot, adjusted for proper ground contact.
        /// </summary>
        /// <returns>The adjusted foot position in world space.</returns>
        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * FootPositionAdjust;
        }
    }
}