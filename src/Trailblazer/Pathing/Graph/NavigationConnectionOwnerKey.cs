//=======================================================================
// NavigationConnectionOwnerKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one source-map-owned explicit connection.</summary>
internal readonly struct NavigationConnectionOwnerKey : IEquatable<NavigationConnectionOwnerKey>, IComparable<NavigationConnectionOwnerKey>
{
    internal NavigationConnectionOwnerKey(string mapId, string connectionId)
    {
        MapId = mapId;
        ConnectionId = connectionId;
    }

    internal string MapId { get; }

    internal string ConnectionId { get; }

    public int CompareTo(NavigationConnectionOwnerKey other)
    {
        int map = string.CompareOrdinal(MapId, other.MapId);
        return map != 0 ? map : string.CompareOrdinal(ConnectionId, other.ConnectionId);
    }

    public bool Equals(NavigationConnectionOwnerKey other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is NavigationConnectionOwnerKey other && Equals(other);

    public override int GetHashCode()
    {
        var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
        return SwiftHashTools.CombineHashCodes(
            comparer.GetHashCode(MapId),
            comparer.GetHashCode(ConnectionId));
    }
}
