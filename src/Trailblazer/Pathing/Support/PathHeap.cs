using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Trailblazer.Tests")]

namespace Trailblazer.Pathing
{
    internal class PathHeapMeta
    {
        /// <summary>
        /// The index of this voxel in the heap.
        /// </summary>
        public uint HeapIndex;

        /// <summary>
        /// Internal version counter to distinguish heap generations.
        /// </summary>
        public uint HeapVersion;

        /// <summary>
        /// A version used to track closed voxels in the heap for the current search.
        /// </summary>
        public uint ClosedHeapVersion;
    }

    /// <summary>
    /// A static class representing a heap of <see cref="PathPartition"/>> for efficient pathfinding.
    /// </summary>
    internal class PathHeap
    {
        /// <summary>
        /// /// Default initial capacity of the heap (64 x 64 = 4096).
        /// </summary>
        public const int DefaultCapacity = 128;

        /// <summary>
        /// Internal storage for heap items.
        /// </summary>
        private PathPartition[] _items;

        private readonly SwiftDictionary<PathPartition, PathHeapMeta> _meta;

        public uint CurrentHeapVersion { get; private set; } = 0;

        /// <summary>
        /// Gets the number of items in the heap.
        /// </summary>
        public uint HeapCount { get; private set; }

        public int ClosedCount => _meta.Count;

        /// <summary>
        /// Current total capacity of the heap.
        /// </summary>
        public int Capacity => _items.Length;

        public PathHeap()
        {
            _items = new PathPartition[DefaultCapacity];
            _meta = new(DefaultCapacity);
            CurrentHeapVersion = 1;
        }

        /// <summary>
        /// Adds a PathPartition to the heap.
        /// </summary>
        public void Add(PathPartition item)
        {
            // exit early if item already in the heap
            if (Contains(item))
                return;

            if (HeapCount + 1 > _items.Length)
                Resize(_items.Length * 2);

            PathHeapMeta meta = new()
            {
                HeapIndex = HeapCount,
                HeapVersion = CurrentHeapVersion
            };
            _meta[item] = meta;
            _items[HeapCount++] = item;
            SortUp(item);
        }

        /// <summary>
        /// Resizes the internal array to accommodate more items.
        /// </summary>
        private void Resize(int newSize)
        {
            int newCapacity = newSize <= DefaultCapacity ? DefaultCapacity : newSize;

            PathPartition[] newArray = new PathPartition[newCapacity];
            if (HeapCount > 0)
                Array.Copy(_items, 0, newArray, 0, HeapCount);
            _items = newArray;

            _meta.EnsureCapacity(newCapacity);
        }

        /// <summary>
        /// Retrieves the PathPartition at the specified index without removing it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PathPartition PeekAt(int index) => _items[index];

        /// <summary>
        /// Removes and returns the first PathPartition in the heap.
        /// </summary>
        /// <returns>The removed PathPartition.</returns>
        public bool RemoveFirst(out PathPartition result)
        {
            if (HeapCount == 0)
            {
                result = null;
                return false;
            }

            result = _items[0];
            if (!_meta.TryGetValue(result, out PathHeapMeta meta))
                return false;

            HeapCount--;

            if (HeapCount == 0)
                _items[0] = null;
            else
            {
                PathPartition temp = _items[HeapCount];
                PathHeapMeta tempMeta = _meta[temp];
                _items[0] = temp;
                tempMeta.HeapIndex = 0;
                _items[HeapCount] = null;

                if (HeapCount > 1)
                    SortDown(temp);
            }

            meta.HeapVersion--;
            return true;
        }

        /// <summary>
        /// Checks if the heap contains the specified PathPartition.
        /// </summary>
        /// <param name="item">The PathPartition to check.</param>
        /// <returns>True if the heap contains the PathPartition, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(PathPartition item)
        {
            if (!_meta.TryGetValue(item, out PathHeapMeta meta)
                || meta.HeapVersion != CurrentHeapVersion)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Marks the specified PathPartition as closed.
        /// </summary>
        /// <param name="item">The PathPartition to mark as closed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetClosed(PathPartition item)
        {
            _meta[item].ClosedHeapVersion = CurrentHeapVersion;
        }

        /// <summary>
        /// Checks if the specified PathPartition is closed.
        /// </summary>
        /// <param name="item">The PathPartition to check.</param>
        /// <returns>True if the PathPartition is closed, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsClosed(PathPartition item)
        {
            if (!_meta.TryGetValue(item, out PathHeapMeta meta)
                || meta.ClosedHeapVersion != CurrentHeapVersion)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sorts a PathPartition up the heap based on its HeapCost.
        /// </summary>
        public void SortUp(PathPartition item)
        {
            PathHeapMeta meta = _meta[item];
            uint index = meta.HeapIndex;
            while (index > 0 && index < HeapCount)
            {
                uint parentIndex = (index - 1) / 2;
                PathPartition parent = _items[parentIndex];

                if (item.PathCost >= parent.PathCost)
                    break;

                Swap(item, parent);
                index = meta.HeapIndex;
            }
        }

        /// <summary>
        /// Sorts a PathPartition down the heap based on its HeapCost.
        /// </summary>
        public void SortDown(PathPartition item)
        {
            PathHeapMeta meta = _meta[item];
            uint index = meta.HeapIndex;
            while (true)
            {
                uint left = (index * 2) + 1;
                uint right = left + 1;
                uint lowest = index;

                if (left < HeapCount && _items[left].PathCost < _items[lowest].PathCost)
                    lowest = left;

                if (right < HeapCount && _items[right].PathCost < _items[lowest].PathCost)
                    lowest = right;

                if (lowest == index)
                    break;

                Swap(item, _items[lowest]);

                index = meta.HeapIndex;
            }
        }

        /// <summary>
        /// Swaps two PathPartitions in the heap and updates their HeapIndex.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap(PathPartition itemA, PathPartition itemB)
        {
            PathHeapMeta metaA = _meta[itemA];
            PathHeapMeta metaB = _meta[itemB];

            uint indexA = metaA.HeapIndex;
            uint indexB = metaB.HeapIndex;

            _items[indexA] = itemB;
            _items[indexB] = itemA;

            metaA.HeapIndex = indexB;
            metaB.HeapIndex = indexA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<PathPartition> EnumerateClosed()
        {
            foreach (KeyValuePair<PathPartition, PathHeapMeta> kvp in _meta)
            {
                if (kvp.Value.ClosedHeapVersion == CurrentHeapVersion)
                    yield return kvp.Key;
            }
        }

        /// <summary>
        /// Clears the heap quickly by incrementing the heap version.
        /// </summary>
        public void FastClear()
        {
            HeapCount = 0;
            CurrentHeapVersion++;
            _meta.Clear();
        }

        /// <summary>
        /// Resets the heap by setting the heap version to 1 and clearing the count.
        /// </summary>
        public void Reset()
        {
            HeapCount = 0;
            CurrentHeapVersion = 1;
            _meta.Clear();
            _meta.TrimExcess();
        }
    }
}
