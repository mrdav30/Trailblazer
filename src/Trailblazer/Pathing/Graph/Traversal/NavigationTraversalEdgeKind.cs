//=======================================================================
// NavigationTraversalEdgeKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies which existing authority produced one evaluated traversal edge.</summary>
internal enum NavigationTraversalEdgeKind : byte
{
    Surface = 0,
    Volume = 1,
    Transition = 2
}
