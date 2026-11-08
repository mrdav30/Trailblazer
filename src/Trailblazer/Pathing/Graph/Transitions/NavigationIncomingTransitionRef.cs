//=======================================================================
// NavigationIncomingTransitionRef.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Stores the lightweight destination-side reference to one source-owned transition.</summary>
internal readonly struct NavigationIncomingTransitionRef : IEquatable<NavigationIncomingTransitionRef>
{
    internal NavigationIncomingTransitionRef(NavigationPublishedTransition transition)
    {
        Owner = transition.Owner;
        SourcePageIndex = transition.SourcePageIndex;
        SourceIndex = transition.Definition.SourceIndex;
        SourceMedium = transition.Definition.SourceMedium;
        DestinationMedium = transition.Definition.DestinationMedium;
        Type = transition.Definition.Type;
    }

    internal NavigationTransitionOwnerKey Owner { get; }

    internal int SourcePageIndex { get; }

    internal VoxelIndex SourceIndex { get; }

    internal NavigationTransitionPageAddress SourcePage => new(
        Owner.MapId,
        SourcePageIndex);

    internal NavigationCellAddress SourceAddress => new(Owner.MapId, SourceIndex);

    internal TraversalMedium SourceMedium { get; }

    internal TraversalMedium DestinationMedium { get; }

    internal TraversalTransitionType Type { get; }

    public bool Equals(NavigationIncomingTransitionRef other) =>
        Owner.Equals(other.Owner)
        && SourcePageIndex == other.SourcePageIndex
        && SourceIndex.Equals(other.SourceIndex)
        && SourceMedium == other.SourceMedium
        && DestinationMedium == other.DestinationMedium
        && Type == other.Type;

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Owner.GetHashCode(), SourceIndex.GetHashCode());
}
