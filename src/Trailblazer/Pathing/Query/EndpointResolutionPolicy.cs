//=======================================================================
// EndpointResolutionPolicy.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Selects how a world-space path endpoint may resolve to a navigation node.
/// </summary>
public enum EndpointResolutionPolicy
{
    /// <summary>
    /// Requires the exact endpoint position to be navigable.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Permits deterministic nearest-navigable resolution within the configured distance.
    /// </summary>
    NearestNavigable = 1
}
