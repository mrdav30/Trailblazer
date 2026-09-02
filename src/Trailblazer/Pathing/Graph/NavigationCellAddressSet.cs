//=======================================================================
// NavigationCellAddressSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores a persistent canonical set of exact navigation-cell addresses.</summary>
internal sealed class NavigationCellAddressSet
{
    private readonly PersistentStringMap<PersistentVoxelIndexMap<byte>> _maps;
    private readonly long _innerBytes;
    private readonly int _innerPages;

    private NavigationCellAddressSet(
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

    internal static NavigationCellAddressSet Empty { get; } = new(
        PersistentStringMap<PersistentVoxelIndexMap<byte>>.Empty,
        0,
        0,
        0);

    internal int Count { get; }

    internal long RetainedBytes => checked(40L + _maps.RetainedBytes + _innerBytes);

    internal int PersistentPageCount => checked(
        1 + _maps.PersistentNodeCount + _innerPages);

    internal bool Contains(NavigationCellAddress address) =>
        _maps.TryGetValue(address.MapId, out PersistentVoxelIndexMap<byte> values)
        && values.TryGetValue(address.Index, out _);

    internal NavigationCellAddressSet Add(NavigationCellAddress address)
    {
        bool hadMap = _maps.TryGetValue(
            address.MapId,
            out PersistentVoxelIndexMap<byte> values);
        values ??= PersistentVoxelIndexMap<byte>.Empty;
        if (values.TryGetValue(address.Index, out _))
            return this;
        long innerBytes = _innerBytes - (hadMap ? values.RetainedBytes : 0L);
        int innerPages = _innerPages - (hadMap ? values.PersistentNodeCount : 0);
        values = values.Set(address.Index, 1);
        return new NavigationCellAddressSet(
            _maps.Set(address.MapId, values),
            checked(Count + 1),
            checked(innerBytes + values.RetainedBytes),
            checked(innerPages + values.PersistentNodeCount));
    }

    internal NavigationCellAddress GetAt(int ordinal)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)ordinal >= (uint)Count,
            ordinal,
            nameof(ordinal));
        int remaining = ordinal;
        for (int map = 0; ; map++)
        {
            PersistentVoxelIndexMap<byte> values = _maps.GetValueAt(map);
            if (remaining < values.Count)
            {
                return new NavigationCellAddress(
                    _maps.GetKeyAt(map),
                    values.GetKeyAt(remaining));
            }
            remaining -= values.Count;
        }
    }
}
