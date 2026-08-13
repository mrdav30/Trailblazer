//=======================================================================
// PendingExternalGridChange.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

internal readonly struct PendingExternalGridChange
{
    public PendingExternalGridChange(
        long gridSpawnToken,
        uint gridVersion,
        Vector3d boundsMin,
        Vector3d boundsMax,
        bool requiresLiveGridTouchSelection,
        bool requiresAuthoredCellBoundsSelection)
    {
        GridSpawnToken = gridSpawnToken;
        GridVersion = gridVersion;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        RequiresLiveGridTouchSelection = requiresLiveGridTouchSelection;
        RequiresAuthoredCellBoundsSelection = requiresAuthoredCellBoundsSelection;
    }

    public long GridSpawnToken { get; }

    public uint GridVersion { get; }

    public Vector3d BoundsMin { get; }

    public Vector3d BoundsMax { get; }

    public bool RequiresLiveGridTouchSelection { get; }

    public bool RequiresAuthoredCellBoundsSelection { get; }

    public bool HasSelectionCriteria => RequiresLiveGridTouchSelection || RequiresAuthoredCellBoundsSelection;

    public ExternalGridChartRebuildRequest ToRebuildRequest(ushort gridIndex)
    {
        return new ExternalGridChartRebuildRequest(
            gridIndex,
            BoundsMin,
            BoundsMax,
            RequiresLiveGridTouchSelection,
            RequiresAuthoredCellBoundsSelection);
    }
}
