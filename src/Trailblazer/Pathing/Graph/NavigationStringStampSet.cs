//=======================================================================
// NavigationStringStampSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Provides fixed-capacity deterministic string membership with constant-time reset.</summary>
internal sealed class NavigationStringStampSet
{
    private const long BaseRetainedBytes = 96L;

    private readonly string[] _values;
    private readonly long[] _stamps;
    private readonly int _mask;
    private readonly int _capacity;
    private int _count;
    private long _generation = 1;

    internal NavigationStringStampSet(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        int required = checked(capacity * 2);
        int tableSize = 1;
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _values = new string[tableSize];
        _stamps = new long[tableSize];
        _mask = tableSize - 1;
        _capacity = capacity;
    }

    internal long RetainedBytes => checked(BaseRetainedBytes + ((long)_values.Length * 16L));

    internal static long GetRetainedBytes(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        int required = checked(capacity * 2);
        int tableSize = 1;
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        return checked(BaseRetainedBytes + ((long)tableSize * 16L));
    }

    internal void Reset()
    {
        if (_generation == long.MaxValue)
            throw new InvalidOperationException("String stamp generation capacity is exhausted.");
        _generation++;
        _count = 0;
    }

    internal bool Add(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        int index = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(value) & _mask;
        while (_stamps[index] == _generation)
        {
            if (string.Equals(_values[index], value, StringComparison.Ordinal))
                return false;
            index = (index + 1) & _mask;
        }
        if (_count == _capacity)
            throw new InvalidOperationException("String stamp set capacity is exhausted.");
        _values[index] = value;
        _stamps[index] = _generation;
        _count++;
        return true;
    }

    internal bool Contains(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        int index = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(value) & _mask;
        while (_stamps[index] == _generation)
        {
            if (string.Equals(_values[index], value, StringComparison.Ordinal))
                return true;
            index = (index + 1) & _mask;
        }
        return false;
    }
}
