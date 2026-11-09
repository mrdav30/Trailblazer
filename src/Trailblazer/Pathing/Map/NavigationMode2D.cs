//=======================================================================
// NavigationMode2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the native motion mode of a planar navigation map.</summary>
public enum NavigationMode2D : byte
{
    /// <summary>Free movement in the navigation plane.</summary>
    TopDown = 1,

    /// <summary>Movement with gravity and support in the navigation plane.</summary>
    SideView = 2
}
