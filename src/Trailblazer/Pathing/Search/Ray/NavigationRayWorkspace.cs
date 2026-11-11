//=======================================================================
// NavigationRayWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Owns fixed caller-side storage for one exclusive navigation ray.</summary>
internal sealed class NavigationRayWorkspace
{
    private NavigationPlanarWork? _planar;
    private long _volumeExclusionsGraphVersion = -1;
    private SwiftPackedSet<long>? _excludedVolumeGridTokens;
    private SwiftList<GridConfigurationKey>? _excludedVolumeCandidates;

    internal NavigationPlanarWork Planar => _planar ??= new NavigationPlanarWork(this);

    internal NavigationRayWorkspace(
        int mapCapacity,
        int pageCapacity,
        int componentCapacity,
        int coveredAddressCapacity,
        int traceIntervalCapacity)
    {
        ChronicleGuard.ThrowIfNegative(mapCapacity, nameof(mapCapacity));
        ChronicleGuard.ThrowIfNegative(pageCapacity, nameof(pageCapacity));
        ChronicleGuard.ThrowIfNegative(componentCapacity, nameof(componentCapacity));
        ChronicleGuard.ThrowIfNegative(
            coveredAddressCapacity,
            nameof(coveredAddressCapacity));
        ChronicleGuard.ThrowIfNegative(
            traceIntervalCapacity,
            nameof(traceIntervalCapacity));
        ChronicleGuard.ThrowIfArgument(
            traceIntervalCapacity > coveredAddressCapacity,
            nameof(traceIntervalCapacity),
            "Trace-interval capacity cannot exceed covered-address capacity.");

        MapCapacity = mapCapacity;
        CoveredAddressCapacity = coveredAddressCapacity;
        TraceIntervalCapacity = traceIntervalCapacity;
        TraceScratch = new GridTraceIntervalScratch(mapCapacity, coveredAddressCapacity);
        TraceIntervals = new SwiftList<GridTraceInterval>(traceIntervalCapacity);
        BodyTraceCells = new SwiftList<GridNavigationBodyTraceCell>(coveredAddressCapacity);
        BodyTraceScratch = new GridNavigationBodyTraceScratch(
            mapCapacity,
            coveredAddressCapacity);
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

    internal SwiftList<GridNavigationBodyTraceCell> BodyTraceCells { get; }

    internal GridNavigationBodyTraceScratch BodyTraceScratch { get; }

    internal NavigationRayChainRecord[] ChainRecords { get; }

    internal NavigationDependencyWorkspace Dependencies { get; }

    internal SwiftPackedSet<long>? GetVolumeExclusions(NavigationWorldGraph graph)
    {
        if (_volumeExclusionsGraphVersion != graph.GraphVersion)
        {
            // One scan per immutable publication, bounded by the admitted map directory.
            _excludedVolumeGridTokens?.Clear();
            for (int i = 0; i < graph.MapCount; i++)
            {
                NavigationMapInstance map = graph.GetInstance(i);
                if (!map.Map.IsSpatial && map.IsMaterialized)
                {
                    _excludedVolumeGridTokens ??= new SwiftPackedSet<long>();
                    _excludedVolumeGridTokens.Add(map.GridIdentity.GridSpawnToken);
                }
            }
            _volumeExclusionsGraphVersion = graph.GraphVersion;
        }
        return _excludedVolumeGridTokens is { Count: > 0 } ? _excludedVolumeGridTokens : null;
    }

    internal SwiftList<GridConfigurationKey> ExcludedVolumeCandidates =>
        _excludedVolumeCandidates ??= new SwiftList<GridConfigurationKey>(MapCapacity);

    internal void Reset()
    {
        TraceScratch.Clear();
        TraceIntervals.Clear();
        BodyTraceCells.Clear();
        BodyTraceScratch.Clear();
        Dependencies.Reset();
    }
}
