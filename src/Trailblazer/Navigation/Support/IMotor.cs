using FixedMathSharp;

namespace Trailblazer.Navigation
{
    public interface IMotor
    {
        /// <summary>
        /// The current world position of the navigator.
        /// </summary>
        Vector3d Position { get; }

        /// <summary>
        /// The navigator's visual rotation in world space.
        /// </summary>
        FixedQuaternion Rotation { get; }

        /// <summary>
        /// The default amount to offset the foot position when calculating ground contact.
        /// </summary>
        Vector3d GetFootPosition();
    }
}
