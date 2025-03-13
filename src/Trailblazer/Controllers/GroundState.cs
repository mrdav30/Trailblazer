using FixedMathSharp;

/// <summary>
/// This state controls how a platform's velocity effects the IScout
/// </summary>
public enum MovementTransferState
{
    /// <summary>
    /// The scout is not affected by velocity of the surface at all.
    /// </summary>
    None = 0,
    /// <summary>
    /// scout gets its initial velocity from the surface, then gradualy comes to a stop.
    /// </summary>
    InitTransfer = 1,
    /// <summary>
    /// scout gets its initial velocity from the surface, and keeps that velocity until landing.
    /// </summary>
    PermaTransfer = 2,
    /// <summary>
    /// scout is relative to the movement of the last touched surface and will move together with that surface.
    /// </summary>
    PermaLocked = 3
}

namespace Trailblazer.Controllers
{
    /// <summary>
    /// The data of the ground.
    /// </summary>
    public struct GroundState
    {
        /// <summary>
        /// The object the scout is on.
        /// </summary>
#nullable enable
        public object? HitObject;
#nullable disable

        /// <summary>
        /// The transform of the object the scout is on.
        /// </summary>
        public Fixed4x4? GroundMatrix;

        /// <summary>
        /// The normal of the ground matrix translation.
        /// </summary>
        public readonly Vector3d GroundNormal => GroundMatrix?.Up ?? Vector3d.Zero;

        public MovementTransferState MovementTransfer;

        public GroundState(
            object hitObject = null, 
            Fixed4x4? groundMatrix = null,
            MovementTransferState movementTransfer = MovementTransferState.None)
        {
            HitObject = hitObject;
            GroundMatrix = groundMatrix;
            MovementTransfer = movementTransfer;
        }
    }
}
