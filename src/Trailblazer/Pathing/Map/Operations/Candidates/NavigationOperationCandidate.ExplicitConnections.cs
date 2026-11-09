//=======================================================================
// NavigationOperationCandidate.ExplicitConnections.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <content>Stages changes to source-owned explicit navigation connections.</content>
internal sealed partial class NavigationOperationCandidate
{
    internal NavigationExplicitConnectionIndex ExplicitConnections => _explicitConnections;

    internal int ExplicitChangedSourceCount => _explicitChangedSources.Count;

    internal int ExplicitChangedOwnerCount => _explicitChangedOwners.Count;

    internal string GetExplicitChangedSourceAt(int ordinal) =>
        _explicitChangedSources.GetKeyAt(ordinal);

    internal NavigationConnectionOwnerKey GetExplicitChangedOwnerAt(int ordinal) =>
        _explicitChangedOwners.GetAt(ordinal);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        string mapId,
        NavigationExplicitConnectionIndex foldSource,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            foldSource,
            mapId,
            transaction: null,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        NavigationOverlayTransaction transaction,
        NavigationExplicitConnectionIndex foldSource,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            foldSource,
            mapId: null,
            transaction,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    private bool TrySelectConnection(
        NavigationConnectionOwnerKey owner,
        out MapState source,
        out NavigationConnection definition)
    {
        if (!_maps.TryGetValue(owner.MapId, out source!))
        {
            definition = null!;
            return false;
        }
        if (source.Overlay.TryGetConnection(
                owner.ConnectionId,
                out NavigationConnectionOverlayOperation overlay))
        {
            if (overlay.Kind == NavigationConnectionOverlayOperationKind.Upsert)
            {
                definition = overlay.Connection!;
                return true;
            }
            if (overlay.Kind == NavigationConnectionOverlayOperationKind.Suppress)
            {
                definition = null!;
                return false;
            }
        }
        ReadOnlySpan<NavigationConnection> baked = source.Map.ConnectionSpan;
        int low = 0;
        int high = baked.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(baked[middle].Id, owner.ConnectionId);
            if (comparison == 0)
            {
                definition = baked[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        definition = null!;
        return false;
    }

    private bool TryGetSemanticPrism(
        NavigationCellAddress address,
        NavigationConnection connection,
        bool validateAnchor,
        Vector3d anchor,
        out GridCellPrism prism,
        out NavigationCell cell,
        out bool dormant)
    {
        dormant = false;
        if (!_maps.TryGetValue(address.MapId, out MapState target))
        {
            prism = default;
            cell = default;
            dormant = true;
            return true;
        }
        if (!target.Map.IsSpatial)
        {
            prism = default;
            cell = default;
            return false;
        }
        if (!TryGetEffectiveCell(target, address.Index, out cell))
        {
            prism = default;
            if (IsKnownSuppressed(target, address.Index))
            {
                dormant = true;
                return true;
            }
            return false;
        }
        if (connection.PortalRadiusClearance > cell.RadiusClearance
            || connection.PortalHeightClearance > cell.HeightClearance)
        {
            prism = default;
            return false;
        }
        bool hasPrism = target.Map.GridBinding.TryGetCellPrism(address.Index, out prism);
        System.Diagnostics.Debug.Assert(hasPrism);
        if (validateAnchor && !prism.Contains(anchor))
        {
            prism = default;
            return false;
        }
        return true;
    }

    private static bool ValidateLowerBound(
        in GridCellPrism source,
        in GridCellPrism destination,
        NavigationCell destinationCell,
        NavigationConnection connection,
        Fixed64 corridorCost)
    {
        Vector3d sourceFoot = new(source.Center.X, source.VerticalMin, source.Center.Z);
        Vector3d destinationFoot = new(
            destination.Center.X,
            destination.VerticalMin,
            destination.Center.Z);
        bool hasApproach = Vector3d.TryGetDistance(
            sourceFoot,
            connection.EntryAnchor,
            out Fixed64 approach);
        bool hasDeparture = Vector3d.TryGetDistance(
            connection.ExitAnchor,
            destinationFoot,
            out Fixed64 departure);
        System.Diagnostics.Debug.Assert(hasApproach && hasDeparture);
        return Fixed64.TryAdd(approach, corridorCost, out Fixed64 total)
            && Fixed64.TryAdd(total, departure, out total)
            && Fixed64.TryAdd(total, connection.AdditionalCost, out total)
            && Fixed64.TryAdd(total, destinationCell.EnterCost, out total)
            && NavigationDistanceMath.TryCeiling(
                sourceFoot,
                destinationFoot,
                out Fixed64 direct)
            && total >= direct;
    }

    private static NavigationExplicitConnectionRecord Dormant(
        NavigationConnectionOwnerKey owner,
        NavigationConnection connection) => new(
            owner,
            connection,
            isActive: false,
            Fixed64.Zero,
            NavigationPagedSequence<GridNavigationPortal>.Empty);

    private static bool IsKnownSuppressed(MapState state, GridForge.Spatial.VoxelIndex index) =>
        state.Overlay.TryGetCell(index, out NavigationCellOverlayOperation operation)
        && operation.Kind == NavigationCellOverlayOperationKind.Suppress;
}
