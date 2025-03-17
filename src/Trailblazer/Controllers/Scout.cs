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

        protected TraversalCondition _traversalState;

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        public TraversalCondition TraversalState => _traversalState;

        /// <summary>
        /// Stores the movement request for the next traversal cycle.
        /// </summary>
        protected TraversalRequest _traversalRequest;

        #region Lifecycle

        /// <summary>
        /// Initializes the scout by setting up its events, traversal state, and movement controller.
        /// </summary>
        public virtual void OnInitialize()
        {
            _events = new();
            _traversalState = TraversalCondition.Empty;
            _controller = ScoutController.CreateNew(this);
        }

        /// <summary>
        /// Updates the scout’s traversal state, including its current medium and surface information.
        /// </summary>
        /// <param name="medium">The traversal medium (e.g., ground, air, water).</param>
        /// <param name="surfaceLevel">The vertical surface level, if applicable.</param>
        /// <param name="movementState">The ground state data, if applicable.</param>
        public virtual void SetTraversalState(
            TraversalMedium medium,
            Fixed64? surfaceLevel = null,
            GroundState? movementState = null)
        {
            _traversalState.Medium = medium;
            _traversalState.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
            _traversalState.Ground = movementState ?? null;
        }

        /// <summary>
        /// Sets the movement request for the next simulation frame.
        /// </summary>
        /// <param name="movementDirection">The desired movement direction.</param>
        /// <param name="traversalSpeed">The movement speed category.</param>
        /// <param name="isRequestingJump">Whether the scout is attempting to jump.</param>
        public virtual void SetTraversalRequest(
            Vector3d movementDirection,
            TraversalSpeed traversalSpeed,
            bool isRequestingJump = false)
        {
            _traversalRequest.MovementDirection = movementDirection;
            _traversalRequest.TraversalSpeed = traversalSpeed;
            _traversalRequest.IsRequestingJump = isRequestingJump;
        }

        #endregion

        #region Simulation

        public virtual void OnSimulate()
        {
            StartTraversal();
        }

        /// <summary>
        /// Handles the start of traversal by passing the movement request to the <see cref="ScoutController"/>.
        /// </summary>
        public virtual void StartTraversal()
        {
            ScoutController.Traverse(_traversalRequest);
            _traversalRequest = default;
        }

        /// <summary>
        /// Retrieves the current traversal state of the scout.
        /// </summary>
        /// <param name="traversalState">The output parameter containing the traversal condition.</param>
        public virtual void GetTraversalState(out TraversalCondition traversalState)
        {
            traversalState = TraversalState;
        }

        public virtual void OnLateUpdate()
        {
            FinalizeTraversal();
        }

        /// <summary>
        /// Finalizes traversal by updating movement calculations and applying corrections.
        /// </summary>
        public virtual void FinalizeTraversal()
        {
            ScoutController.FinishFrameTraversal();
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
