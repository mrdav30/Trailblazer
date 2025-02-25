// Thanks to Sebastian Lague's tutorial: https://www.youtube.com/watch?v=3Dw5d7PlcTM

using System;

namespace Lockstep.Simulation.Pathfinding
{
    /// <summary>
    /// A static class representing a heap of GridNodes for efficient pathfinding.
    /// </summary>
    public static class PathPartitionHeap
    {
        public const int DefaultCapacity = 64 * 64;

        /// <summary>
        /// Gets the number of items in the heap.
        /// </summary>
        public static uint Count { get; private set; }

        private static uint _heapVersion = 1;

        private static PathPartition[] _items = new PathPartition[DefaultCapacity];

        public static int Capacity => _items.Length;

        /// <summary>
        /// Adds a GridNode to the heap.
        /// </summary>
        /// <param name="item">The GridNode to add.</param>
        public static void Add(PathPartition item)
        {
            if (Count + 1 > _items.Length)
                Resize(_items.Length * 2);
            item.HeapIndex = Count;
            _items[Count++] = item;
            SortUp(item);
            item.HeapVersion = _heapVersion;
        }

        // Ensures the capacity of the internal array is sufficient.
        private static void Resize(int newSize)
        {
            int newCapacity = newSize <= DefaultCapacity ? DefaultCapacity : newSize;

            PathPartition[] newArray = new PathPartition[newCapacity];
            if (Count > 0)
                Array.Copy(_items, 0, newArray, 0, Count);
            _items = newArray;
        }

        /// <summary>
        /// Removes and returns the first GridNode in the heap.
        /// </summary>
        /// <returns>The removed GridNode.</returns>
        public static PathPartition RemoveFirst()
        {
            PathPartition result = _items[0];
            PathPartition temp = _items[--Count];
            _items[0] = temp;
            _items[0].HeapIndex = 0;
            SortDown(_items[0]);
            result.HeapVersion--;
            return result;
        }

        /// <summary>
        /// Checks if the heap contains the specified GridNode.
        /// </summary>
        /// <param name="item">The GridNode to check.</param>
        /// <returns>True if the heap contains the GridNode, otherwise false.</returns>
        public static bool Contains(PathPartition item) => item.HeapVersion == _heapVersion;

        /// <summary>
        /// Marks the specified GridNode as closed.
        /// </summary>
        /// <param name="item">The GridNode to mark as closed.</param>
        public static void SetClosed(PathPartition item) => item.ClosedHeapVersion = _heapVersion;

        /// <summary>
        /// Checks if the specified GridNode is closed.
        /// </summary>
        /// <param name="item">The GridNode to check.</param>
        /// <returns>True if the GridNode is closed, otherwise false.</returns>
        public static bool IsClosed(PathPartition item) => item.ClosedHeapVersion == _heapVersion;

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

        // Sorts the specified GridNode down the heap.
        public static void SortDown(PathPartition item)
        {
            while (true)
            {
                uint childIndexLeft = item.HeapIndex * 2 + 1;
                uint childIndexRight = item.HeapIndex * 2 + 2;

                if (childIndexLeft > Count)
                    break;

                uint swapIndex = childIndexLeft;

                if (childIndexRight < Count)
                {
                    if (_items[childIndexLeft].TotalCost > _items[childIndexRight].TotalCost)
                        swapIndex = childIndexRight;
                }

                PathPartition swapPartition = _items[swapIndex];
                if (item.TotalCost < swapPartition.TotalCost)
                    break;

                Swap(item, swapPartition);
            }
        }

        // Sorts the specified GridNode up the heap.
        public static void SortUp(PathPartition item)
        {
            if (item.HeapIndex == 0)
                return;

            uint parentIndex = (item.HeapIndex - 1) / 2;
            while (true)
            {
                PathPartition curNode = _items[parentIndex];
                if (item.TotalCost > curNode.TotalCost)
                    break;

                Swap(item, curNode);

                if (parentIndex == 0)
                    break;

                parentIndex = (item.HeapIndex - 1) / 2;
            }
        }

        // Swaps two GridNodes in the heap.
        public static void Swap(PathPartition itemA, PathPartition itemB)
        {
            uint itemAIndex = itemA.HeapIndex;

            _items[itemAIndex] = itemB;
            _items[itemB.HeapIndex] = itemA;

            itemA.HeapIndex = itemB.HeapIndex;
            itemB.HeapIndex = itemAIndex;
        }
    }
}
