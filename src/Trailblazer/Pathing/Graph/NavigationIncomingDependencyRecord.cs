//=======================================================================
// NavigationIncomingDependencyRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores sorted source maps that structurally reference one destination map ID.</summary>
internal sealed class NavigationIncomingDependencyRecord
{
    private readonly NavigationIncomingDependency[] _sources;

    private NavigationIncomingDependencyRecord(NavigationIncomingDependency[] sources) =>
        _sources = sources;

    internal static NavigationIncomingDependencyRecord Empty { get; } =
        new(Array.Empty<NavigationIncomingDependency>());

    internal ReadOnlySpan<NavigationIncomingDependency> Sources => _sources;

    internal bool IsEmpty => _sources.Length == 0;

    internal long RetainedBytes => checked(32L + ((long)_sources.Length * 16L));

    internal NavigationIncomingDependencyRecord With(string sourceMapId, int count)
    {
        int index = Find(sourceMapId);
        if (index >= 0 && _sources[index].Count == count)
            return this;
        if (index < 0 && count == 0)
            return this;

        int insertion = index >= 0 ? index : ~index;
        int nextLength = _sources.Length + (index < 0 ? 1 : count == 0 ? -1 : 0);
        if (nextLength == 0)
            return Empty;

        var next = new NavigationIncomingDependency[nextLength];
        if (insertion > 0)
            Array.Copy(_sources, 0, next, 0, insertion);
        if (count != 0)
        {
            next[insertion] = new NavigationIncomingDependency(sourceMapId, count);
            int sourceOffset = index >= 0 ? insertion + 1 : insertion;
            int destinationOffset = insertion + 1;
            if (sourceOffset < _sources.Length)
            {
                Array.Copy(
                    _sources,
                    sourceOffset,
                    next,
                    destinationOffset,
                    _sources.Length - sourceOffset);
            }
        }
        else if (insertion + 1 < _sources.Length)
        {
            Array.Copy(
                _sources,
                insertion + 1,
                next,
                insertion,
                _sources.Length - insertion - 1);
        }
        return new NavigationIncomingDependencyRecord(next);
    }

    private int Find(string sourceMapId)
    {
        int low = 0;
        int high = _sources.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(_sources[middle].SourceMapId, sourceMapId);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return ~low;
    }
}

/// <summary>Stores one incoming source and its parallel directed edge count.</summary>
internal readonly struct NavigationIncomingDependency
{
    internal NavigationIncomingDependency(string sourceMapId, int count)
    {
        SourceMapId = sourceMapId;
        Count = count;
    }

    internal string SourceMapId { get; }

    internal int Count { get; }
}
