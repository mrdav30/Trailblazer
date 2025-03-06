using FixedMathSharp;

namespace Trailblazer.Controllers
{
    public enum TraversalMedium
    {
        Ground = 0,
        Air = 1,
        Water = 2,
        Unknown = 99
    }

    /// <summary>
    /// The data of the ground.
    /// </summary>
    public struct MovementState
    {
        public TraversalMedium Medium { get; set; }

        /// <summary>
        /// The object the scout is on.
        /// </summary>
        public object HitObject { get; set; }

        /// <summary>
        /// The transform of the object the scout is on.
        /// </summary>
        public Fixed4x4 GroundMatrix { get; set; }

        /// <summary>
        /// The normal of the ground matrix translation.
        /// </summary>
        public Vector3d GroundNormal { get; set; }

        /// <summary>
        /// Stores the height of the current surface.
        /// </summary>
        public Fixed64 SurfaceLevel { get; set; }

        /// <summary>
        /// The default traversal state.
        /// </summary>
        public readonly static MovementState DefaultMovementState = new MovementState
        {
            Medium = TraversalMedium.Unknown,
            HitObject = null,
            GroundMatrix = Fixed4x4.Identity,
            GroundNormal = Vector3d.Zero,
            SurfaceLevel = Fixed64.Zero
        };
    }
}
