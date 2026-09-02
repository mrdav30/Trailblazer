//=======================================================================
// NavigationConnectionOwnerKeySet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores a persistent canonical set of explicit-connection owner keys.</summary>
internal sealed class NavigationConnectionOwnerKeySet
{
    private readonly PersistentStringMap<PersistentStringMap<bool>> _maps;
    private readonly long _innerBytes;
    private readonly int _innerPages;

    private NavigationConnectionOwnerKeySet(
        PersistentStringMap<PersistentStringMap<bool>> maps,
        int count,
        long innerBytes,
        int innerPages)
    {
        _maps = maps;
        Count = count;
        _innerBytes = innerBytes;
        _innerPages = innerPages;
    }

    internal static NavigationConnectionOwnerKeySet Empty { get; } = new(
        PersistentStringMap<PersistentStringMap<bool>>.Empty,
        0,
        0,
        0);

    internal int Count { get; }

    internal long RetainedBytes => checked(40L + _maps.RetainedBytes + _innerBytes);

    internal int PersistentPageCount => checked(
        1 + _maps.PersistentNodeCount + _innerPages);

    internal NavigationConnectionOwnerKeySet Add(NavigationConnectionOwnerKey owner)
    {
        bool hadMap = _maps.TryGetValue(owner.MapId, out PersistentStringMap<bool> values);
        values ??= PersistentStringMap<bool>.Empty;
        if (values.ContainsKey(owner.ConnectionId))
            return this;
        long innerBytes = _innerBytes - (hadMap ? values.RetainedBytes : 0L);
        int innerPages = _innerPages - (hadMap ? values.PersistentNodeCount : 0);
        values = values.Set(owner.ConnectionId, true);
        return new NavigationConnectionOwnerKeySet(
            _maps.Set(owner.MapId, values),
            checked(Count + 1),
            checked(innerBytes + values.RetainedBytes),
            checked(innerPages + values.PersistentNodeCount));
    }

    internal NavigationConnectionOwnerKey GetAt(int ordinal)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)ordinal >= (uint)Count,
            ordinal,
            nameof(ordinal));
        int remaining = ordinal;
        for (int map = 0; ; map++)
        {
            PersistentStringMap<bool> values = _maps.GetValueAt(map);
            if (remaining < values.Count)
            {
                return new NavigationConnectionOwnerKey(
                    _maps.GetKeyAt(map),
                    values.GetKeyAt(remaining));
            }
            remaining -= values.Count;
        }
    }
}
