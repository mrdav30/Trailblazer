//=======================================================================
// NavigationSurfaceComponentKeySet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores a persistent canonical set of exact address-medium keys.</summary>
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
        && values.TryGetValue(key.Representative.Index, out byte mask)
        && (mask & NavigationMediumSlots<byte>.GetBit(key.Medium)) != 0;

    internal NavigationSurfaceComponentKeySet Add(NavigationSurfaceComponentKey key)
    {
        string mapId = key.Representative.MapId;
        bool hadMap = _maps.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<byte> values);
        values ??= PersistentVoxelIndexMap<byte>.Empty;
        byte bit = NavigationMediumSlots<byte>.GetBit(key.Medium);
        values.TryGetValue(key.Representative.Index, out byte mask);
        if ((mask & bit) != 0)
            return this;
        long innerBytes = _innerBytes - (hadMap ? values.RetainedBytes : 0L);
        int innerPages = _innerPages - (hadMap ? values.PersistentNodeCount : 0);
        values = values.Set(key.Representative.Index, (byte)(mask | bit));
        return new NavigationSurfaceComponentKeySet(
            _maps.Set(mapId, values),
            checked(Count + 1),
            checked(innerBytes + values.RetainedBytes),
            checked(innerPages + values.PersistentNodeCount));
    }

    internal NavigationSurfaceComponentKeySet Remove(NavigationSurfaceComponentKey key)
    {
        string mapId = key.Representative.MapId;
        if (!_maps.TryGetValue(mapId, out PersistentVoxelIndexMap<byte> values)
            || !values.TryGetValue(key.Representative.Index, out byte mask))
        {
            return this;
        }
        byte bit = NavigationMediumSlots<byte>.GetBit(key.Medium);
        if ((mask & bit) == 0)
            return this;
        long innerBytes = _innerBytes - values.RetainedBytes;
        int innerPages = _innerPages - values.PersistentNodeCount;
        byte nextMask = (byte)(mask & ~bit);
        values = nextMask == 0
            ? values.Remove(key.Representative.Index, out _)
            : values.Set(key.Representative.Index, nextMask);
        PersistentStringMap<PersistentVoxelIndexMap<byte>> maps = values.Count == 0
            ? _maps.Remove(mapId, out _)
            : _maps.Set(mapId, values);
        return new NavigationSurfaceComponentKeySet(
            maps,
            Count - 1,
            checked(innerBytes + (values.Count == 0 ? 0L : values.RetainedBytes)),
            checked(innerPages + (values.Count == 0 ? 0 : values.PersistentNodeCount)));
    }

    internal Enumerator GetEnumerator() => new(this);

    internal struct Enumerator
    {
        private readonly NavigationSurfaceComponentKeySet _set;
        private int _map;
        private int _address;
        private TraversalMedium _medium;
        private TraversalMedium _currentMedium;

        internal Enumerator(NavigationSurfaceComponentKeySet set)
        {
            _set = set;
            _map = 0;
            _address = 0;
            _medium = TraversalMedium.Solid;
            _currentMedium = TraversalMedium.Unknown;
        }

        internal NavigationSurfaceComponentKey Current
        {
            get
            {
                PersistentVoxelIndexMap<byte> values = _set._maps.GetValueAt(_map);
                return new NavigationSurfaceComponentKey(
                    new NavigationCellAddress(
                        _set._maps.GetKeyAt(_map),
                        values.GetKeyAt(_address)),
                    _currentMedium);
            }
        }

        internal bool MoveNext()
        {
            while (_map < _set._maps.Count)
            {
                PersistentVoxelIndexMap<byte> values = _set._maps.GetValueAt(_map);
                while (_address < values.Count)
                {
                    byte mask = values.GetValueAt(_address);
                    while (_medium <= TraversalMedium.Liquid)
                    {
                        TraversalMedium medium = _medium++;
                        if ((mask & NavigationMediumSlots<byte>.GetBit(medium)) == 0)
                            continue;
                        _currentMedium = medium;
                        return true;
                    }
                    _address++;
                    _medium = TraversalMedium.Solid;
                }
                _map++;
                _address = 0;
            }
            return false;
        }
    }
}
