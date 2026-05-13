using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Interface representing a generic pathfinding request. Defines shared data such as start/end voxels,
/// unit size, and optional parameters like walkability and search range limits. 
/// Also provides a hash key for caching or pooling.
/// </summary>
public interface IPathRequest
{
    /// <summary>
    /// The world context this request resolves and surveys against.
    /// </summary>
    TrailblazerWorldContext Context { get; }

    /// <summary>
    /// The origin world position.
    /// </summary>
    Vector3d Origin { get; }

    /// <summary>
    /// Most recently evaluated grid voxel under the agent.
    /// </summary>
    Voxel? StartNode { get; }

    /// <summary>
    /// The target world position. 
    /// </summary>
    Vector3d TargetPosition { get; }

    /// <summary>
    /// Final grid voxel targeted as the destination.
    /// </summary>
    Voxel? EndNode { get; }

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
    bool AllowUnwalkableEndpoints { get; }

    /// <summary>
    /// The max search limit used when generating the path.
    /// Requests must have a value greater than zero before they are considered valid.
    /// </summary>
    int MaxPathSearchRange { get; set; }

    /// <summary>
    /// Whether the request has a valid start voxel. 
    /// Requests with null start or end voxels are considered invalid.
    /// </summary>
    bool HasOrigin { get; }

    /// <summary>
    /// Whether the request has a valid end voxel.
    /// Requests with null start or end voxels are considered invalid.
    /// </summary>
    bool HasDestination { get; }

    /// <summary>
    /// Whether the request has valid start and end voxels.
    /// </summary>
    bool HasValidEndpoints { get; }

    /// <summary>
    /// Whether the request is valid and can be processed. 
    /// Requests must have valid endpoints and a positive search range to be considered valid.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// A unique hash for this path request, useful for caching and guide pooling.
    /// </summary>
    public int RequestCacheKey { get; }

    /// <summary>
    /// Updates the request with new origin and destination positions, along with an optional unit size.
    /// Returns true if the update was successful and the request is now valid, or false if the new
    /// parameters resulted in an invalid request. Failed updates clear the resolved endpoints and
    /// reset <see cref="MaxPathSearchRange"/> to zero.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="destination"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize);

    /// <summary>
    /// Attempts to update the request's origin position and corresponding start voxel. 
    /// Returns true if successful, or false if the new origin is invalid (e.g. no valid start voxel could be found).
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="resetSearchRange"></param>
    /// <returns></returns>
    bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false);

    /// <summary>
    /// Attempts to update the request's destination position and corresponding end voxel.
    /// Returns true if successful, or false if the new destination is invalid (e.g. no valid end voxel could be found).
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="resetSearchRange"></param>
    /// <returns></returns>
    bool TrySetDestination(Vector3d destination, bool resetSearchRange = false);

    /// <summary>
    /// Attempts to update the request's unit size and corresponding start/end voxel validity.
    /// Returns true if successful, or false if the new unit size is invalid (e.g. no valid start/end voxels could be found with the new size).
    /// </summary>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    bool TrySetUnitSize(Fixed64 unitSize);
}
