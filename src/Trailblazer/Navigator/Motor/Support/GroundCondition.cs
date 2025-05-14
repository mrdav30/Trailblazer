using FixedMathSharp;

namespace Trailblazer.Navigation.Motor
{
    /// <summary>
    /// Represents the state of the surface the scout is interacting with, including surface movement and normal data.
    /// </summary>
    public struct GroundCondition
    {
        /// <summary>
        /// The object the scout is currently standing on.
        /// </summary>
#nullable enable
        public object? BaseObject;
#nullable disable

        /// <summary>
        /// The transformation matrix of the surface object.
        /// </summary>
        public Fixed4x4? GroundMatrix;

        /// <summary>
        /// The normal vector of the surface, indicating its slope.
        /// </summary>
        public readonly Vector3d GroundNormal => GroundMatrix?.Up ?? Vector3d.Zero;

        /// <summary>
        /// The current surface friction applied to movement.
        /// </summary>
        public Fixed64 SurfaceFriction;

        /// <summary>
        /// Determines how the scout inherits movement from the ground surface.
        /// </summary>
        public MotionTransfer MotionTransferState;

        /// <summary>
        /// Represents an empty surface state with default values.
        /// </summary>
        public static readonly GroundCondition Empty = new();
    }
}
