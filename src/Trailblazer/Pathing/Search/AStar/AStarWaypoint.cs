using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

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
