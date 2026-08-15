//=======================================================================
// NavigationFlowFieldWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal struct NavigationFlowFieldSearchNode
{
    internal NavigationCellAddress Address;
    internal Fixed64 IntegrationCost;
    internal NavigationSelectedEdgeRef SelectedEdge;
    internal int HeapIndex;
    internal bool HasSelectedEdge;
    internal bool Closed;
}

/// <summary>Owns reusable fixed-capacity buffers for one exclusive flow query.</summary>
internal sealed class NavigationFlowFieldWorkspace
{
    private readonly NavigationNodeRef[] _keys;
    private readonly NavigationFlowFieldSearchNode[] _records;
    private readonly long[] _stamps;
    private readonly int[] _activeSlots;
    private readonly NavigationPageStampSet _dependencyPageSet;
    private readonly NavigationAddressStampSet _dependencyComponentSet;
    private readonly int _mask;
    private readonly int _nodeCapacity;
    private int _nodeCount;
    private long _generation = 1;

    internal NavigationFlowFieldWorkspace(
        int dependencyPageCapacity,
        int dependencyComponentCapacity,
        int nodeCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(
            dependencyPageCapacity,
            nameof(dependencyPageCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            dependencyComponentCapacity,
            nameof(dependencyComponentCapacity));
        SwiftThrowHelper.ThrowIfNegative(nodeCapacity, nameof(nodeCapacity));
        int tableSize = 1;
        int required = checked(Math.Max(1, nodeCapacity * 2));
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _keys = new NavigationNodeRef[tableSize];
        _records = new NavigationFlowFieldSearchNode[tableSize];
        _stamps = new long[tableSize];
        _activeSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        _mask = tableSize - 1;
        _nodeCapacity = nodeCapacity;
        HeapSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        SettledSlots = nodeCapacity == 0 ? Array.Empty<int>() : new int[nodeCapacity];
        DependencyPages = dependencyPageCapacity == 0
            ? Array.Empty<GraphPageDependencyAddress>()
            : new GraphPageDependencyAddress[dependencyPageCapacity];
        DependencyComponents = dependencyComponentCapacity == 0
            ? Array.Empty<NavigationSurfaceComponentKey>()
            : new NavigationSurfaceComponentKey[dependencyComponentCapacity];
        _dependencyPageSet = new NavigationPageStampSet(
            Math.Max(1, dependencyPageCapacity));
        _dependencyComponentSet = new NavigationAddressStampSet(
            Math.Max(1, dependencyComponentCapacity));
    }

    internal int[] HeapSlots { get; }

    internal int[] SettledSlots { get; }

    internal GraphPageDependencyAddress[] DependencyPages { get; }

    internal NavigationSurfaceComponentKey[] DependencyComponents { get; }

    internal int HeapCount { get; set; }

    internal int SettledCount { get; set; }

    internal int DependencyPageCount { get; private set; }

    internal int DependencyComponentCount { get; private set; }

    internal void Reset()
    {
        if (_generation == long.MaxValue)
            throw new InvalidOperationException(
                "Flow node-table generation capacity is exhausted.");
        for (int i = 0; i < _nodeCount; i++)
        {
            int slot = _activeSlots[i];
            _keys[slot] = default;
            _records[slot] = default;
            _activeSlots[i] = 0;
        }
        if (DependencyPageCount > 0)
            Array.Clear(DependencyPages, 0, DependencyPageCount);
        if (DependencyComponentCount > 0)
            Array.Clear(DependencyComponents, 0, DependencyComponentCount);
        _generation++;
        _nodeCount = 0;
        HeapCount = 0;
        SettledCount = 0;
        DependencyPageCount = 0;
        DependencyComponentCount = 0;
        _dependencyPageSet.Reset();
        _dependencyComponentSet.Reset();
    }

    private bool TryGetSlot(NavigationNodeRef node, out int slot)
    {
        slot = node.GetHashCode() & _mask;
        while (_stamps[slot] == _generation)
        {
            if (_keys[slot] == node)
                return true;
            slot = (slot + 1) & _mask;
        }
        return false;
    }

    internal bool TryGetOrAdd(
        NavigationNodeRef node,
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

    internal NavigationNodeRef GetNode(int slot) => _keys[slot];

    internal bool TryRecordComponent(NavigationSurfaceComponentKey key)
    {
        if (!_dependencyComponentSet.Add(key.Representative))
            return true;
        if (DependencyComponentCount >= DependencyComponents.Length)
            return false;
        DependencyComponents[DependencyComponentCount++] = key;
        return true;
    }

    internal bool TryRecordPage(string mapId, int pageIndex)
    {
        if (_dependencyPageSet.Contains(mapId, pageIndex))
            return true;
        if (DependencyPageCount >= DependencyPages.Length)
            return false;
        _dependencyPageSet.Add(mapId, pageIndex);
        DependencyPages[DependencyPageCount++] =
            new GraphPageDependencyAddress(mapId, pageIndex);
        return true;
    }
}
