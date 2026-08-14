//=======================================================================
// NavigationIncomingDependencyRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores sorted source maps that structurally reference one destination map ID.</summary>
internal sealed class NavigationIncomingDependencyRecord
{
    private readonly PersistentStringMap<int> _sources;

    internal NavigationIncomingDependencyRecord(PersistentStringMap<int> sources) =>
        _sources = sources;

    internal int Count => _sources.Count;

    internal NavigationIncomingDependency GetAt(int ordinal) => new(
        _sources.GetKeyAt(ordinal),
        _sources.GetValueAt(ordinal));

    internal long RetainedBytes => checked(24L + _sources.RetainedBytes);

    internal int PersistentPageCount => checked(1 + _sources.PersistentNodeCount);
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
