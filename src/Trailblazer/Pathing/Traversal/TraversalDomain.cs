//=======================================================================
// TraversalDomain.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Identifies the graph domain requested by a navigation query.
/// </summary>
public enum TraversalDomain
{
    /// <summary>
    /// Resolves the domain deterministically from the endpoint and profile.
    /// </summary>
    Automatic = 0,

    /// <summary>
    /// Uses solid surface traversal.
    /// </summary>
    Surface = 1,

    /// <summary>
    /// Uses gas or liquid volume traversal.
    /// </summary>
    Volume = 2
}
