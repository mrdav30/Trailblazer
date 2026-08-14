//=======================================================================
// NavigationExplicitConnectionRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one selected explicit definition and its immutable corridor certificate.</summary>
internal sealed class NavigationExplicitConnectionRecord
{
    private readonly Vector3d[] _portalWaypoints;

    internal NavigationExplicitConnectionRecord(
        NavigationConnectionOwnerKey owner,
        NavigationConnection definition,
        bool isActive,
        Fixed64 corridorCost,
        Vector3d[] portalWaypoints,
        bool isLowerBoundCertified = false)
    {
        Owner = owner;
        Definition = definition;
        Source = new NavigationCellAddress(owner.MapId, definition.SourceIndex);
        Destination = definition.Destination;
        IsActive = isActive;
        CorridorCost = corridorCost;
        _portalWaypoints = portalWaypoints;
        IsLowerBoundCertified = isLowerBoundCertified;
    }

    internal NavigationConnectionOwnerKey Owner { get; }

    internal NavigationConnection Definition { get; }

    internal NavigationCellAddress Source { get; }

    internal NavigationCellAddress Destination { get; }

    internal bool IsActive { get; }

    internal Fixed64 CorridorCost { get; }

    internal bool IsLowerBoundCertified { get; }

    internal ReadOnlySpan<Vector3d> PortalWaypoints => _portalWaypoints;

    internal long RetainedBytes => checked(80L + ((long)_portalWaypoints.Length * 24L));
}
