using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Builds chart data and explicit transitions from tokenized traversable-state authoring input.
/// </summary>
public sealed class TraversalAuthoringMap
{
    private static readonly (int Dy, int Dx, int Dz)[] PositivePerpendicularNeighborOffsets =
    {
        (0, 1, 0),
        (1, 0, 0),
        (0, 0, 1)
    };

    /// <summary>
    /// Creates a new traversal authoring map with the specified parameters.
    /// </summary>
    /// <param name="chartName">The name of the chart.</param>
    /// <param name="sourceMap">The source map containing tokenized traversable-state data.</param>
    /// <param name="minBounds">The minimum bounds of the authored map in world space.</param>
    /// <param name="interval">The interval between voxels in the authored map.</param>
    /// <param name="legend">The legend used to interpret tokens in the source map.</param>
    /// <param name="transitionIdPrefix">The prefix applied to generated transition IDs.</param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public TraversalAuthoringMap(
        string chartName,
        string[,,] sourceMap,
        Vector3d minBounds,
        Fixed64 interval,
        TraversalLegend legend = null,
        string transitionIdPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(chartName))
            throw new ArgumentException("Chart name cannot be null or whitespace.", nameof(chartName));

        ChartName = chartName;
        SourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap));
        MinBounds = minBounds;
        Interval = interval;
        Legend = legend ?? TraversalLegend.CreateBuiltIn();
        TransitionIdPrefix = string.IsNullOrWhiteSpace(transitionIdPrefix)
            ? chartName
            : transitionIdPrefix;
    }

    /// <summary>
    /// The chart name used for the built chart.
    /// </summary>
    public string ChartName { get; }

    /// <summary>
    /// The raw token source map.
    /// </summary>
    public string[,,] SourceMap { get; }

    /// <summary>
    /// The world-space minimum bounds of the authored map.
    /// </summary>
    public Vector3d MinBounds { get; }

    /// <summary>
    /// The authored voxel interval.
    /// </summary>
    public Fixed64 Interval { get; }

    /// <summary>
    /// The token legend used during parsing.
    /// </summary>
    public TraversalLegend Legend { get; }

    /// <summary>
    /// Prefix applied to generated transition ids.
    /// </summary>
    public string TransitionIdPrefix { get; }

    /// <summary>
    /// Parses the source map and builds a chart and set of explicit transitions according to the legend and authoring rules.
    /// </summary>
    /// <returns>A build result containing the generated chart and transitions.</returns>
    public TraversalBuildResult Build()
    {
        int sizeY = SourceMap.GetLength(0);
        int sizeX = SourceMap.GetLength(1);
        int sizeZ = SourceMap.GetLength(2);

        var parsedCells = new ParsedTraversalCell[sizeY, sizeX, sizeZ];
        var chartCells = new NavigationChartCell[sizeY, sizeX, sizeZ];

        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                {
                    ParsedTraversalCell parsedCell = ParseCell(SourceMap[y, x, z], y, x, z);
                    parsedCells[y, x, z] = parsedCell;
                    chartCells[y, x, z] = BuildChartCell(parsedCell);
                }

        var chart = NavigationChart.From3D(ChartName, chartCells, MinBounds, Interval);
        TraversalTransition[] generatedTransitions = BuildTransitions(parsedCells);
        return new TraversalBuildResult(chart, generatedTransitions);
    }

    private static NavigationChartCell BuildChartCell(ParsedTraversalCell parsedCell)
    {
        NavigationChartCell chartCell = parsedCell.Entry.ChartCell;
        if (!parsedCell.HasTransitionMarker
            || !parsedCell.Entry.HasAnchorMedium
            || parsedCell.Entry.Medium != TraversalMedium.Solid)
        {
            return chartCell;
        }

        NavigationChartCellFlags flags = chartCell.Flags
            | NavigationChartCellFlags.TransitionSourceHint
            | NavigationChartCellFlags.TransitionDestinationHint;

        return new NavigationChartCell(
            chartCell.TraversalKinds,
            chartCell.PathCostModifier,
            flags);
    }

    private TraversalTransition[] BuildTransitions(ParsedTraversalCell[,,] parsedCells)
    {
        int sizeY = parsedCells.GetLength(0);
        int sizeX = parsedCells.GetLength(1);
        int sizeZ = parsedCells.GetLength(2);

        SwiftList<TraversalTransition> transitions = new();
        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                {
                    ParsedTraversalCell current = parsedCells[y, x, z];
                    if (!current.CanGenerateTransition)
                        continue;

                    for (int i = 0; i < PositivePerpendicularNeighborOffsets.Length; i++)
                    {
                        (int dy, int dx, int dz) = PositivePerpendicularNeighborOffsets[i];
                        int neighborY = y + dy;
                        int neighborX = x + dx;
                        int neighborZ = z + dz;
                        if (!IsInBounds(parsedCells, neighborY, neighborX, neighborZ))
                            continue;

                        ParsedTraversalCell neighbor = parsedCells[neighborY, neighborX, neighborZ];
                        if (!neighbor.CanGenerateTransition)
                            continue;

                        AddTransitionsForPair(
                            transitions,
                            current,
                            neighbor,
                            y,
                            x,
                            z,
                            neighborY,
                            neighborX,
                            neighborZ);
                    }
                }

        return transitions.ToArray();
    }

    private void AddTransitionsForPair(
        SwiftList<TraversalTransition> transitions,
        ParsedTraversalCell first,
        ParsedTraversalCell second,
        int firstY,
        int firstX,
        int firstZ,
        int secondY,
        int secondX,
        int secondZ)
    {
        TraversalMedium firstMedium = first.Entry.Medium;
        TraversalMedium secondMedium = second.Entry.Medium;

        if (firstMedium == secondMedium)
            return;

        Vector3d firstPosition = GetWorldPosition(firstY, firstX, firstZ);
        Vector3d secondPosition = GetWorldPosition(secondY, secondX, secondZ);

        if (TryResolveChartAndVolumePair(
            firstMedium,
            secondMedium,
            firstPosition,
            secondPosition,
            out TraversalTransition chartToVolumeTransition,
            out TraversalTransition volumeToChartTransition))
        {
            transitions.Add(chartToVolumeTransition);
            transitions.Add(volumeToChartTransition);
        }
    }

    private bool TryResolveChartAndVolumePair(
        TraversalMedium firstMedium,
        TraversalMedium secondMedium,
        Vector3d firstPosition,
        Vector3d secondPosition,
        out TraversalTransition chartToVolumeTransition,
        out TraversalTransition volumeToChartTransition)
    {
        chartToVolumeTransition = default;
        volumeToChartTransition = default;

        if (firstMedium == TraversalMedium.Solid
            && TryBuildChartVolumeTransitionPair(
                firstPosition,
                secondPosition,
                secondMedium,
                out chartToVolumeTransition,
                out volumeToChartTransition))
        {
            return true;
        }

        if (secondMedium == TraversalMedium.Solid
            && TryBuildChartVolumeTransitionPair(
                secondPosition,
                firstPosition,
                firstMedium,
                out chartToVolumeTransition,
                out volumeToChartTransition))
        {
            return true;
        }

        return false;
    }

    private bool TryBuildChartVolumeTransitionPair(
        Vector3d chartPosition,
        Vector3d volumePosition,
        TraversalMedium volumeMedium,
        out TraversalTransition chartToVolumeTransition,
        out TraversalTransition volumeToChartTransition)
    {
        chartToVolumeTransition = default;
        volumeToChartTransition = default;

        TraversalTransitionType entryType;
        TraversalTransitionType exitType;
        TraversalTransitionAnchor volumeAnchor;

        switch (volumeMedium)
        {
            case TraversalMedium.Gas:
                entryType = TraversalTransitionType.Takeoff;
                exitType = TraversalTransitionType.Landing;
                volumeAnchor = TraversalTransitionAnchor.Gas(volumePosition);
                break;
            case TraversalMedium.Liquid:
                entryType = TraversalTransitionType.SwimEntry;
                exitType = TraversalTransitionType.SwimExit;
                volumeAnchor = TraversalTransitionAnchor.Liquid(volumePosition);
                break;
            default:
                return false;
        }

        TraversalTransitionAnchor chartAnchor = TraversalTransitionAnchor.Solid(chartPosition);
        chartToVolumeTransition = new TraversalTransition(
            CreateGeneratedTransitionId(entryType, chartPosition, volumePosition),
            entryType,
            chartAnchor,
            volumeAnchor);
        volumeToChartTransition = new TraversalTransition(
            CreateGeneratedTransitionId(exitType, volumePosition, chartPosition),
            exitType,
            volumeAnchor,
            chartAnchor);
        return true;
    }

    private string CreateGeneratedTransitionId(
        TraversalTransitionType transitionType,
        Vector3d sourcePosition,
        Vector3d destinationPosition)
    {
        (int sourceY, int sourceX, int sourceZ) = ToIndices(sourcePosition);
        (int destinationY, int destinationX, int destinationZ) = ToIndices(destinationPosition);
        return $"{TransitionIdPrefix}:{transitionType}:{sourceY}_{sourceX}_{sourceZ}->{destinationY}_{destinationX}_{destinationZ}";
    }

    private (int Y, int X, int Z) ToIndices(Vector3d worldPosition)
    {
        int x = (int)((worldPosition.x - MinBounds.x) / Interval);
        int y = (int)((worldPosition.y - MinBounds.y) / Interval);
        int z = (int)((worldPosition.z - MinBounds.z) / Interval);
        return (y, x, z);
    }

    private Vector3d GetWorldPosition(int y, int x, int z) =>
        new(
            MinBounds.x + x * Interval,
            MinBounds.y + y * Interval,
            MinBounds.z + z * Interval);

    private ParsedTraversalCell ParseCell(string rawToken, int y, int x, int z)
    {
        string normalizedToken = rawToken?.Trim() ?? string.Empty;
        bool hasTransitionMarker = false;
        if (normalizedToken.Length > 0)
        {
            int markerIndex = normalizedToken.IndexOf('!');
            if (markerIndex >= 0)
            {
                if (markerIndex != normalizedToken.Length - 1
                    || normalizedToken.LastIndexOf('!') != markerIndex)
                {
                    throw new ArgumentException(
                        $"Invalid token '{normalizedToken}' at [{y}, {x}, {z}]. Only a single trailing '!' marker is supported.");
                }

                hasTransitionMarker = true;
                normalizedToken = normalizedToken[..^1].TrimEnd();
                if (string.IsNullOrEmpty(normalizedToken))
                {
                    throw new ArgumentException(
                        $"Invalid token '{rawToken}' at [{y}, {x}, {z}]. Transition markers require a base token.");
                }
            }
        }

        if (!Legend.TryGetEntry(normalizedToken, out TraversalLegendEntry entry))
        {
            throw new ArgumentException(
                $"Unknown traversable-state token '{rawToken}' at [{y}, {x}, {z}].");
        }

        if (hasTransitionMarker && !entry.HasAnchorMedium)
        {
            throw new ArgumentException(
                $"Token '{rawToken}' at [{y}, {x}, {z}] cannot be marked for transition generation.");
        }

        if (hasTransitionMarker
            && entry.Medium == TraversalMedium.Solid
            && !entry.ChartCell.HasSolid)
        {
            throw new ArgumentException(
                $"Token '{rawToken}' at [{y}, {x}, {z}] maps to a non-traversable chart cell and cannot be marked.");
        }

        return new ParsedTraversalCell(entry, hasTransitionMarker);
    }

    private static bool IsInBounds(ParsedTraversalCell[,,] parsedCells, int y, int x, int z)
    {
        return y >= 0 && y < parsedCells.GetLength(0)
            && x >= 0 && x < parsedCells.GetLength(1)
            && z >= 0 && z < parsedCells.GetLength(2);
    }
}
