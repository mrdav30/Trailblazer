//=======================================================================
// HeuristicMethod.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Specifies the heuristic method used for estimating distances in pathfinding algorithms.
/// </summary>
/// <remarks>
/// Use this enumeration to select the distance calculation strategy appropriate for the grid or
/// coordinate system in use.
/// Manhattan is typically used for four-directional grids, Octile for eight-directional
/// grids, and Euclidean for continuous or diagonal movement scenarios.
/// </remarks>
internal enum HeuristicMethod
{
    /// <summary>
    /// Represents the Manhattan distance metric, also known as the L1 norm, used to calculate the distance between
    /// points as the sum of the absolute differences of their coordinates.
    /// </summary>
    Manhattan,
    /// <summary>
    /// Represents the Octile distance metric, which is a modification of the Manhattan distance that accounts for
    /// diagonal movement in grid-based pathfinding.
    /// </summary>
    Octile,
    /// <summary>
    /// Represents the Euclidean distance metric used for measuring straight-line distance between points in Euclidean space.
    /// </summary>
    Euclidean
    //Chebyshev?
}
