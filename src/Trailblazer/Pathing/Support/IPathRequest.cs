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
        Voxel StartNode { get; }

        /// <summary>
        /// Final grid voxel targeted as the destination.
        /// </summary>
        Voxel EndNode { get; }

        /// <summary>
        /// The physical unit diameter or size used to validate voxel walkability and clearance.
        /// </summary>
        Fixed64 UnitSize { get; }

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

        bool HasOrigin { get; }

        bool HasDestination { get; }

        bool HasValidEndpoints { get; }

        bool IsValid { get; }

        /// <summary>
        /// A unique hash for this path request, useful for caching and guide pooling.
        /// </summary>
        public int RequestCacheKey { get; }

        bool TryPrepare(Vector3d origin, Vector3d destination, Fixed64? unitSize);

        bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false);

        bool TrySetDestination(Vector3d destination, bool resetSearchRange = false);

        bool TrySetUnitSize(Fixed64 unitSize);

        bool Validate();
    }
}
