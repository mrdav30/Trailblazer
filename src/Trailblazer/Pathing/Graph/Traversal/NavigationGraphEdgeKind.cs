//=======================================================================
// NavigationGraphEdgeKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the structural kind of one graph edge.</summary>
internal enum NavigationGraphEdgeKind : byte
{
    Native = 0,
    Explicit = 1,
    Seam = 2
}
