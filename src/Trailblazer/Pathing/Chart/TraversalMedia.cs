//=======================================================================
// TraversalMedia.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes which authored traversal media are present in a dense <see cref="NavigationChartCell"/>.
/// </summary>
[Flags]
public enum TraversalMedia
{
    /// <summary>
    /// This cell contributes no authored traversal data.
    /// </summary>
    None = 0,

    /// <summary>
    /// This cell contributes chart-backed solid traversal.
    /// </summary>
    Solid = 1 << 0,

    /// <summary>
    /// This cell contributes authored gas traversal.
    /// </summary>
    Gas = 1 << 1,

    /// <summary>
    /// This cell contributes authored liquid traversal.
    /// </summary>
    Liquid = 1 << 2,

    /// <summary>
    /// Convenience mask covering all authored volume traversal kinds.
    /// </summary>
    AnyVolume = Gas | Liquid
}
