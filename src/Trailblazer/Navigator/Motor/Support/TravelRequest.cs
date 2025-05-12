using FixedMathSharp;

namespace Trailblazer.Navigator.Motor
{
    /// <summary>
    /// Represents a movement request for the scout, including direction, speed, and jump intent.
    /// </summary>
    public class TravelRequest
    {
        /// <summary>
        /// The global direction in which the scout wants to move.
        /// </summary>
        public Vector3d MovementDirection { get; set; }

        /// <summary>
        /// The speed at which the scout wants to move.
        /// </summary>
        public MovementSpeed MovementSpeed { get; set; }

        /// <summary>
        /// Indicates whether the scout is requesting to jump.
        /// </summary>
        public bool IsRequestingJump { get; set; }

        /// <summary>
        /// Determines if the scout is actively moving based on direction and speed.
        /// </summary>
        public bool IsMoving => MovementDirection != Vector3d.Zero && MovementSpeed != MovementSpeed.Stationary;

        /// <summary>
        /// Represents an empty movement request with default values.
        /// </summary>
        public static readonly TravelRequest Empty = new();
    }
}
