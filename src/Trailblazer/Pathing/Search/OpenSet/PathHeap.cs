//=======================================================================
// PathHeap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

internal struct PathHeapMeta
{
    public uint HeapIndex;

    public uint HeapVersion;

    public uint ClosedHeapVersion;

    public int PathCost;

    public int TieBreakCost;
}

/// <summary>
/// Shared binary heap used by the pathing surveyors to track open and closed nodes.
/// Heap ordering cost is owned by the heap metadata instead of the node types themselves.
/// </summary>
internal sealed class PathHeap<TNode> where TNode : class
{
    public const int DefaultCapacity = 128;

    private TNode[] _items;

    private readonly PathHeapMetadata<TNode> _meta;

    public uint CurrentHeapVersion { get; private set; } = 1;

    public uint HeapCount { get; private set; }

    public int TrackedCount => _meta.Count;

    public int Capacity => _items.Length;

    public PathHeap()
    {
        _items = new TNode[DefaultCapacity];
        _meta = new(DefaultCapacity);
    }

    public void Add(TNode item, int pathCost, int tieBreakCost = 0)
    {
        if (Contains(item))
            return;

        if (HeapCount + 1 > _items.Length)
            Resize(_items.Length * 2);

        PathHeapMeta meta = new()
        {
            HeapIndex = HeapCount,
            HeapVersion = CurrentHeapVersion,
            PathCost = pathCost,
            TieBreakCost = tieBreakCost
        };

        _meta[item] = meta;
        _items[HeapCount++] = item;
        SortUp(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPathCost(TNode item, out int pathCost)
    {
        if (!_meta.TryGetValue(item, out PathHeapMeta meta))
        {
            pathCost = int.MaxValue;
            return false;
        }

        pathCost = meta.PathCost;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdatePathCost(TNode item, int pathCost, int tieBreakCost = 0)
    {
        if (_meta.TryGetValue(item, out PathHeapMeta meta))
        {
            meta.PathCost = pathCost;
            meta.TieBreakCost = tieBreakCost;
            _meta[item] = meta;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TNode PeekAt(int index) => _items[index];

    public bool RemoveFirst([MaybeNullWhen(false)] out TNode result)
    {
        if (HeapCount == 0)
        {
            result = null!;
            return false;
        }

        result = _items[0];
        if (!_meta.TryGetValue(result, out PathHeapMeta meta))
            return false;

        HeapCount--;

        if (HeapCount == 0)
        {
            _items[0] = null!;
        }
        else
        {
            TNode temp = _items[HeapCount];
            PathHeapMeta tempMeta = _meta[temp];
            _items[0] = temp;
            tempMeta.HeapIndex = 0;
            _meta[temp] = tempMeta;
            _items[HeapCount] = null!;

            if (HeapCount > 1)
                SortDown(temp);
        }

        meta.HeapVersion--;
        _meta[result] = meta;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TNode item)
    {
        return _meta.TryGetValue(item, out PathHeapMeta meta)
            && meta.HeapVersion == CurrentHeapVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetClosed(TNode item)
    {
        if (_meta.TryGetValue(item, out PathHeapMeta meta))
        {
            meta.ClosedHeapVersion = CurrentHeapVersion;
            _meta[item] = meta;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsClosed(TNode item)
    {
        return _meta.TryGetValue(item, out PathHeapMeta meta)
            && meta.ClosedHeapVersion == CurrentHeapVersion;
    }

    public void SortUp(TNode item)
    {
        PathHeapMeta meta = _meta[item];
        uint index = meta.HeapIndex;

        while (index > 0 && index < HeapCount)
        {
            uint parentIndex = (index - 1) / 2;
            TNode parent = _items[parentIndex];

            if (!HasHigherPriority(meta, _meta[parent]))
                break;

            Swap(item, parent);
            meta = _meta[item];
            index = meta.HeapIndex;
        }
    }

    public void SortDown(TNode item)
    {
        PathHeapMeta meta = _meta[item];
        uint index = meta.HeapIndex;

        while (true)
        {
            uint left = (index * 2) + 1;
            uint right = left + 1;
            uint lowest = index;

            if (left < HeapCount
                && HasHigherPriority(_meta[_items[left]], _meta[_items[lowest]]))
            {
                lowest = left;
            }

            if (right < HeapCount
                && HasHigherPriority(_meta[_items[right]], _meta[_items[lowest]]))
            {
                lowest = right;
            }

            if (lowest == index)
                break;

            Swap(item, _items[lowest]);
            meta = _meta[item];
            index = meta.HeapIndex;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PathHeapMetadata<TNode>.ClosedEnumerable EnumerateClosed()
        => _meta.EnumerateClosed(CurrentHeapVersion);

    public void FastClear()
    {
        HeapCount = 0;
        CurrentHeapVersion++;
        _meta.Clear();
    }

    public void Reset()
    {
        HeapCount = 0;
        CurrentHeapVersion = 1;
        _meta.Clear();
        _meta.TrimExcess();
    }

    private void Resize(int newSize)
    {
        int newCapacity = newSize <= DefaultCapacity ? DefaultCapacity : newSize;

        TNode[] newArray = new TNode[newCapacity];
        if (HeapCount > 0)
            Array.Copy(_items, 0, newArray, 0, HeapCount);

        _items = newArray;
        _meta.EnsureCapacity(newCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasHigherPriority(PathHeapMeta candidate, PathHeapMeta current)
    {
        return candidate.PathCost < current.PathCost
            || (candidate.PathCost == current.PathCost
                && candidate.TieBreakCost < current.TieBreakCost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Swap(TNode itemA, TNode itemB)
    {
        PathHeapMeta metaA = _meta[itemA];
        PathHeapMeta metaB = _meta[itemB];

        uint indexA = metaA.HeapIndex;
        uint indexB = metaB.HeapIndex;

        _items[indexA] = itemB;
        _items[indexB] = itemA;

        metaA.HeapIndex = indexB;
        metaB.HeapIndex = indexA;
        _meta[itemA] = metaA;
        _meta[itemB] = metaB;
    }
}
