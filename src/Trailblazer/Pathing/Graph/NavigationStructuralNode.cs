//=======================================================================
// NavigationStructuralNode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores one map's sorted effective outgoing structural dependencies.</summary>
internal sealed class NavigationStructuralNode
{
    private readonly NavigationPagedSequence<NavigationStructuralLink> _links;

    internal NavigationStructuralNode(NavigationPagedSequence<NavigationStructuralLink> links) =>
        _links = links;

    internal int LinkCount => _links.Count;

    internal NavigationPagedSequence<NavigationStructuralLink>.Enumerator GetLinkEnumerator() =>
        _links.GetEnumerator();

    internal long RetainedBytes => checked(24L + _links.RetainedBytes);

    internal int PersistentPageCount => _links.PersistentPageCount;

}

/// <summary>Stores one unique directed map dependency and its parallel edge count.</summary>
internal readonly struct NavigationStructuralLink : IEquatable<NavigationStructuralLink>
{
    internal NavigationStructuralLink(string destinationMapId, int count)
    {
        DestinationMapId = destinationMapId;
        Count = count;
    }

    internal string DestinationMapId { get; }

    internal int Count { get; }

    public bool Equals(NavigationStructuralLink other) =>
        Count == other.Count
        && string.Equals(DestinationMapId, other.DestinationMapId, StringComparison.Ordinal);
}
