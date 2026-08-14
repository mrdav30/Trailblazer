//=======================================================================
// NavigationStructuralNode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Stores one map's sorted effective outgoing structural dependencies.</summary>
internal sealed class NavigationStructuralNode
{
    private readonly NavigationStructuralLink[] _links;

    internal NavigationStructuralNode(NavigationStructuralLink[] links) => _links = links;

    internal ReadOnlySpan<NavigationStructuralLink> Links => _links;

    internal long RetainedBytes => checked(32L + ((long)_links.Length * 16L));

    internal static NavigationStructuralNode Capture(
        NavigationMapInstance instance,
        NavigationExplicitConnectionIndex explicitConnections)
    {
        var destinations = new SwiftList<string>();
        int ownerCount = explicitConnections.GetSourceOwnerCount(instance.MapId);
        for (int i = 0; i < ownerCount; i++)
        {
            NavigationExplicitConnectionRecord record =
                explicitConnections.GetSourceOwnerAt(instance.MapId, i);
            if (record.IsActive)
                destinations.Add(record.Destination.MapId);
        }

        ReadOnlySpan<TraversalTransitionDefinition> transitions = instance.Map.TransitionSpan;
        for (int i = 0; i < transitions.Length; i++)
        {
            if (!instance.Overlay.TryGetTransition(transitions[i].Id, out _))
                destinations.Add(transitions[i].Destination.MapId);
        }

        for (int i = 0; i < instance.Overlay.TransitionCount; i++)
        {
            TraversalTransitionOverlayOperation operation = instance.Overlay.GetTransitionAt(i);
            if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert)
                destinations.Add(operation.Transition.Destination.MapId);
        }

        string[] values = destinations.ToArray();
        if (values.Length == 0)
            return new NavigationStructuralNode(Array.Empty<NavigationStructuralLink>());

        Array.Sort(values, StringComparer.Ordinal);
        int uniqueCount = 1;
        for (int i = 1; i < values.Length; i++)
        {
            if (!string.Equals(values[i - 1], values[i], StringComparison.Ordinal))
                uniqueCount++;
        }

        var links = new NavigationStructuralLink[uniqueCount];
        int linkIndex = 0;
        int start = 0;
        while (start < values.Length)
        {
            int end = start + 1;
            while (end < values.Length
                && string.Equals(values[start], values[end], StringComparison.Ordinal))
            {
                end++;
            }
            links[linkIndex++] = new NavigationStructuralLink(values[start], end - start);
            start = end;
        }
        return new NavigationStructuralNode(links);
    }

    internal bool HasSameLinks(NavigationStructuralNode other)
    {
        if (_links.Length != other._links.Length)
            return false;
        for (int i = 0; i < _links.Length; i++)
        {
            if (!_links[i].Equals(other._links[i]))
                return false;
        }
        return true;
    }

    internal int GetLinkCount(string destinationMapId)
    {
        int low = 0;
        int high = _links.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(_links[middle].DestinationMapId, destinationMapId);
            if (comparison == 0)
                return _links[middle].Count;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return 0;
    }

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
