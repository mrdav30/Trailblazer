//=======================================================================
// FlowField.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a flow field cell used in vector field-based pathfinding.
/// Stores directional and distance data used to navigate agents through the grid.
/// </summary>
public struct FlowField
{
    /// <summary>
    /// The global coordinates corresponding to this voxel in the field.
    /// </summary>
    public WorldVoxelIndex GlobalIndex { get; set; }

    /// <summary>
    /// The movement direction vector pointing toward the goal from this cell.
    /// </summary>
    public Vector3d Direction { get; set; }

    /// <summary>
    /// The scalar distance from this voxel to the goal in grid steps.
    /// </summary>
    public int PathCost { get; set; }

    /// <summary>
    /// Indicates whether this voxel is the goal or anchor in the flow field.
    /// </summary>
    public bool IsGoal { get; set; }
}
