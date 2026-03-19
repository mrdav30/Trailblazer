using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

internal sealed class AerialPathHeapMeta
{
    public uint HeapIndex;

    public uint HeapVersion;

    public uint ClosedHeapVersion;

    public int PathCost;
}

/// <summary>
/// Heap optimized for raw-voxel aerial A* expansion.
/// </summary>
internal sealed class AerialPathHeap
{
    public const int DefaultCapacity = 128;

    private Voxel[] _items;

    private readonly SwiftDictionary<Voxel, AerialPathHeapMeta> _meta;

    public uint CurrentHeapVersion { get; private set; } = 1;

    public uint HeapCount { get; private set; }

    public int ClosedCount => _meta.Count;

    public AerialPathHeap()
    {
        _items = new Voxel[DefaultCapacity];
        _meta = new(DefaultCapacity);
    }

    public void Add(Voxel item, int pathCost)
    {
        if (Contains(item))
            return;

        if (HeapCount + 1 > _items.Length)
            Resize(_items.Length * 2);

        AerialPathHeapMeta meta = new()
        {
            HeapIndex = HeapCount,
            HeapVersion = CurrentHeapVersion,
            PathCost = pathCost
        };

        _meta[item] = meta;
        _items[HeapCount++] = item;
        SortUp(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPathCost(Voxel item, out int pathCost)
    {
        if (!_meta.TryGetValue(item, out AerialPathHeapMeta meta))
        {
            pathCost = int.MaxValue;
            return false;
        }

        pathCost = meta.PathCost;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdatePathCost(Voxel item, int pathCost)
    {
        if (_meta.TryGetValue(item, out AerialPathHeapMeta meta))
            meta.PathCost = pathCost;
    }

    public bool RemoveFirst(out Voxel result)
    {
        if (HeapCount == 0)
        {
            result = null;
            return false;
        }

        result = _items[0];
        if (!_meta.TryGetValue(result, out AerialPathHeapMeta meta))
            return false;

        HeapCount--;

        if (HeapCount == 0)
            _items[0] = null;
        else
        {
            Voxel temp = _items[HeapCount];
            AerialPathHeapMeta tempMeta = _meta[temp];
            _items[0] = temp;
            tempMeta.HeapIndex = 0;
            _items[HeapCount] = null;

            if (HeapCount > 1)
                SortDown(temp);
        }

        meta.HeapVersion--;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Voxel item)
    {
        return _meta.TryGetValue(item, out AerialPathHeapMeta meta)
            && meta.HeapVersion == CurrentHeapVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetClosed(Voxel item)
    {
        if (_meta.TryGetValue(item, out AerialPathHeapMeta meta))
            meta.ClosedHeapVersion = CurrentHeapVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsClosed(Voxel item)
    {
        return _meta.TryGetValue(item, out AerialPathHeapMeta meta)
            && meta.ClosedHeapVersion == CurrentHeapVersion;
    }

    public void SortUp(Voxel item)
    {
        AerialPathHeapMeta meta = _meta[item];
        uint index = meta.HeapIndex;
        while (index > 0 && index < HeapCount)
        {
            uint parentIndex = (index - 1) / 2;
            Voxel parent = _items[parentIndex];

            if (meta.PathCost >= _meta[parent].PathCost)
                break;

            Swap(item, parent);
            index = meta.HeapIndex;
        }
    }

    public void SortDown(Voxel item)
    {
        AerialPathHeapMeta meta = _meta[item];
        uint index = meta.HeapIndex;

        while (true)
        {
            uint left = (index * 2) + 1;
            uint right = left + 1;
            uint lowest = index;

            if (left < HeapCount
                && _meta[_items[left]].PathCost < _meta[_items[lowest]].PathCost)
            {
                lowest = left;
            }

            if (right < HeapCount
                && _meta[_items[right]].PathCost < _meta[_items[lowest]].PathCost)
            {
                lowest = right;
            }

            if (lowest == index)
                break;

            Swap(item, _items[lowest]);
            index = meta.HeapIndex;
        }
    }

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

        Voxel[] newArray = new Voxel[newCapacity];
        if (HeapCount > 0)
            Array.Copy(_items, 0, newArray, 0, HeapCount);

        _items = newArray;
        _meta.EnsureCapacity(newCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Swap(Voxel itemA, Voxel itemB)
    {
        AerialPathHeapMeta metaA = _meta[itemA];
        AerialPathHeapMeta metaB = _meta[itemB];

        uint indexA = metaA.HeapIndex;
        uint indexB = metaB.HeapIndex;

        _items[indexA] = itemB;
        _items[indexB] = itemA;

        metaA.HeapIndex = indexB;
        metaB.HeapIndex = indexA;
    }
}
