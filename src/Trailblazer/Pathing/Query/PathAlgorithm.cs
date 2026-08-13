//=======================================================================
// PathAlgorithm.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Selects the deterministic search algorithm used by a path query.
/// </summary>
public enum PathAlgorithm
{
    /// <summary>
    /// Computes an origin-to-destination path with the internal certified heuristic.
    /// </summary>
    AStar = 0,

    /// <summary>
    /// Computes a reusable destination-centric reverse-Dijkstra flow field.
    /// </summary>
    FlowField = 1
}
