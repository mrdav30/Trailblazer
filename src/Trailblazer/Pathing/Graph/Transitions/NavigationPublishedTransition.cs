//=======================================================================
// NavigationPublishedTransition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Stores one complete source-owned explicit transition record.</summary>
internal readonly struct NavigationPublishedTransition : IEquatable<NavigationPublishedTransition>
{
    internal NavigationPublishedTransition(
        string ownerMapId,
        TraversalTransitionDefinition definition,
        NavigationTransitionPageAddress sourcePage,
        NavigationTransitionPageAddress? destinationPage)
    {
        Owner = new NavigationTransitionOwnerKey(ownerMapId, definition.Id);
        Definition = definition;
        SourcePageIndex = sourcePage.PageIndex;
        HasDestinationPage = destinationPage.HasValue;
        DestinationPageIndex = destinationPage?.PageIndex ?? -1;
    }

    internal NavigationTransitionOwnerKey Owner { get; }

    internal TraversalTransitionDefinition Definition { get; }

    internal int SourcePageIndex { get; }

    internal bool HasDestinationPage { get; }

    internal int DestinationPageIndex { get; }

    internal NavigationTransitionPageAddress SourcePage => new(
        Owner.MapId,
        SourcePageIndex);

    internal NavigationTransitionPageAddress? DestinationPage => HasDestinationPage
        ? new NavigationTransitionPageAddress(
            Definition.Destination.MapId,
            DestinationPageIndex)
        : null;

    internal NavigationCellAddress SourceAddress => new(Owner.MapId, Definition.SourceIndex);

    public bool Equals(NavigationPublishedTransition other) =>
        Owner.Equals(other.Owner)
        && Definition.Equals(other.Definition)
        && SourcePageIndex == other.SourcePageIndex
        && HasDestinationPage == other.HasDestinationPage
        && DestinationPageIndex == other.DestinationPageIndex;

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Owner.GetHashCode(), Definition.GetHashCode());
}
