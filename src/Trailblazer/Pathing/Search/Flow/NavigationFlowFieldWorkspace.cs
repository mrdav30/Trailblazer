//=======================================================================
// NavigationFlowFieldWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns reusable fixed-capacity buffers for one exclusive flow query.</summary>
internal sealed class NavigationFlowFieldWorkspace
{
    private readonly NavigationMediumStateRef[] _keys;
    private readonly NavigationFlowFieldSearchNode[] _records;
    private readonly long[] _stamps;
    private readonly int[] _activeSlots;
    private readonly int _mask;
    private readonly int _nodeCapacity;
    private int _nodeCount;
    private long _generation = 1;

    internal NavigationFlowFieldWorkspace(
        int mapCapacity,
        int dependencyPageCapacity,
        int dependencyComponentCapacity,
        int nodeCapacity,
        int rayCoveredAddressCapacity,
        int rayTraceIntervalCapacity)
    {
        ChronicleGuard.ThrowIfNegative(
            dependencyPageCapacity,
            nameof(dependencyPageCapacity));
        ChronicleGuard.ThrowIfNegative(
            dependencyComponentCapacity,
            nameof(dependencyComponentCapacity));
        ChronicleGuard.ThrowIfNegative(nodeCapacity, nameof(nodeCapacity));
        int tableSize = 1;
        int required = checked(Math.Max(1, nodeCapacity * 2));
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _keys = new NavigationMediumStateRef[tableSize];
        _records = new NavigationFlowFieldSearchNode[tableSize];
        _stamps = new long[tableSize];
        _activeSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        _mask = tableSize - 1;
        _nodeCapacity = nodeCapacity;
        HeapSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        SettledSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        EndpointWorkspace = new NavigationEndpointWorkspace(
            mapCapacity,
            dependencyPageCapacity,
            dependencyComponentCapacity);
        RayWorkspace = new NavigationRayWorkspace(
            mapCapacity,
            dependencyPageCapacity,
            dependencyComponentCapacity,
            rayCoveredAddressCapacity,
            rayTraceIntervalCapacity);
    }

    internal NavigationEndpointWorkspace EndpointWorkspace { get; }

    internal NavigationRayWorkspace RayWorkspace { get; }

    internal int[] HeapSlots { get; }

    internal int[] SettledSlots { get; }

    internal int NodeCapacity => _nodeCapacity;

    internal int DependencyPageCapacity => DependencyPages.Length;

    internal int DependencyComponentCapacity => DependencyComponents.Length;

    internal GraphPageDependencyAddress[] DependencyPages => EndpointWorkspace.Pages;

    internal NavigationSurfaceComponentKey[] DependencyComponents =>
        EndpointWorkspace.Components;

    internal int HeapCount { get; set; }

    internal int SettledCount { get; set; }

    internal int DependencyPageCount => EndpointWorkspace.PageCount;

    internal int DependencyComponentCount => EndpointWorkspace.ComponentCount;

    internal void Reset()
    {
        ResetSearch();
        EndpointWorkspace.Reset();
        RayWorkspace.Reset();
    }

    internal void ResetSearch()
    {
        int remaining = int.MaxValue;
        AdvanceResetSearch(ref remaining);
    }

    /// <summary>Releases prior search records within the supplied node slice.</summary>
    internal bool AdvanceResetSearch(ref int nodeRemaining)
    {
        while (_nodeCount > 0 && nodeRemaining > 0)
        {
            int ordinal = --_nodeCount;
            int slot = _activeSlots[ordinal];
            _keys[slot] = default;
            _records[slot] = default;
            _activeSlots[ordinal] = 0;
            nodeRemaining--;
        }
        if (_nodeCount > 0)
            return false;
        unchecked
        {
            _generation++;
        }
        HeapCount = 0;
        SettledCount = 0;
        return true;
    }

    internal bool TryGetSlot(NavigationMediumStateRef node, out int slot)
    {
        slot = node.GetHashCode() & _mask;
        while (_stamps[slot] == _generation)
        {
            if (_keys[slot].Equals(node))
                return true;
            slot = (slot + 1) & _mask;
        }
        return false;
    }

    internal bool TryGetOrAdd(
        NavigationMediumStateRef node,
        out int slot,
        out bool added)
    {
        if (TryGetSlot(node, out slot))
        {
            added = false;
            return true;
        }
        if (_nodeCount >= _nodeCapacity)
        {
            added = false;
            return false;
        }
        _keys[slot] = node;
        _records[slot] = default;
        _stamps[slot] = _generation;
        _activeSlots[_nodeCount++] = slot;
        added = true;
        return true;
    }

    internal ref NavigationFlowFieldSearchNode GetRecord(int slot) =>
        ref _records[slot];

    internal NavigationMediumStateRef GetNode(int slot) => _keys[slot];

    internal bool TryRecordComponent(NavigationSurfaceComponentKey key)
    {
        return EndpointWorkspace.TryRecordComponent(key);
    }

    internal bool TryRecordPage(string mapId, int pageIndex)
    {
        return EndpointWorkspace.TryRecordPage(mapId, pageIndex);
    }
}
