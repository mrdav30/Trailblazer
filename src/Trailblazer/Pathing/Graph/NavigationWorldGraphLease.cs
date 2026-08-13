//=======================================================================
// NavigationWorldGraphLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Owns one exact immutable graph generation checkout.</summary>
internal sealed class NavigationWorldGraphLease : IDisposable
{
    private NavigationWorldGraphStore? _owner;
    private NavigationWorldGraph? _graph;

    internal NavigationWorldGraphLease(
        NavigationWorldGraphStore owner,
        NavigationWorldGraph graph)
    {
        _owner = owner;
        _graph = graph;
        graph.Checkout();
    }

    internal NavigationWorldGraph Graph => Volatile.Read(ref _graph)!;

    internal void Reinitialize(NavigationWorldGraphStore owner, NavigationWorldGraph graph)
    {
        _graph = graph;
        Volatile.Write(ref _owner, owner);
        graph.Checkout();
    }

    internal NavigationWorldGraph DetachGraph() => Interlocked.Exchange(ref _graph, null)!;

    public void Dispose()
    {
        NavigationWorldGraphStore? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(this);
    }
}
