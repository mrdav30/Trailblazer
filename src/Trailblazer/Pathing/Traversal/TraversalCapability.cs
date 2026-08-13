//=======================================================================
// TraversalCapability.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes traversal abilities that authored cells or transitions may require.
/// </summary>
[Flags]
public enum TraversalCapability
{
    /// <summary>
    /// No additional traversal ability is required.
    /// </summary>
    None = 0,

    /// <summary>
    /// The agent can use jump transitions.
    /// </summary>
    Jump = 1 << 0,

    /// <summary>
    /// The agent can use climb transitions.
    /// </summary>
    Climb = 1 << 1,

    /// <summary>
    /// The agent can traverse media that require swimming.
    /// </summary>
    Swim = 1 << 2,

    /// <summary>
    /// The agent can traverse freely through supported gas volumes.
    /// </summary>
    Fly = 1 << 3,

    /// <summary>
    /// The agent can use teleport transitions.
    /// </summary>
    Teleport = 1 << 4
}
