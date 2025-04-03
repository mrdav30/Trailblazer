using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Defines the core interface for a scout entity, providing position, rotation, traversal state, and event handling.
    /// </summary>
    public interface IScout
    {
        /// <summary>
        /// The current world position of the scout.
        /// </summary>
        public Vector3d WorldPosition { get; }

        /// <summary>
        /// The scout's visual rotation in world space.
        /// </summary>
        public FixedQuaternion VisualRotation { get; }

        /// <summary>
        /// The controller responsible for managing the scout's movement and physics interactions.
        /// </summary>
        ScoutController ScoutController { get; }

        /// <summary>
        /// The set of events associated with the scout, allowing for external interactions such as force application and state transitions.
        /// </summary>
        #nullable enable
        ScoutEvents Events { get; }
#nullable disable

        /// <summary>
        /// Gets the position of the scout's foot in world space, typically used for ground checks and platform interactions.
        /// </summary>
        /// <returns>The world-space position of the scout's foot.</returns>
        Vector3d GetFootPosition();
    }
}
