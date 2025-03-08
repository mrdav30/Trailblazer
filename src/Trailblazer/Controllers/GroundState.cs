using FixedMathSharp;

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
        public readonly Vector3d GroundNormal => GroundMatrix?.ExtractUp() ?? Vector3d.Zero;

        /// <summary>
        /// The default traversal state.
        /// </summary>
        public readonly static GroundState DefaultGroundState = new()
        {
            GroundMatrix = Fixed4x4.Identity,
        };

        public GroundState(
            object hitObject = null, 
            Fixed4x4? groundMatrix = null)
        {
            HitObject = hitObject;
            GroundMatrix = groundMatrix;
        }
    }
}
