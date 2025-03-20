using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Represents the state of the surface the scout is interacting with, including surface movement and normal data.
    /// </summary>
    public struct SurfaceCondition
    {
        /// <summary>
        /// The object the scout is currently standing on.
        /// </summary>
#nullable enable
        public object? SurfaceObject;
#nullable disable

        /// <summary>
        /// The transformation matrix of the surface object.
        /// </summary>
        public Fixed4x4? SurfaceMatrix;

        /// <summary>
        /// The normal vector of the surface, indicating its slope.
        /// </summary>
        public readonly Vector3d SurfaceNormal => SurfaceMatrix?.Up ?? Vector3d.Zero;

        /// <summary>
        /// Determines how the scout inherits movement from the ground surface.
        /// </summary>
        public MotionTransfer MotionTransferState;

        /// <summary>
        /// Represents an empty surface state with default values.
        /// </summary>
        public static readonly SurfaceCondition Empty = new();
    }
}
