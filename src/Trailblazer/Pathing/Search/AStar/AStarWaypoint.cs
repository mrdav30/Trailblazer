//=======================================================================
// AStarWaypoint.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a waypoint used in A* pathfinding, containing position, cost, and goal information for a specific node in the search.
/// </summary>
/// <remarks>
/// This struct is typically used to store intermediate or final results during A* pathfinding operations.
/// </remarks>
public struct AStarWaypoint
{
    /// <summary>
    /// Smoothed world position
    /// </summary>
    public Vector3d Position;

    /// <summary>
    /// Which voxel this corresponds to
    /// </summary>
    public WorldVoxelIndex? GlobalIndex;

    /// <summary>
    /// PathCost at this node (MovementCost + Heuristic at this node)
    /// </summary>
    public int PathCost;

    /// <summary>
    /// True if this is the goal node
    /// </summary>
    public bool IsGoal;
}
