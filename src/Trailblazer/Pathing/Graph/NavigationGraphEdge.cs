//=======================================================================
// NavigationGraphEdge.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Identifies the structural kind of one graph edge.</summary>
internal enum NavigationGraphEdgeKind : byte
{
    Native = 0,
    Explicit = 1
}

/// <summary>Describes one allocation-free graph edge result.</summary>
internal readonly struct NavigationGraphEdge
{
    internal NavigationGraphEdge(
        NavigationNodeRef target,
        NavigationGraphEdgeKind kind,
        GridNavigationPortal nativePortal,
        int nativeDirectionOrdinal = -1)
    {
        Target = target;
        Kind = kind;
        NativePortal = nativePortal;
        NativeDirectionOrdinal = nativeDirectionOrdinal;
        ExplicitConnection = null!;
    }

    internal NavigationGraphEdge(
        NavigationNodeRef target,
        NavigationExplicitConnectionRecord explicitConnection)
    {
        Target = target;
        Kind = NavigationGraphEdgeKind.Explicit;
        NativePortal = default;
        NativeDirectionOrdinal = -1;
        ExplicitConnection = explicitConnection;
    }

    internal NavigationNodeRef Target { get; }

    internal NavigationGraphEdgeKind Kind { get; }

    internal GridNavigationPortal NativePortal { get; }

    internal int NativeDirectionOrdinal { get; }

    internal NavigationExplicitConnectionRecord ExplicitConnection { get; }
}
