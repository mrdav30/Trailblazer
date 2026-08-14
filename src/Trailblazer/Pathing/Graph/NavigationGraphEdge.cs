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
    Explicit = 1,
    Seam = 2
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
        AutomaticSeam = default;
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
        AutomaticSeam = default;
    }

    internal NavigationGraphEdge(
        NavigationNodeRef target,
        NavigationAutomaticSeamRef automaticSeam)
    {
        Target = target;
        Kind = NavigationGraphEdgeKind.Seam;
        NativePortal = default;
        NativeDirectionOrdinal = -1;
        ExplicitConnection = null!;
        AutomaticSeam = automaticSeam;
    }

    internal NavigationNodeRef Target { get; }

    internal NavigationGraphEdgeKind Kind { get; }

    internal GridNavigationPortal NativePortal { get; }

    internal int NativeDirectionOrdinal { get; }

    internal NavigationExplicitConnectionRecord ExplicitConnection { get; }

    internal NavigationAutomaticSeamRef AutomaticSeam { get; }
}
