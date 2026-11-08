//=======================================================================
// NavigationTransitionOwnerKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one source-map-owned explicit transition.</summary>
internal readonly struct NavigationTransitionOwnerKey : IEquatable<NavigationTransitionOwnerKey>
{
    internal NavigationTransitionOwnerKey(string mapId, string transitionId)
    {
        MapId = mapId;
        TransitionId = transitionId;
    }

    internal string MapId { get; }

    internal string TransitionId { get; }

    public bool Equals(NavigationTransitionOwnerKey other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && string.Equals(TransitionId, other.TransitionId, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
        return SwiftHashTools.CombineHashCodes(
            comparer.GetHashCode(MapId),
            comparer.GetHashCode(TransitionId));
    }
}
