//=======================================================================
// NavigationExplicitConnectionRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Stores one selected explicit definition and its immutable corridor certificate.</summary>
internal sealed class NavigationExplicitConnectionRecord
{
    private const long BaseRetainedBytes = 104L;
    private readonly NavigationPagedSequence<GridNavigationPortal> _navigationPortals;

    internal NavigationExplicitConnectionRecord(
        NavigationConnectionOwnerKey owner,
        NavigationConnection definition,
        bool isActive,
        Fixed64 corridorCost,
        NavigationPagedSequence<GridNavigationPortal> navigationPortals,
        bool isLowerBoundCertified = false)
    {
        Owner = owner;
        Definition = definition;
        Source = new NavigationCellAddress(owner.MapId, definition.SourceIndex);
        Destination = definition.Destination;
        IsActive = isActive;
        CorridorCost = corridorCost;
        _navigationPortals = navigationPortals;
        IsLowerBoundCertified = isLowerBoundCertified;
    }

    internal NavigationConnectionOwnerKey Owner { get; }

    internal NavigationConnection Definition { get; }

    internal NavigationCellAddress Source { get; }

    internal NavigationCellAddress Destination { get; }

    internal bool IsActive { get; }

    internal Fixed64 CorridorCost { get; }

    internal bool IsLowerBoundCertified { get; }

    internal NavigationPagedSequence<GridNavigationPortal> NavigationPortals => _navigationPortals;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _navigationPortals.RetainedBytes);

    internal int PersistentPageCount => checked(
        1
        + _navigationPortals.PersistentPageCount);
}
