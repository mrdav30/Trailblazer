using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Interface representing a generic pathfinding request. Defines shared data such as start/end nodes,
    /// unit size, and optional parameters like walkability and search range limits. 
    /// Also provides a hash key for caching or pooling.
    /// </summary>
    public interface IPathRequest
    {
        /// <summary>
        /// Most recently evaluated grid node under the agent.
        /// </summary>
        Node Start { get; set; }

        /// <summary>
        /// Final grid node targeted as the destination.
        /// </summary>
        Node End { get; set; }

        /// <summary>
        /// The physical unit diameter or size used to validate node walkability and clearance.
        /// </summary>
        Fixed64 UnitSize { get; set; }

        /// <summary>
        /// Whether the start and end nodes are the same or null, indicating no meaningful travel is required.
        /// </summary>
        bool HasZeroDisplacement { get; }

        /// <summary>
        /// Optional override to allow reaching unwalkable destinations (useful for edge cases).
        /// </summary>
        bool AllowUnwalkable { get; }

        /// <summary>
        /// An optional max search limit when generating the path.
        /// If null, the search will continue until all walkable nodes are evaluated.
        /// </summary>
        int? MaxPathSearchRange { get; }

        /// <summary>
        /// A unique hash for this path request, useful for caching and guide pooling.
        /// </summary>
        public int RequestCacheKey { get; }
    }
}
