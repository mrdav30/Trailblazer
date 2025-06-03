using GridForge.Grids;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// A static class representing a heap of <see cref="PathPartition"/>> for efficient pathfinding.
    /// </summary>
    public static class PathPartitionHeap
    {
        /// <summary>
        /// /// Default initial capacity of the heap (64 x 64 = 4096).
        /// </summary>
        public const int DefaultCapacity = 64 * 64;

        /// <summary>
        /// Gets the number of items in the heap.
        /// </summary>
        public static uint Count { get; private set; }

        /// <summary>
        /// Internal version counter to distinguish heap generations.
        /// </summary>
        private static uint _heapVersion = 1;

        /// <summary>
        /// Internal storage for heap items.
        /// </summary>
        private static PathPartition[] _items = new PathPartition[DefaultCapacity];

        /// <summary>
        /// Current total capacity of the heap.
        /// </summary>
        public static int Capacity => _items.Length;

        /// <summary>
        /// Adds a PathPartition to the heap.
        /// </summary>
        public static void Add(PathPartition partition)
        {
            if (Count + 1 > _items.Length)
                Resize(_items.Length * 2);
            partition.HeapIndex = Count;
            _items[Count++] = partition;
            SortUp(partition);
            partition.HeapVersion = _heapVersion;
        }

        /// <summary>
        /// Resizes the internal array to accommodate more items.
        /// </summary>
        private static void Resize(int newSize)
        {
            int newCapacity = newSize <= DefaultCapacity ? DefaultCapacity : newSize;

            PathPartition[] newArray = new PathPartition[newCapacity];
            if (Count > 0)
                Array.Copy(_items, 0, newArray, 0, Count);
            _items = newArray;
        }

        /// <summary>
        /// Retrieves the PathPartition at the specified index without removing it.
        /// </summary>
        public static PathPartition PeekAt(int index) => _items[index];

        /// <summary>
        /// Removes and returns the first PathPartition in the heap.
        /// </summary>
        /// <returns>The removed PathPartition.</returns>
        public static bool RemoveFirst(out PathPartition result)
        {
            result = null;
            if (Count == 0) 
                return false;

            result = _items[0];
            Count--;

            if (Count == 0)
                _items[0] = null;
            else
            {
                PathPartition temp = _items[Count];
                _items[0] = temp;
                _items[0].HeapIndex = 0;
                _items[Count] = null;

                if (Count > 1)
                    SortDown(temp);
            }

            result.HeapVersion--;
            return true;
        }

        /// <summary>
        /// Checks if the heap contains the specified PathPartition.
        /// </summary>
        /// <param name="item">The PathPartition to check.</param>
        /// <returns>True if the heap contains the PathPartition, otherwise false.</returns>
        public static bool Contains(PathPartition item) => item.HeapVersion == _heapVersion;

        /// <summary>
        /// Marks the specified PathPartition as closed.
        /// </summary>
        /// <param name="item">The PathPartition to mark as closed.</param>
        public static void SetClosed(PathPartition item) => item.ClosedHeapVersion = _heapVersion;

        /// <summary>
        /// Checks if the specified PathPartition is closed.
        /// </summary>
        /// <param name="item">The PathPartition to check.</param>
        /// <returns>True if the PathPartition is closed, otherwise false.</returns>
        public static bool IsClosed(PathPartition item) => item.ClosedHeapVersion == _heapVersion;

        /// <summary>
        /// Sorts a PathPartition up the heap based on its HeapCost.
        /// </summary>
        public static void SortUp(PathPartition item)
        {
            uint index = item.HeapIndex;

            while (index > 0 && index < Count)
            {
                uint parentIndex = (index - 1) / 2;
                PathPartition parent = _items[parentIndex];

                if (item.HeapCost >= parent.HeapCost)
                    break;

                Swap(item, parent);
                index = item.HeapIndex;
            }
        }

        /// <summary>
        /// Sorts a PathPartition down the heap based on its HeapCost.
        /// </summary>
        public static void SortDown(PathPartition item)
        {
            uint index = item.HeapIndex;

            while (true)
            {
                uint left = 2 * index + 1;
                uint right = 2 * index + 2;
                uint smallest = index;

                if (left < Count && _items[left].HeapCost < _items[smallest].HeapCost)
                    smallest = left;

                if (right < Count && _items[right].HeapCost < _items[smallest].HeapCost)
                    smallest = right;

                if (smallest == index)
                    break;

                Swap(item, _items[smallest]);
                index = item.HeapIndex;
            }
        }

        /// <summary>
        /// Swaps two PathPartitions in the heap and updates their HeapIndex.
        /// </summary>
        public static void Swap(PathPartition itemA, PathPartition itemB)
        {
            uint indexA = itemA.HeapIndex;
            uint indexB = itemB.HeapIndex;

            _items[indexA] = itemB;
            _items[indexB] = itemA;

            itemA.HeapIndex = indexB;
            itemB.HeapIndex = indexA;
        }

        /// <summary>
        /// Clears the heap quickly by incrementing the heap version.
        /// </summary>
        public static void FastClear()
        {
            _heapVersion++;
            Count = 0;
        }

        /// <summary>
        /// Resets the heap by setting the heap version to 1 and clearing the count.
        /// </summary>
        public static void Reset()
        {
            _heapVersion = 1;
            Count = 0;
        }
    }
}
