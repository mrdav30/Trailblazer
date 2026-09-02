//=======================================================================
// NavigationPageStampSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Provides fixed-capacity deterministic page-address membership.</summary>
internal sealed class NavigationPageStampSet
{
    private readonly string[] _mapIds;
    private readonly int[] _pageIndices;
    private readonly long[] _stamps;
    private readonly int _mask;
    private int _count;
    private long _generation = 1;

    internal NavigationPageStampSet(int capacity)
    {
        int required = checked(capacity * 2);
        int tableSize = 1;
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _mapIds = new string[tableSize];
        _pageIndices = new int[tableSize];
        _stamps = new long[tableSize];
        _mask = tableSize - 1;
    }

    internal void Reset()
    {
        unchecked
        {
            _generation++;
        }
        _count = 0;
    }

    internal bool Contains(string mapId, int pageIndex) =>
        Find(mapId, pageIndex, out _);

    internal bool Add(string mapId, int pageIndex)
    {
        if (Find(mapId, pageIndex, out int index))
            return false;
        _mapIds[index] = mapId;
        _pageIndices[index] = pageIndex;
        _stamps[index] = _generation;
        _count++;
        return true;
    }

    private bool Find(string mapId, int pageIndex, out int index)
    {
        int mapHash = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(mapId);
        index = SwiftHashTools.CombineHashCodes(mapHash, pageIndex) & _mask;
        while (_stamps[index] == _generation)
        {
            if (_pageIndices[index] == pageIndex
                && string.Equals(_mapIds[index], mapId, StringComparison.Ordinal))
            {
                return true;
            }
            index = (index + 1) & _mask;
        }
        return false;
    }
}
