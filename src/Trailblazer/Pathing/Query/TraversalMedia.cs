//=======================================================================
// TraversalMedia.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes the traversal media supported or requested by a navigation contract.
/// </summary>
[Flags]
public enum TraversalMedia
{
    /// <summary>No traversal medium.</summary>
    None = 0,

    /// <summary>Solid traversal.</summary>
    Solid = 1 << 0,

    /// <summary>Gas traversal.</summary>
    Gas = 1 << 1,

    /// <summary>Liquid traversal.</summary>
    Liquid = 1 << 2,

    /// <summary>Convenience mask covering both volume traversal media.</summary>
    AnyVolume = Gas | Liquid
}
