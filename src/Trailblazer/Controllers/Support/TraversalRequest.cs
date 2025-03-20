using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Represents a movement request for the scout, including direction, speed, and jump intent.
    /// </summary>
    public struct TraversalRequest
    {
        /// <summary>
        /// The global direction in which the scout wants to move.
        /// </summary>
        public Vector3d MovementDirection { get;set; }

        /// <summary>
        /// The speed at which the scout wants to move.
        /// </summary>
        public TraversalSpeed TraversalSpeed { get; set; }

        /// <summary>
        /// Indicates whether the scout is requesting to jump.
        /// </summary>
        public bool IsRequestingJump { get; set; }

        /// <summary>
        /// Determines if the scout is actively moving based on direction and speed.
        /// </summary>
        public readonly bool IsMoving => MovementDirection != Vector3d.Zero && TraversalSpeed != TraversalSpeed.Stationary;

        /// <summary>
        /// Represents an empty movement request with default values.
        /// </summary>
        public static readonly TraversalRequest Empty = new();
    }
}
