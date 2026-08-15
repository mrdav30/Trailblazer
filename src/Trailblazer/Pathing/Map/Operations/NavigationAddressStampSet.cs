//=======================================================================
// NavigationAddressStampSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Provides fixed-capacity address deduplication with constant-time logical reset.</summary>
internal sealed class NavigationAddressStampSet
{
    private readonly NavigationCellAddress[] _values;
    private readonly long[] _stamps;
    private readonly int _mask;
    private long _generation = 1;

    internal NavigationAddressStampSet(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        int required = checked(capacity * 2);
        int tableSize = 1;
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _values = new NavigationCellAddress[tableSize];
        _stamps = new long[tableSize];
        _mask = tableSize - 1;
    }

    internal void Reset()
    {
        if (_generation == long.MaxValue)
            throw new InvalidOperationException("Address stamp generation capacity is exhausted.");
        _generation++;
    }

    internal bool Add(NavigationCellAddress value)
    {
        int index = value.GetHashCode() & _mask;
        while (_stamps[index] == _generation)
        {
            if (_values[index].Equals(value))
                return false;
            index = (index + 1) & _mask;
        }
        _values[index] = value;
        _stamps[index] = _generation;
        return true;
    }

    internal bool Contains(NavigationCellAddress value)
    {
        int index = value.GetHashCode() & _mask;
        while (_stamps[index] == _generation)
        {
            if (_values[index].Equals(value))
                return true;
            index = (index + 1) & _mask;
        }
        return false;
    }
}
