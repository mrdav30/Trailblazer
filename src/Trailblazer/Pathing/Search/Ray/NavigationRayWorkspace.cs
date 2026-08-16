//=======================================================================
// NavigationRayWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Owns fixed caller-side storage for one exclusive navigation ray.</summary>
internal sealed class NavigationRayWorkspace
{
    internal NavigationRayWorkspace(
        int mapCapacity,
        int pageCapacity,
        int componentCapacity,
        int coveredAddressCapacity,
        int traceIntervalCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(mapCapacity, nameof(mapCapacity));
        SwiftThrowHelper.ThrowIfNegative(pageCapacity, nameof(pageCapacity));
        SwiftThrowHelper.ThrowIfNegative(componentCapacity, nameof(componentCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            coveredAddressCapacity,
            nameof(coveredAddressCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            traceIntervalCapacity,
            nameof(traceIntervalCapacity));
        SwiftThrowHelper.ThrowIfArgument(
            traceIntervalCapacity > coveredAddressCapacity,
            nameof(traceIntervalCapacity),
            "Trace-interval capacity cannot exceed covered-address capacity.");

        MapCapacity = mapCapacity;
        CoveredAddressCapacity = coveredAddressCapacity;
        TraceIntervalCapacity = traceIntervalCapacity;
        TraceScratch = new GridTraceIntervalScratch(mapCapacity, coveredAddressCapacity);
        TraceIntervals = new SwiftList<GridTraceInterval>(traceIntervalCapacity);
        IntervalAddresses = traceIntervalCapacity == 0
            ? Array.Empty<NavigationCellAddress>()
            : new NavigationCellAddress[traceIntervalCapacity];
        IntervalNodes = traceIntervalCapacity == 0
            ? Array.Empty<NavigationNodeRef>()
            : new NavigationNodeRef[traceIntervalCapacity];
        PredecessorOrdinals = traceIntervalCapacity == 0
            ? Array.Empty<int>()
            : new int[traceIntervalCapacity];
        EdgeOrdinals = traceIntervalCapacity == 0
            ? Array.Empty<int>()
            : new int[traceIntervalCapacity];
        Dependencies = new NavigationDependencyWorkspace(pageCapacity, componentCapacity);
    }

    internal int MapCapacity { get; }

    internal int CoveredAddressCapacity { get; }

    internal int TraceIntervalCapacity { get; }

    internal GridTraceIntervalScratch TraceScratch { get; }

    internal SwiftList<GridTraceInterval> TraceIntervals { get; }

    internal NavigationCellAddress[] IntervalAddresses { get; }

    internal NavigationNodeRef[] IntervalNodes { get; }

    internal int[] PredecessorOrdinals { get; }

    internal int[] EdgeOrdinals { get; }

    internal NavigationDependencyWorkspace Dependencies { get; }

    internal void Reset()
    {
        TraceScratch.Clear();
        TraceIntervals.Clear();
        Dependencies.Reset();
    }
}
