using FixedMathSharp;

/// <summary>
/// Defines how a scout inherits movement from the surface it is standing on.
/// </summary>
public enum MovementTransferState
{
    /// <summary>
    /// The scout is unaffected by the movement of the surface.
    /// </summary>
    None = 0,

    /// <summary>
    /// The scout receives an initial velocity from the surface but gradually slows down.
    /// </summary>
    InitTransfer = 1,

    /// <summary>
    /// The scout maintains its velocity from the surface until it lands again.
    /// </summary>
    PermaTransfer = 2,

    /// <summary>
    /// The scout is locked to the movement of the surface and moves along with it.
    /// </summary>
    PermaLocked = 3
}

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Represents the state of the ground the scout is interacting with, including surface movement and normal data.
    /// </summary>
    public struct GroundState
    {
        /// <summary>
        /// The object the scout is currently standing on.
        /// </summary>
#nullable enable
        public object? HitObject;
#nullable disable

        /// <summary>
        /// The transformation matrix of the ground object.
        /// </summary>
        public Fixed4x4? GroundMatrix;

        /// <summary>
        /// The normal vector of the ground, indicating its slope.
        /// </summary>
        public readonly Vector3d GroundNormal => GroundMatrix?.Up ?? Vector3d.Zero;

        /// <summary>
        /// Determines how the scout inherits movement from the ground surface.
        /// </summary>
        public MovementTransferState MovementTransfer;

        /// <summary>
        /// Represents an empty ground state with default values.
        /// </summary>
        public static readonly GroundState Empty = new();
    }
}
