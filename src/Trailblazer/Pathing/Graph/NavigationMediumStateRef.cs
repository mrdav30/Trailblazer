//=======================================================================
// NavigationMediumStateRef.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one traversal-medium state over an immutable physical node.</summary>
internal readonly struct NavigationMediumStateRef :
    IEquatable<NavigationMediumStateRef>,
    IComparable<NavigationMediumStateRef>
{
    internal NavigationMediumStateRef(NavigationNodeRef node, TraversalMedium medium)
    {
        Node = node;
        Medium = medium;
    }

    internal NavigationNodeRef Node { get; }

    internal TraversalMedium Medium { get; }

    internal bool IsValid => Node.IsValid && NavigationCell.IsKnownMedium(Medium);

    public int CompareTo(NavigationMediumStateRef other)
    {
        int comparison = Node.MapOrdinal.CompareTo(other.Node.MapOrdinal);
        if (comparison != 0)
            return comparison;
        comparison = Node.CellSlot.CompareTo(other.Node.CellSlot);
        if (comparison != 0)
            return comparison;
        int medium = (int)Medium;
        int otherMedium = (int)other.Medium;
        return medium < otherMedium ? -1 : medium > otherMedium ? 1 : 0;
    }

    public bool Equals(NavigationMediumStateRef other) =>
        Node.Equals(other.Node) && Medium == other.Medium;

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Node.GetHashCode(), (int)Medium);

}
