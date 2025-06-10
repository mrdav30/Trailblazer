using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Interface representing a generic pathfinding request. Defines shared data such as start/end voxels,
    /// unit size, and optional parameters like walkability and search range limits. 
    /// Also provides a hash key for caching or pooling.
    /// </summary>
    public interface IPathRequest
    {
        /// <summary>
        /// Most recently evaluated grid voxel under the agent.
        /// </summary>
        Voxel Start { get; set; }

        /// <summary>
        /// Final grid voxel targeted as the destination.
        /// </summary>
        Voxel End { get; set; }

        /// <summary>
        /// The physical unit diameter or size used to validate voxel walkability and clearance.
        /// </summary>
        Fixed64 UnitSize { get; set; }

        /// <summary>
        /// Whether the start and end voxels are the same or null, indicating no meaningful travel is required.
        /// </summary>
        bool HasZeroDisplacement { get; }

        /// <summary>
        /// Optional override to allow reaching unwalkable destinations (useful for edge cases).
        /// </summary>
        bool AllowUnwalkable { get; }

        /// <summary>
        /// An optional max search limit when generating the path.
        /// If null, the search will continue until all walkable voxels are evaluated.
        /// </summary>
        int? MaxPathSearchRange { get; set; }

        bool IsValid { get; }

        /// <summary>
        /// A unique hash for this path request, useful for caching and guide pooling.
        /// </summary>
        public int RequestCacheKey { get; }

        void Prepare();
    }
}
