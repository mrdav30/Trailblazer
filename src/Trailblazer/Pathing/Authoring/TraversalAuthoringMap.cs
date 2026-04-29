using FixedMathSharp;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Builds chart data and explicit transitions from tokenized traversable-state authoring input.
/// </summary>
public sealed class TraversalAuthoringMap
{

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
        TraversalLegend? legend = null,
        string? transitionIdPrefix = null)
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
        TraversalTransition[] generatedTransitions =
            GeneratedTraversalTransitionBuilder.BuildTransitions(chart, TransitionIdPrefix);
        return new TraversalBuildResult(chart, generatedTransitions, TransitionIdPrefix);
    }

    private static NavigationChartCell BuildChartCell(ParsedTraversalCell parsedCell)
    {
        NavigationChartCell chartCell = parsedCell.Entry.ChartCell;
        int costModifier = parsedCell.PathCostModifier;
        NavigationChartCellFlags flags = chartCell.Flags;

        if (parsedCell.HasTransitionMarker
            && (flags & NavigationChartCellFlags.ClimbSurfaceHint) != 0)
        {
            flags |= NavigationChartCellFlags.ClimbTransitionHint;
        }

        if (!parsedCell.HasTransitionMarker || !parsedCell.CanGenerateTransition)
        {
            if (costModifier == 0)
            {
                if (flags == chartCell.Flags)
                    return chartCell;

                return new NavigationChartCell(
                    chartCell.TraversalKinds,
                    chartCell.PathCostModifier,
                    flags,
                    chartCell.GeneratedTransitionMedia);
            }

            return new NavigationChartCell(
                chartCell.TraversalKinds,
                costModifier,
                flags,
                chartCell.GeneratedTransitionMedia);
        }

        if (chartCell.HasSolid)
        {
            flags |= NavigationChartCellFlags.TransitionSourceHint
                | NavigationChartCellFlags.TransitionDestinationHint;
        }

        return new NavigationChartCell(
            chartCell.TraversalKinds,
            costModifier != 0 ? costModifier : chartCell.PathCostModifier,
            flags,
            parsedCell.TransitionMedia);
    }

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

        // Parse an optional inline path cost modifier suffix: <token>_<int> (e.g. "S_60", "SL_45").
        // The suffix is extracted after transition-marker stripping so "S_60!" is also valid.
        int pathCostModifier = 0;
        int underscoreIndex = normalizedToken.LastIndexOf('_');
        if (underscoreIndex >= 0)
        {
            string costPart = normalizedToken[(underscoreIndex + 1)..];
            if (int.TryParse(costPart, out int parsedCost))
            {
                pathCostModifier = parsedCost;
                normalizedToken = normalizedToken[..underscoreIndex].TrimEnd();
            }
        }

        if (!Legend.TryGetEntry(normalizedToken, out TraversalLegendEntry entry))
        {
            throw new ArgumentException(
                $"Unknown traversable-state token '{rawToken}' at [{y}, {x}, {z}].");
        }

        if (hasTransitionMarker
            && !entry.HasTransitionMedia
            && (entry.ChartCell.Flags & NavigationChartCellFlags.ClimbSurfaceHint) == 0)
        {
            throw new ArgumentException(
                $"Token '{rawToken}' at [{y}, {x}, {z}] cannot be marked for transition generation.");
        }

        // Ignore cost modifiers on skip cells; they contribute no traversal data.
        if (!entry.ChartCell.HasTraversalData)
            pathCostModifier = 0;

        return new ParsedTraversalCell(entry, hasTransitionMarker, pathCostModifier);
    }
}
