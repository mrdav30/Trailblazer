//=======================================================================
// NavigationRayWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
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
        ChainRecords = traceIntervalCapacity == 0
            ? Array.Empty<NavigationRayChainRecord>()
            : new NavigationRayChainRecord[traceIntervalCapacity];
        Dependencies = new NavigationDependencyWorkspace(pageCapacity, componentCapacity);
    }

    internal int MapCapacity { get; }

    internal int CoveredAddressCapacity { get; }

    internal int TraceIntervalCapacity { get; }

    internal GridTraceIntervalScratch TraceScratch { get; }

    internal SwiftList<GridTraceInterval> TraceIntervals { get; }

    internal NavigationRayChainRecord[] ChainRecords { get; }

    internal NavigationDependencyWorkspace Dependencies { get; }

    internal void Reset()
    {
        TraceScratch.Clear();
        TraceIntervals.Clear();
        Dependencies.Reset();
    }
}

internal enum NavigationRayChainRecordState : byte
{
    Unavailable,
    Unreached,
    Ready,
    Expanded
}

internal struct NavigationRayChainRecord
{
    internal NavigationNodeRef Node;
    internal int PredecessorOrdinal;
    internal int RootOrdinal;
    internal Fixed64 ArrivalParameter;
    internal Fixed64 TraversalCost;
    internal NavigationExplicitConnectionRecord IncomingExplicitConnection;
    internal NavigationRayChainRecordState State;
    internal bool IsSemanticCostNeutral;
}
