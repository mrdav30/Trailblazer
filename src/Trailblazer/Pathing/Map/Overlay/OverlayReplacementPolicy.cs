//=======================================================================
// OverlayReplacementPolicy.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Selects how an existing semantic overlay is handled when its map is replaced.
/// </summary>
public enum OverlayReplacementPolicy
{
    /// <summary>
    /// Retain the overlay and reject the replacement if it is invalid against the new map.
    /// </summary>
    PreserveAndRevalidate = 0,

    /// <summary>
    /// Atomically install the replacement map with an empty overlay.
    /// </summary>
    Clear = 1
}
