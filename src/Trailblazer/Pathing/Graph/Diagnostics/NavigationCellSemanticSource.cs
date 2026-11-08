//=======================================================================
// NavigationCellSemanticSource.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the semantic source of one effective graph cell.</summary>
public enum NavigationCellSemanticSource : byte
{
    /// <summary>The effective value comes from the immutable map bake.</summary>
    Baked = 0,
    /// <summary>A semantic overlay supplies the effective payload.</summary>
    OverlaySet = 1,
    /// <summary>A semantic overlay tombstones traversal at the address.</summary>
    OverlaySuppressed = 2,
    /// <summary>A semantic overlay owns an unbaked dynamic cell slot.</summary>
    DynamicOverlaySet = 3,
    /// <summary>An unbaked dynamic slot remains reserved but has no effective payload.</summary>
    DynamicInactive = 4
}
