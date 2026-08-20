//=======================================================================
// NavigationAStarNodeTable.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal struct NavigationAStarNodeRecord
{
    internal Fixed64 Cost;
    internal Fixed64 Heuristic;
    // The search-only f-score adds eight bytes per workspace node, never to cached payloads.
    internal Fixed64 EstimatedTotalCost;
    internal NavigationNodeRef Parent;
    internal int HeapIndex;
    internal int ParentEdgeOrdinal;
    internal bool HasParent;
    internal bool Closed;
}

/// <summary>Stores fixed-capacity generation-stamped A* metadata.</summary>
internal sealed class NavigationAStarNodeTable
{
    private readonly NavigationNodeRef[] _keys;
    private readonly NavigationAStarNodeRecord[] _records;
    private readonly long[] _stamps;
    private readonly int _mask;
    private readonly int _capacity;
    private int _count;
    private long _generation = 1;

    internal NavigationAStarNodeTable(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        int tableSize = 1;
        int required = checked(Math.Max(1, capacity * 2));
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _keys = new NavigationNodeRef[tableSize];
        _records = new NavigationAStarNodeRecord[tableSize];
        _stamps = new long[tableSize];
        _mask = tableSize - 1;
        _capacity = capacity;
    }

    internal void Reset()
    {
        if (_generation == long.MaxValue)
            throw new InvalidOperationException("A* node-table generation capacity is exhausted.");
        _generation++;
        _count = 0;
    }

    internal bool TryGetSlot(NavigationNodeRef node, out int slot)
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
        if (_count >= _capacity)
        {
            added = false;
            return false;
        }
        _keys[slot] = node;
        _records[slot] = default;
        _stamps[slot] = _generation;
        _count++;
        added = true;
        return true;
    }

    internal ref NavigationAStarNodeRecord GetRecord(int slot) => ref _records[slot];
}
