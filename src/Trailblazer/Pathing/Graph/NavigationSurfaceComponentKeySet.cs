//=======================================================================
// NavigationSurfaceComponentKeySet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores a persistent canonical set of exact surface-component keys.</summary>
internal sealed class NavigationSurfaceComponentKeySet
{
    private readonly PersistentStringMap<PersistentVoxelIndexMap<byte>> _maps;
    private readonly long _innerBytes;
    private readonly int _innerPages;

    private NavigationSurfaceComponentKeySet(
        PersistentStringMap<PersistentVoxelIndexMap<byte>> maps,
        int count,
        long innerBytes,
        int innerPages)
    {
        _maps = maps;
        Count = count;
        _innerBytes = innerBytes;
        _innerPages = innerPages;
    }

    internal static NavigationSurfaceComponentKeySet Empty { get; } = new(
        PersistentStringMap<PersistentVoxelIndexMap<byte>>.Empty,
        0,
        0,
        0);

    internal int Count { get; }

    internal long RetainedBytes => checked(40L + _maps.RetainedBytes + _innerBytes);

    internal int PersistentPageCount => checked(
        1 + _maps.PersistentNodeCount + _innerPages);

    internal bool Contains(NavigationSurfaceComponentKey key) =>
        _maps.TryGetValue(
            key.Representative.MapId,
            out PersistentVoxelIndexMap<byte> values)
        && values.TryGetValue(key.Representative.Index, out _);

    internal NavigationSurfaceComponentKeySet Add(NavigationSurfaceComponentKey key)
    {
        string mapId = key.Representative.MapId;
        bool hadMap = _maps.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<byte> values);
        values ??= PersistentVoxelIndexMap<byte>.Empty;
        if (values.TryGetValue(key.Representative.Index, out _))
            return this;
        long innerBytes = _innerBytes - (hadMap ? values.RetainedBytes : 0L);
        int innerPages = _innerPages - (hadMap ? values.PersistentNodeCount : 0);
        values = values.Set(key.Representative.Index, 1);
        return new NavigationSurfaceComponentKeySet(
            _maps.Set(mapId, values),
            checked(Count + 1),
            checked(innerBytes + values.RetainedBytes),
            checked(innerPages + values.PersistentNodeCount));
    }

    internal NavigationSurfaceComponentKey GetAt(int ordinal)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)ordinal >= (uint)Count,
            ordinal,
            nameof(ordinal));
        int remaining = ordinal;
        for (int map = 0; map < _maps.Count; map++)
        {
            PersistentVoxelIndexMap<byte> values = _maps.GetValueAt(map);
            if (remaining < values.Count)
            {
                return new NavigationSurfaceComponentKey(
                    new NavigationCellAddress(
                        _maps.GetKeyAt(map),
                        values.GetKeyAt(remaining)));
            }
            remaining -= values.Count;
        }
        return default;
    }
}
