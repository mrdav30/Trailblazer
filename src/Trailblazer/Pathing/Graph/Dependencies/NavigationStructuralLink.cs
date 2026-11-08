//=======================================================================
// NavigationStructuralLink.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores one unique directed map dependency and its parallel edge count.</summary>
internal readonly struct NavigationStructuralLink : IEquatable<NavigationStructuralLink>
{
    internal NavigationStructuralLink(
        string destinationMapId,
        int count,
        int uncertifiedCount)
    {
        DestinationMapId = destinationMapId;
        Count = count;
        UncertifiedCount = uncertifiedCount;
    }

    internal string DestinationMapId { get; }

    internal int Count { get; }

    internal int UncertifiedCount { get; }

    public bool Equals(NavigationStructuralLink other) =>
        Count == other.Count
        && UncertifiedCount == other.UncertifiedCount
        && string.Equals(DestinationMapId, other.DestinationMapId, StringComparison.Ordinal);
}
