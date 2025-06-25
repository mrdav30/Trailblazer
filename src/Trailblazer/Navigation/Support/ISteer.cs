using FixedMathSharp;

namespace Trailblazer.Navigation
{
    public interface ISteer
    {
        /// <summary>
        /// The current world position of the navigator.
        /// </summary>
        Vector3d Position { get; }

        /// <summary>
        /// The current velocity of the navigator in world space.
        /// </summary>
        Vector3d Velocity { get; }

        /// <summary>
        /// The current movement speed, derived from the magnitude of the velocity.
        /// </summary>
        Fixed64 Speed { get; }

        /// <summary>
        /// Minimum speed the agent must maintain to avoid being considered stuck.
        /// </summary>
        Fixed64 StuckThresholdSpeed { get; }

        /// <summary>
        /// The size of navigator in worldspace.
        /// </summary>
        /// <remarks>
        /// Note: Add a little padding to manevour around blockers
        /// </remarks>
        Fixed64 UnitSize { get; }

        /// <summary>
        /// Half the unit size, used for radius-based spatial checks.
        /// </summary>
        Fixed64 UnitRadius { get; }

        /// <summary>
        /// Indicates if the agent is currently steering left to avoid another object.
        /// </summary>
        bool IsAvoidingLeft { get; set; }
    }
}
