//=======================================================================
// NavigationIncomingSurfaceEdge.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Pairs one predecessor with its original forward edge and durable locator.</summary>
internal readonly struct NavigationIncomingSurfaceEdge
{
    internal NavigationIncomingSurfaceEdge(
        NavigationNodeRef predecessor,
        NavigationGraphEdge forwardEdge,
        NavigationSelectedEdgeRef selectedEdge)
    {
        Predecessor = predecessor;
        ForwardEdge = forwardEdge;
        SelectedEdge = selectedEdge;
    }

    internal NavigationNodeRef Predecessor { get; }

    internal NavigationGraphEdge ForwardEdge { get; }

    internal NavigationSelectedEdgeRef SelectedEdge { get; }
}
