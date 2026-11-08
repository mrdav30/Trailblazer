//=======================================================================
// NavigationOperationCandidate.MapFold.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <content>Folds staged map changes into the candidate graph.</content>
internal sealed partial class NavigationOperationCandidate
{
    internal MapFoldCursor BeginMapFold(
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            prepared,
            replacementPolicy,
            limits,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    internal MapFoldCursor BeginMapRemovalFold(
        string mapId,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            mapId,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);
}
