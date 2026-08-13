//=======================================================================
// TraversalMedium.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer;

/// <summary>
/// Specifies the traversal medium used by runtime, pathing, and transition systems.
/// </summary>
public enum TraversalMedium
{
    /// <summary>
    /// The traversal medium is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Solid surface traversal.
    /// </summary>
    Solid = 1,

    /// <summary>
    /// Gas traversal.
    /// </summary>
    Gas = 2,

    /// <summary>
    /// Liquid traversal.
    /// </summary>
    Liquid = 3,
}
