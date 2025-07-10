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
        /// The world position from the previous frame, used for velocity calculations.
        /// </summary>
        Vector3d LastPosition { get; }

        /// <summary>
        /// The navigator's visual rotation in world space.
        /// </summary>
        FixedQuaternion Rotation { get; }

        /// <summary>
        /// The current velocity of the navigator in world space.
        /// </summary>
        Vector3d Velocity { get; }

        /// <summary>
        /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
        /// </summary>
        TrekCondition FrameCondition { get; }

        /// <summary>
        /// The traversal request for the current frame, containing directional intent and travel mode.
        /// </summary>
        TrekRequest FrameRequest { get; }

        /// <summary>
        /// Adds the given delta to the current frame’s position offset.
        /// </summary>
        /// <param name="delta">The offset to apply to position this frame.</param>
        void AddPositionDelta(Vector3d delta);

        /// <summary>
        /// Adds the given delta to the current frame’s rotation offset.
        /// </summary>
        /// <param name="delta">The offset to apply to rotation this frame.</param>
        void AddRotationDelta(FixedQuaternion delta);

        /// <summary>
        /// Adds the given delta to the current frame’s velocity offset.
        /// </summary>
        /// <param name="delta">The offset to apply to velocity this frame.</param>
        void AddVelocityDelta(Vector3d delta);

        /// <summary>
        /// Performs a grounded surface check to determine the current traversal condition.
        /// Implementations should update the surface state based on collision or probe logic.
        /// </summary>
        void CheckTrekCondition();

        /// <summary>
        /// The default amount to offset the foot position when calculating ground contact.
        /// </summary>
        Vector3d GetFootPosition();
    }
}
