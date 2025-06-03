using FixedMathSharp;

/// <summary>
/// Defines the contract for a navigation guide responsible for computing and providing movement directions
/// to reach a specified destination using a particular pathfinding strategy (e.g., A* or flow field).
/// </summary>
namespace Trailblazer.Pathing
{
    public interface IGuide
    {
        /// <summary>
        /// Indicates whether a valid path has been computed.
        /// </summary>
        bool PathFound { get; }

        /// <summary>
        /// Indicates whether the guide is currently valid and can be used.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Indicates whether the guide is currently in use by an agent.
        /// </summary>
        public bool IsInUse { get; }

        /// <summary>
        /// The frame in which this guide was last used, used for eviction or reuse logic.
        /// </summary>
        public int LastUsedFrame { get; }

        /// <summary>
        /// A unique hash key representing the request that generated this guide.
        /// </summary>
        int RequestHashKey { get; }

        /// <summary>
        /// Indicates whether the path contains additional waypoints to follow.
        /// </summary>
        bool HasWaypoints { get; }

        /// <summary>
        /// Called once when the guide is initialized or recycled for reuse.
        /// </summary>
        /// <param name="request">The guide request configuration.</param>
        bool Initialize(IPathRequest request);

        /// <summary>
        /// Marks the guide as in use for the current frame or request.
        /// </summary>
        void MarkInUse();

        /// <summary>
        /// Determines whether the agent has reached the destination based on the current index.
        /// </summary>
        /// <param name="index">The current traversal index.</param>
        bool HasArrived(int index);


        /// <summary>
        /// Returns the index used to track guide progression based on the given position.
        /// </summary>
        /// <param name="from">The agent’s current position.</param>
        /// <returns>The progression index corresponding to the given position.</returns>
        int GetIndex(Vector3d from);

        /// <summary>
        /// Returns the direction the agent should move in, based on the current path and position.
        /// </summary>
        /// <param name="from">The current position of the agent.</param>
        /// <param name="index">The current index of the path collection the agent is traversing.</param>
        /// <returns>A normalized direction vector toward the next target.</returns>
        Vector3d GetMovementDirection(Vector3d from, int index);
        
        /// <summary>
        /// Releases the guide for reuse or reinitialization.
        /// </summary>
        void Release();

        /// <summary>
        /// Disposes of internal guide resources, if applicable.
        /// </summary>
        void Dispose();
    }
}
