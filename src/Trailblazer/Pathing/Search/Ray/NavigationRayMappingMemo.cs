//=======================================================================
// NavigationRayMappingMemo.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Bounded immutable mapping facts for one immediate workspace's owning store.</summary>
internal sealed class NavigationRayMappingMemo
{
    internal const int Capacity = 1024;
    private readonly NavigationRayMappingSnapshot[] _entries = new NavigationRayMappingSnapshot[Capacity];
    private NavigationWorldGraphStore? _store;
    private long _graphVersion;

    // Called only after the original single-map Solid direct-ray qualifications. The store
    // publishes strictly increasing versions, including reset; entries retain no graph or map root.
    internal bool TryPrepare(in NavigationRayRequest request)
    {
        if (_store == null)
            _store = request.Store;
        if (!ReferenceEquals(_store, request.Store))
            return false;
        if (_graphVersion != request.ExpectedGraph.GraphVersion)
        {
            Clear();
            _graphVersion = request.ExpectedGraph.GraphVersion;
        }
        return true;
    }

    internal ref NavigationRayMappingSnapshot GetSlot(VoxelIndex index) =>
        ref _entries[index.GetHashCode() & (Capacity - 1)];

    internal bool TryGet(VoxelIndex index, out NavigationRayMappingSnapshot snapshot)
    {
        snapshot = GetSlot(index);
        return snapshot.IsValid && snapshot.Index == index;
    }

    // A completed ray keeps its facts. Context reset/disposal and the next observed graph
    // version drop all entries, including their bounded map-name references.
    internal void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
        _graphVersion = 0;
    }

    internal void Dispose()
    {
        Clear();
        _store = null;
    }
}
