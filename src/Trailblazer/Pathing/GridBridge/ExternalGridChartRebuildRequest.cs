//=======================================================================
// ExternalGridChartRebuildRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one coalesced external-grid rebuild selection request for <see cref="PathManager"/>.
/// </summary>
internal readonly struct ExternalGridChartRebuildRequest
{
    internal ExternalGridChartRebuildRequest(
        ushort gridIndex,
        Vector3d boundsMin,
        Vector3d boundsMax,
        bool includeLiveGridTouches,
        bool includeAuthoredCellsInBounds)
    {
        GridIndex = gridIndex;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        IncludeLiveGridTouches = includeLiveGridTouches;
        IncludeAuthoredCellsInBounds = includeAuthoredCellsInBounds;
    }

    public ushort GridIndex { get; }

    public Vector3d BoundsMin { get; }

    public Vector3d BoundsMax { get; }

    public bool IncludeLiveGridTouches { get; }

    public bool IncludeAuthoredCellsInBounds { get; }

    public bool HasSelectionCriteria => IncludeLiveGridTouches || IncludeAuthoredCellsInBounds;
}
