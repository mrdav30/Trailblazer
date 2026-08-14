//=======================================================================
// NavigationMapFoldWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Advances one map install/remove candidate without rescanning its payload in one frame.</summary>
internal sealed class NavigationMapFoldWork
{
    private readonly NavigationOperationCandidate.MapFoldCursor _cursor;

    internal NavigationMapFoldWork(
        NavigationOperationCandidate source,
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        _cursor = source.BeginMapFold(
            prepared,
            replacementPolicy,
            limits,
            corridorPrisms,
            corridorWaypoints);
    }

    internal NavigationMapFoldWork(
        NavigationOperationCandidate source,
        string mapId)
    {
        _cursor = source.BeginMapRemovalFold(mapId);
    }

    internal NavigationOperationCandidate Candidate => _cursor.Candidate;

    internal long SourceRetainedBytes => _cursor.SourceRetainedBytes;

    internal int SourcePersistentPageCount => _cursor.SourcePersistentPageCount;

    internal long RetainedBytes => _cursor.RetainedBytes;

    internal int PersistentPageCount => _cursor.PersistentPageCount;

    internal bool Advance(MaintenanceWorkMeter meter, out NavigationOperationRejection rejection) =>
        _cursor.Advance(meter, out rejection);
}
