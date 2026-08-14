//=======================================================================
// NavigationGraphEdge.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the structural kind of one graph edge.</summary>
internal enum NavigationGraphEdgeKind : byte
{
    Native = 0
}

/// <summary>Describes one allocation-free graph edge result.</summary>
internal readonly struct NavigationGraphEdge
{
    internal NavigationGraphEdge(NavigationNodeRef target, NavigationGraphEdgeKind kind)
    {
        Target = target;
        Kind = kind;
    }

    internal NavigationNodeRef Target { get; }

    internal NavigationGraphEdgeKind Kind { get; }
}
