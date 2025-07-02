using FixedMathSharp;
using Trailblazer.Navigation.Motor;

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
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        TraversalCondition TraversalState { get; }

        void AddPositionDelta(Vector3d delta);

        void AddRotationDelta(FixedQuaternion delta);

        void AddVelocityDelta(Vector3d delta);

        /// <summary>
        /// The default amount to offset the foot position when calculating ground contact.
        /// </summary>
        Vector3d GetFootPosition();
    }
}
