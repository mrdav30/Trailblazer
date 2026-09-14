//=======================================================================
// NavigationComponentStampSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Deduplicates exact address-medium component keys within a dependency workspace.</summary>
internal sealed class NavigationComponentStampSet
{
    private readonly NavigationSurfaceComponentKey[] _values;
    private readonly long[] _stamps;
    private readonly int _mask;
    private long _generation = 1;

    internal NavigationComponentStampSet(int capacity)
    {
        int required = checked(capacity * 2);
        int tableSize = 1;
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _values = new NavigationSurfaceComponentKey[tableSize];
        _stamps = new long[tableSize];
        _mask = tableSize - 1;
    }

    internal void Reset() => _generation = NavigationGenerationCounter.Advance(
        _generation,
        "Component stamp generation capacity is exhausted.");

    internal bool Contains(NavigationSurfaceComponentKey value) => Find(value, out _);

    internal bool Add(NavigationSurfaceComponentKey value)
    {
        if (Find(value, out int index))
            return false;
        _values[index] = value;
        _stamps[index] = _generation;
        return true;
    }

    private bool Find(NavigationSurfaceComponentKey value, out int index)
    {
        index = value.GetHashCode() & _mask;
        while (_stamps[index] == _generation)
        {
            if (_values[index].Equals(value))
                return true;
            index = (index + 1) & _mask;
        }
        return false;
    }
}
