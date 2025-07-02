using FixedMathSharp;

namespace Trailblazer.Navigation.Turning
{
    public interface ITurn
    {
        /// <summary>
        /// The current world position of the navigator.
        /// </summary>
        Vector3d Position { get; }

        /// <summary>
        /// The last world position of the navigator.
        /// </summary>
        Vector3d LastPosition { get; }

        /// <summary>
        /// The navigator's visual rotation in world space.
        /// </summary>
        FixedQuaternion Rotation { get; }

        /// <summary>
        /// The direction the navigator is currently facing.
        /// </summary>
        Vector3d Forward { get; }

        void ApplyRotation(FixedQuaternion rot);
    }
}
