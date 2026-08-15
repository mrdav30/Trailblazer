//=======================================================================
// NavigationDependencySortWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Canonically sorts unique endpoint dependencies with bounded comparisons.</summary>
internal sealed class NavigationDependencySortWork
{
    private enum Collection : byte
    {
        Components = 0,
        Pages = 1,
        Complete = 2
    }

    private enum SiftStage : byte
    {
        None = 0,
        ChooseChild = 1,
        CompareRoot = 2
    }

    private readonly NavigationAStarWorkspace _workspace;
    private Collection _collection;
    private SiftStage _siftStage;
    private int _heapSize;
    private int _buildIndex;
    private int _sortEnd;
    private int _siftRoot;
    private int _siftCandidate;
    private bool _building;

    internal NavigationDependencySortWork(NavigationAStarWorkspace workspace)
    {
        _workspace = workspace;
        InitializeCollection();
    }

    internal bool IsComplete => _collection == Collection.Complete;

    internal bool Advance(NavigationWorkMeter meter, int lookupStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        int remaining = Math.Min(lookupStepLimit, meter.RemainingLookupProbes);
        while (!IsComplete)
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
                {
                    _collection = _collection == Collection.Components
                        ? Collection.Pages
                        : Collection.Complete;
                    InitializeCollection();
                    continue;
                }
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
        return true;
    }

    private void InitializeCollection()
    {
        if (IsComplete)
            return;
        int count = _collection == Collection.Components
            ? _workspace.EndpointComponentCount
            : _workspace.EndpointPageCount;
        _heapSize = count;
        _buildIndex = (count / 2) - 1;
        _sortEnd = count - 1;
        _building = true;
        _siftStage = SiftStage.None;
    }

    private void BeginSift(int root)
    {
        _siftRoot = root;
        _siftStage = SiftStage.ChooseChild;
    }

    private int Compare(int left, int right)
    {
        if (_collection == Collection.Components)
        {
            return string.CompareOrdinal(
                _workspace.EndpointComponents[left],
                _workspace.EndpointComponents[right]);
        }
        GraphPageDependencyAddress leftPage = _workspace.EndpointPages[left];
        GraphPageDependencyAddress rightPage = _workspace.EndpointPages[right];
        int mapComparison = string.CompareOrdinal(leftPage.MapId, rightPage.MapId);
        return mapComparison != 0
            ? mapComparison
            : leftPage.PageIndex.CompareTo(rightPage.PageIndex);
    }

    private void Swap(int left, int right)
    {
        if (left == right)
            return;
        if (_collection == Collection.Components)
        {
            (_workspace.EndpointComponents[left], _workspace.EndpointComponents[right]) =
                (_workspace.EndpointComponents[right], _workspace.EndpointComponents[left]);
            return;
        }
        (_workspace.EndpointPages[left], _workspace.EndpointPages[right]) =
            (_workspace.EndpointPages[right], _workspace.EndpointPages[left]);
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
