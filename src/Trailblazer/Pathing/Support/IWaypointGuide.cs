using FixedMathSharp;

namespace Trailblazer.Pathing
{
    public interface IWaypointGuide : IGuide
    {
        /// <summary>
        /// Returns the index of the current waypoint being pursued.
        /// </summary>
        int CurrentWaypointIndex { get; }

        /// <summary>
        /// Returns the index used to track guide progression based on the given position.
        /// </summary>
        /// <param name="from">The agent’s current position.</param>
        /// <returns>The progression index corresponding to the given position.</returns>
        int GetIndex(Vector3d from);

        /// <summary>
        /// Advances the current waypoint index (called when agent has reached the current target).
        /// </summary>
        void AdvanceWaypoint();

        /// <summary>
        /// Returns the direction the agent should move in, based on the current path and position.
        /// </summary>
        /// <param name="from">The current position of the agent.</param>
        /// <returns>A normalized direction vector toward the next target.</returns>
        Vector3d GetMovementDirection(Vector3d from);
    }
}
