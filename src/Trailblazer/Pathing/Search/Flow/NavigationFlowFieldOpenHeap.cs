//=======================================================================
// NavigationFlowFieldOpenHeap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Orders flow nodes by exact integration cost and stable address.</summary>
internal struct NavigationFlowFieldOpenHeap
{
    private readonly NavigationFlowFieldWorkspace _workspace;

    internal NavigationFlowFieldOpenHeap(NavigationFlowFieldWorkspace workspace) =>
        _workspace = workspace;

    internal bool TryPeek(out int slot)
    {
        if (_workspace.HeapCount == 0)
        {
            slot = -1;
            return false;
        }
        slot = _workspace.HeapSlots[0];
        return true;
    }

    internal void Push(int slot)
    {
        int index = _workspace.HeapCount++;
        _workspace.HeapSlots[index] = slot;
        _workspace.GetRecord(slot).HeapIndex = index;
        SortUp(index);
    }

    internal int Pop()
    {
        int result = _workspace.HeapSlots[0];
        _workspace.GetRecord(result).HeapIndex = -1;
        int last = --_workspace.HeapCount;
        if (last > 0)
        {
            int moved = _workspace.HeapSlots[last];
            _workspace.HeapSlots[0] = moved;
            _workspace.GetRecord(moved).HeapIndex = 0;
            SortDown(0);
        }
        return result;
    }

    internal void DecreaseKey(int slot) =>
        SortUp(_workspace.GetRecord(slot).HeapIndex);

    private void SortUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (Compare(index, parent) >= 0)
                return;
            Swap(index, parent);
            index = parent;
        }
    }

    private void SortDown(int index)
    {
        while (true)
        {
            int left = checked((index * 2) + 1);
            if (left >= _workspace.HeapCount)
                return;
            int right = left + 1;
            int best = right < _workspace.HeapCount && Compare(right, left) < 0
                ? right
                : left;
            if (Compare(best, index) >= 0)
                return;
            Swap(index, best);
            index = best;
        }
    }

    private int Compare(int leftIndex, int rightIndex)
    {
        ref NavigationFlowFieldSearchNode left = ref _workspace.GetRecord(
            _workspace.HeapSlots[leftIndex]);
        ref NavigationFlowFieldSearchNode right = ref _workspace.GetRecord(
            _workspace.HeapSlots[rightIndex]);
        int comparison = left.IntegrationCost.CompareTo(right.IntegrationCost);
        if (comparison != 0)
            return comparison;
        comparison = left.Address.CompareTo(right.Address);
        return comparison != 0
            ? comparison
            : _workspace.GetNode(_workspace.HeapSlots[leftIndex]).Medium.CompareTo(
                _workspace.GetNode(_workspace.HeapSlots[rightIndex]).Medium);
    }

    private void Swap(int left, int right)
    {
        int leftSlot = _workspace.HeapSlots[left];
        int rightSlot = _workspace.HeapSlots[right];
        _workspace.HeapSlots[left] = rightSlot;
        _workspace.HeapSlots[right] = leftSlot;
        _workspace.GetRecord(rightSlot).HeapIndex = left;
        _workspace.GetRecord(leftSlot).HeapIndex = right;
    }
}
