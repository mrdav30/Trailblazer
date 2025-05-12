using FixedMathSharp;
using Trailblazer.Navigator.Motor;

namespace Trailblazer.Navigator
{
    /// <summary>
    /// Base abstract class representing a scout, responsible for handling movement, traversal state, and simulation flow.
    /// </summary>
    /// <remarks>
    /// This class acts as a bridge between the simulation logic (`ScoutController`) and the entity's external representation.  
    /// It defines common traversal behaviors and lifecycle methods that can be extended by concrete implementations.
    /// </remarks>
    public abstract class Scout : IScout
    {
        /// <inheritdoc cref="IScout.Position"/>
        public Vector3d Position { get; protected set; }

        /// <inheritdoc cref="IScout.Rotation"/>
        public FixedQuaternion Rotation { get; protected set; } = FixedQuaternion.Identity;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust { get; set; } = new Fixed64(0.25f);

        /// <inheritdoc cref="IScout.Controller"/>
        public NavigatorMotor Controller { get; protected set; }

        /// <inheritdoc cref="IScout.Events"/>
        public ScoutEvents Events { get; protected set; }

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition TraversalCondition { get; protected set; }

        /// <summary>
        /// Stores the movement request for the next traversal cycle.
        /// </summary>
        public TravelRequest TravelRequest { get; protected set; }

        #region Lifecycle

        /// <summary>
        /// Initializes the scout by setting up its events, traversal state, and movement controller.
        /// </summary>
        public virtual void OnInitialize(TraversalCondition initialCondition)
        {
            Events = new();
            TraversalCondition = initialCondition;
            Controller = NavigatorMotor.CreateNew(this, TraversalCondition);
        }

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <param name="medium">The traversal medium (e.g., ground, air, water).</param>
        /// <param name="surfaceLevel">The vertical surface level, if applicable.</param>
        /// <param name="surfaceCondition">The ground state data, if applicable.</param>
        /// <param name="ceilingLevel">The vertical ceiling level, if applicable.</param>
        public virtual void SetTraversalCondition(
            TraversalMedium? medium = null,
            Fixed64? surfaceLevel = null,
            GroundCondition? surfaceCondition = null,
            Fixed64? ceilingLevel = null)
        {
            TraversalCondition.Medium = medium ?? TraversalCondition.Medium;
            TraversalCondition.SurfaceLevel = surfaceLevel ?? TraversalCondition.SurfaceLevel;
            TraversalCondition.GroundState = surfaceCondition ?? TraversalCondition.GroundState;
            TraversalCondition.CeilingLevel = ceilingLevel ?? TraversalCondition.CeilingLevel;
        }

        public virtual void SetTraversalCondition(TraversalCondition condition) => TraversalCondition = condition;

        /// <summary>
        /// Sets the movement request for the next simulation frame.
        /// </summary>
        /// <param name="movementDirection">The desired movement direction.</param>
        /// <param name="movementSpeed">The movement speed category.</param>
        /// <param name="isRequestingJump">Whether the scout is attempting to jump.</param>
        public virtual void SetTravelRequest(
            Vector3d? movementDirection = null,
            MovementSpeed? movementSpeed = null,
            bool? isRequestingJump = null)
        {
            TravelRequest.MovementDirection = movementDirection ?? TravelRequest.MovementDirection;
            TravelRequest.MovementSpeed = movementSpeed ?? TravelRequest.MovementSpeed;
            TravelRequest.IsRequestingJump = isRequestingJump ?? TravelRequest.IsRequestingJump;
        }

        public virtual void SetTravelRequest(TravelRequest request) => TravelRequest = request;

        #endregion

        #region Simulation

        /// <summary>
        /// Handles the start of traversal by passing the movement request to the <see cref="Controller"/>.
        /// </summary>
        public virtual void StartTraversal()
        {
            Controller.Traverse(TravelRequest);
            TravelRequest = default;
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        public virtual void FinalizeTraversal()
        {
            Controller.FinishFrameTraversal(TraversalCondition);
        }

        /// <summary>
        /// Returns the world-space position of the scout’s foot, adjusted for proper ground contact.
        /// </summary>
        /// <returns>The adjusted foot position in world space.</returns>
        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * FootPositionAdjust;
        }

        #endregion
    }
}
