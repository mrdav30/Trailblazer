using FixedMathSharp;

namespace Trailblazer.Controllers
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
        /// <inheritdoc cref="IScout.WorldPosition"/>
        public Vector3d WorldPosition { get; protected set; }

        /// <inheritdoc cref="IScout.VisualRotation"/>
        public FixedQuaternion VisualRotation { get; protected set; } = FixedQuaternion.Identity;

        /// <summary>
        /// Adjustment factor for the foot position, used to determine ground contact points.
        /// </summary>
        public Fixed64 FootPositionAdjust { get; set; } = new Fixed64(0.25f);

        protected ScoutController _controller;

        /// <inheritdoc cref="IScout.ScoutController"/>
        public ScoutController ScoutController => _controller;

        protected ScoutEvents _events;

        /// <inheritdoc cref="IScout.Events"/>
        public ScoutEvents Events => _events;

        protected TraversalCondition _traversalCondition;

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition TraversalCondition => _traversalCondition;

        /// <summary>
        /// Stores the movement request for the next traversal cycle.
        /// </summary>
        protected TravelRequest _travelRequest;

        #region Lifecycle

        /// <summary>
        /// Initializes the scout by setting up its events, traversal state, and movement controller.
        /// </summary>
        public virtual void OnInitialize(TraversalCondition initialCondition)
        {
            _events = new();
            _traversalCondition = initialCondition;
            _controller = ScoutController.CreateNew(this, _traversalCondition);
        }

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <param name="medium">The traversal medium (e.g., ground, air, water).</param>
        /// <param name="surfaceLevel">The vertical surface level, if applicable.</param>
        /// <param name="surfaceCondition">The ground state data, if applicable.</param>
        /// <param name="ceilingLevel">The vertical ceiling level, if applicable.</param>
        public virtual void SetTraversalCondition(
            TraversalMedium medium,
            Fixed64? surfaceLevel = null,
            GroundCondition? surfaceCondition = null,
            Fixed64? ceilingLevel = null)
        {
            _traversalCondition.Medium = medium;
            _traversalCondition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
            _traversalCondition.GroundState = surfaceCondition ?? null;
            _traversalCondition.CeilingLevel = ceilingLevel ?? Fixed64.MAX_VALUE;
        }

        public virtual void SetTraversalCondition(TraversalCondition condition) => _traversalCondition = condition;

        /// <summary>
        /// Sets the movement request for the next simulation frame.
        /// </summary>
        /// <param name="movementDirection">The desired movement direction.</param>
        /// <param name="movementSpeed">The movement speed category.</param>
        /// <param name="isRequestingJump">Whether the scout is attempting to jump.</param>
        public virtual void SetTravelRequest(
            Vector3d? movementDirection = null,
            MovementSpeed? movementSpeed = null,
            bool isRequestingJump = false)
        {
            _travelRequest.MovementDirection = movementDirection ?? Vector3d.Zero;
            _travelRequest.MovementSpeed = movementSpeed ?? MovementSpeed.Stationary;
            _travelRequest.IsRequestingJump = isRequestingJump;
        }

        public virtual void SetTravelRequest(TravelRequest request) => _travelRequest = request;

        #endregion

        #region Simulation

        /// <summary>
        /// Handles the start of traversal by passing the movement request to the <see cref="ScoutController"/>.
        /// </summary>
        public virtual void StartTraversal()
        {
            ScoutController.Traverse(_travelRequest);
            _travelRequest = default;
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        public virtual void FinalizeTraversal()
        {
            ScoutController.FinishFrameTraversal(_traversalCondition);
        }

        /// <summary>
        /// Returns the world-space position of the scout’s foot, adjusted for proper ground contact.
        /// </summary>
        /// <returns>The adjusted foot position in world space.</returns>
        public virtual Vector3d GetFootPosition()
        {
            return WorldPosition + Vector3d.Down * FootPositionAdjust;
        }

        #endregion
    }
}
