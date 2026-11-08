//=======================================================================
// NavigationFlowPayloadSortWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Canonically heap-sorts flow nodes or lookup ordinals with bounded comparisons.</summary>
internal struct NavigationFlowPayloadSortWork
{
    private enum SiftStage : byte
    {
        None = 0,
        ChooseChild = 1,
        CompareRoot = 2
    }

    private readonly NavigationFlowFieldNode[] _nodes;
    private readonly int[] _lookup;
    private readonly bool _sortNodes;
    private SiftStage _siftStage;
    private int _heapSize;
    private int _buildIndex;
    private int _sortEnd;
    private int _siftRoot;
    private int _siftCandidate;
    private bool _building;

    internal NavigationFlowPayloadSortWork(
        NavigationFlowFieldNode[] nodes,
        int[] lookup,
        bool sortNodes)
    {
        _nodes = nodes;
        _lookup = lookup;
        _sortNodes = sortNodes;
        _siftStage = SiftStage.None;
        _heapSize = nodes.Length;
        _buildIndex = (nodes.Length / 2) - 1;
        _sortEnd = nodes.Length - 1;
        _siftRoot = 0;
        _siftCandidate = 0;
        _building = true;
    }

    internal bool Advance(NavigationWorkMeter meter, int lookupStepLimit)
    {
        int remaining = Math.Min(lookupStepLimit, meter.RemainingLookupProbes);
        while (true)
        {
            if (_siftStage == SiftStage.None)
            {
                if (_building)
                {
                    if (_buildIndex < 0)
                    {
                        _building = false;
                        continue;
                    }
                    BeginSift(_buildIndex--);
                    continue;
                }
                if (_sortEnd <= 0)
                    return true;
                Swap(0, _sortEnd);
                _heapSize = _sortEnd--;
                BeginSift(0);
                continue;
            }
            if (_siftStage == SiftStage.ChooseChild)
            {
                int left = checked((_siftRoot * 2) + 1);
                if (left >= _heapSize)
                {
                    _siftStage = SiftStage.None;
                    continue;
                }
                _siftCandidate = left;
                int right = left + 1;
                if (right < _heapSize)
                {
                    if (!TryConsumeComparison(meter, ref remaining))
                        return false;
                    if (Compare(left, right) < 0)
                        _siftCandidate = right;
                }
                _siftStage = SiftStage.CompareRoot;
                continue;
            }
            if (!TryConsumeComparison(meter, ref remaining))
                return false;
            if (Compare(_siftRoot, _siftCandidate) >= 0)
            {
                _siftStage = SiftStage.None;
                continue;
            }
            Swap(_siftRoot, _siftCandidate);
            _siftRoot = _siftCandidate;
            _siftStage = SiftStage.ChooseChild;
        }
    }

    private void BeginSift(int root)
    {
        _siftRoot = root;
        _siftStage = SiftStage.ChooseChild;
    }

    private int Compare(int left, int right)
    {
        if (!_sortNodes)
        {
            NavigationFlowFieldNode lookupLeft = _nodes[_lookup[left]];
            NavigationFlowFieldNode lookupRight = _nodes[_lookup[right]];
            int lookupComparison = lookupLeft.Address.CompareTo(lookupRight.Address);
            return lookupComparison != 0
                ? lookupComparison
                : ((int)lookupLeft.Medium).CompareTo((int)lookupRight.Medium);
        }
        NavigationFlowFieldNode leftNode = _nodes[left];
        NavigationFlowFieldNode rightNode = _nodes[right];
        int comparison = leftNode.IntegrationCost.CompareTo(
            rightNode.IntegrationCost);
        if (comparison != 0)
            return comparison;
        comparison = leftNode.Address.CompareTo(rightNode.Address);
        return comparison != 0
            ? comparison
            : ((int)leftNode.Medium).CompareTo((int)rightNode.Medium);
    }

    private void Swap(int left, int right)
    {
        if (_sortNodes)
        {
            (_nodes[left], _nodes[right]) = (_nodes[right], _nodes[left]);
            return;
        }
        (_lookup[left], _lookup[right]) = (_lookup[right], _lookup[left]);
    }

    private static bool TryConsumeComparison(
        NavigationWorkMeter meter,
        ref int remaining)
    {
        if (remaining == 0 || !meter.TryConsumeLookupProbes(1))
            return false;
        remaining--;
        return true;
    }
}
