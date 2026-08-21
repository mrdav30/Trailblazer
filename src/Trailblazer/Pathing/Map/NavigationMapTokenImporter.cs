//=======================================================================
// NavigationMapTokenImporter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Globalization;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Imports X/Y/Z rectangular token maps into immutable addressed navigation maps.
/// </summary>
public static class NavigationMapTokenImporter
{
    /// <summary>
    /// Parses a rectangular token volume and emits explicit canonical cells and addressed
    /// semantic transitions. Token coverage is not retained as a default-cell mask.
    /// </summary>
    public static NavigationMap ImportRectangular(
        string mapId,
        GridConfiguration gridConfiguration,
        string[,,] source,
        NavigationTokenLegend? legend = null,
        string? transitionIdPrefix = null)
    {
        SwiftThrowHelper.ThrowIfNull(source, nameof(source));
        var builder = new NavigationMapBuilder(mapId, gridConfiguration);
        SwiftThrowHelper.ThrowIfArgument(
            builder.GridBinding.Configuration.TopologyKind != GridTopologyKind.RectangularPrism,
            nameof(gridConfiguration),
            "Token import requires rectangular-prism topology.");
        SwiftThrowHelper.ThrowIfArgument(
            source.GetLength(0) != builder.GridBinding.Width
            || source.GetLength(1) != builder.GridBinding.Height
            || source.GetLength(2) != builder.GridBinding.Length,
            nameof(source),
            "Token dimensions must match the normalized grid address space in X/Y/Z order.");

        NavigationTokenLegend resolvedLegend = legend ?? NavigationTokenLegend.CreateBuiltIn();
        string resolvedPrefix = string.IsNullOrWhiteSpace(transitionIdPrefix)
            ? mapId
            : transitionIdPrefix!;
        var parsed = new ParsedTokenCell[
            builder.GridBinding.Width,
            builder.GridBinding.Height,
            builder.GridBinding.Length];

        for (int x = 0; x < parsed.GetLength(0); x++)
        {
            for (int y = 0; y < parsed.GetLength(1); y++)
            {
                for (int z = 0; z < parsed.GetLength(2); z++)
                {
                    ParsedTokenCell cell = ParseCell(source[x, y, z], x, y, z, resolvedLegend);
                    parsed[x, y, z] = cell;
                    if (cell.Entry.EmitsCell)
                        builder.AddCell(new VoxelIndex(x, y, z), cell.Cell);
                }
            }
        }

        GenerateTransitions(builder, parsed, mapId, resolvedPrefix);
        return builder.Build();
    }

    private static ParsedTokenCell ParseCell(
        string rawToken,
        int x,
        int y,
        int z,
        NavigationTokenLegend legend)
    {
        string token = rawToken?.Trim() ?? string.Empty;
        bool hasMarker = false;
        int markerIndex = token.IndexOf('!');
        if (markerIndex >= 0)
        {
            if (markerIndex != token.Length - 1 || token.LastIndexOf('!') != markerIndex)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(rawToken),
                    $"Invalid token '{token}' at [{x}, {y}, {z}]. Only one trailing '!' marker is supported.");
            }
            hasMarker = true;
            token = token[..^1].TrimEnd();
            if (token.Length == 0)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(rawToken),
                    $"Invalid token '{rawToken}' at [{x}, {y}, {z}]. A marker requires a base token.");
            }
        }

        bool hasInlineCost = false;
        Fixed64 inlineCost = default;
        int costSeparatorIndex = token.LastIndexOf('_');
        if (costSeparatorIndex >= 0)
        {
            string costText = token[(costSeparatorIndex + 1)..];
            bool parsed = Fixed64.TryParse(costText, CultureInfo.InvariantCulture, out inlineCost);
            if (!parsed || inlineCost < Fixed64.Zero || costSeparatorIndex == 0)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(rawToken),
                    $"Invalid non-negative fixed-point cost in token '{rawToken}' at [{x}, {y}, {z}].");
            }
            hasInlineCost = true;
            token = token[..costSeparatorIndex].TrimEnd();
        }

        bool known = legend.TryGetEntry(token, out NavigationTokenLegendEntry entry);
        if (!known)
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(rawToken),
                $"Unknown navigation token '{rawToken}' at [{x}, {y}, {z}].");
        }
        if (hasMarker
            && (!entry.EmitsCell
                || (entry.TransitionMedia == TraversalMedia.None
                    && (entry.Cell.Flags & NavigationCellFlags.ClimbSurfaceHint) == 0)))
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(rawToken),
                $"Token '{rawToken}' at [{x}, {y}, {z}] cannot generate transitions.");
        }

        if (!entry.EmitsCell)
            return default;

        return new ParsedTokenCell(
            entry,
            entry.CreateCell(inlineCost, hasInlineCost, hasMarker),
            hasMarker);
    }

    private static void GenerateTransitions(
        NavigationMapBuilder builder,
        ParsedTokenCell[,,] cells,
        string mapId,
        string transitionIdPrefix)
    {
        for (int x = 0; x < cells.GetLength(0); x++)
        {
            for (int y = 0; y < cells.GetLength(1); y++)
            {
                for (int z = 0; z < cells.GetLength(2); z++)
                {
                    VoxelIndex source = new(x, y, z);
                    if (x + 1 < cells.GetLength(0))
                        GenerateTransitionPair(builder, cells[x, y, z], cells[x + 1, y, z], source, new VoxelIndex(x + 1, y, z), mapId, transitionIdPrefix);
                    if (y + 1 < cells.GetLength(1))
                        GenerateTransitionPair(builder, cells[x, y, z], cells[x, y + 1, z], source, new VoxelIndex(x, y + 1, z), mapId, transitionIdPrefix);
                    if (z + 1 < cells.GetLength(2))
                        GenerateTransitionPair(builder, cells[x, y, z], cells[x, y, z + 1], source, new VoxelIndex(x, y, z + 1), mapId, transitionIdPrefix);
                }
            }
        }
    }

    private static void GenerateTransitionPair(
        NavigationMapBuilder builder,
        ParsedTokenCell first,
        ParsedTokenCell second,
        VoxelIndex firstIndex,
        VoxelIndex secondIndex,
        string mapId,
        string prefix)
    {
        if (!first.Entry.EmitsCell || !second.Entry.EmitsCell)
            return;

        TryAddMediaTransitionPair(builder, first, second, firstIndex, secondIndex, mapId, prefix);
        TryAddClimbTransitionPair(builder, first, second, firstIndex, secondIndex, mapId, prefix);
    }

    private static void TryAddMediaTransitionPair(
        NavigationMapBuilder builder,
        ParsedTokenCell first,
        ParsedTokenCell second,
        VoxelIndex firstIndex,
        VoxelIndex secondIndex,
        string mapId,
        string prefix)
    {
        TraversalMedia firstMarkerMedia = first.HasMarker ? first.Entry.TransitionMedia : TraversalMedia.None;
        TraversalMedia secondMarkerMedia = second.HasMarker ? second.Entry.TransitionMedia : TraversalMedia.None;
        int candidateCount = 0;
        VoxelIndex solidIndex = default;
        VoxelIndex volumeIndex = default;
        TraversalMedium volumeMedium = default;

        TrySelectBoundary(firstMarkerMedia, secondMarkerMedia, TraversalMedia.Gas, TraversalMedium.Gas, firstIndex, secondIndex, ref candidateCount, ref solidIndex, ref volumeIndex, ref volumeMedium);
        TrySelectBoundary(firstMarkerMedia, secondMarkerMedia, TraversalMedia.Liquid, TraversalMedium.Liquid, firstIndex, secondIndex, ref candidateCount, ref solidIndex, ref volumeIndex, ref volumeMedium);
        if (candidateCount != 1)
            return;

        TraversalTransitionType entryType = volumeMedium == TraversalMedium.Gas
            ? TraversalTransitionType.Takeoff
            : TraversalTransitionType.SwimEntry;
        TraversalTransitionType exitType = volumeMedium == TraversalMedium.Gas
            ? TraversalTransitionType.Landing
            : TraversalTransitionType.SwimExit;
        TraversalCapability capability = volumeMedium == TraversalMedium.Gas
            ? TraversalCapability.Fly
            : TraversalCapability.Swim;
        builder.AddTransition(CreateTransition(prefix, entryType, solidIndex, TraversalMedium.Solid, volumeIndex, volumeMedium, mapId, capability));
        builder.AddTransition(CreateTransition(prefix, exitType, volumeIndex, volumeMedium, solidIndex, TraversalMedium.Solid, mapId, capability));
    }

    private static void TrySelectBoundary(
        TraversalMedia firstMedia,
        TraversalMedia secondMedia,
        TraversalMedia volumeFlag,
        TraversalMedium volumeMediumCandidate,
        VoxelIndex firstIndex,
        VoxelIndex secondIndex,
        ref int candidateCount,
        ref VoxelIndex solidIndex,
        ref VoxelIndex volumeIndex,
        ref TraversalMedium volumeMedium)
    {
        if ((firstMedia & TraversalMedia.Solid) != 0 && (secondMedia & volumeFlag) != 0)
        {
            candidateCount++;
            solidIndex = firstIndex;
            volumeIndex = secondIndex;
            volumeMedium = volumeMediumCandidate;
        }

        if ((secondMedia & TraversalMedia.Solid) != 0 && (firstMedia & volumeFlag) != 0)
        {
            candidateCount++;
            solidIndex = secondIndex;
            volumeIndex = firstIndex;
            volumeMedium = volumeMediumCandidate;
        }
    }

    private static void TryAddClimbTransitionPair(
        NavigationMapBuilder builder,
        ParsedTokenCell first,
        ParsedTokenCell second,
        VoxelIndex firstIndex,
        VoxelIndex secondIndex,
        string mapId,
        string prefix)
    {
        bool firstClimb = (first.Cell.Flags & NavigationCellFlags.ClimbSurfaceHint) != 0;
        bool secondClimb = (second.Cell.Flags & NavigationCellFlags.ClimbSurfaceHint) != 0;
        bool connect = (firstClimb && secondClimb)
            || (firstClimb && first.HasMarker && (second.Cell.Media & TraversalMedia.Solid) != 0)
            || (secondClimb && second.HasMarker && (first.Cell.Media & TraversalMedia.Solid) != 0);
        if (!connect)
            return;

        TraversalTransitionLocomotionHints firstToSecondHints =
            TraversalTransitionLocomotionHints.RequestClimb
            | (secondClimb
                ? TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion
                : TraversalTransitionLocomotionHints.None);
        TraversalTransitionLocomotionHints secondToFirstHints =
            TraversalTransitionLocomotionHints.RequestClimb
            | (firstClimb
                ? TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion
                : TraversalTransitionLocomotionHints.None);
        builder.AddTransition(CreateTransition(
            prefix,
            TraversalTransitionType.Climb,
            firstIndex,
            TraversalMedium.Solid,
            secondIndex,
            TraversalMedium.Solid,
            mapId,
            TraversalCapability.Climb,
            Fixed64.One,
            firstToSecondHints));
        builder.AddTransition(CreateTransition(
            prefix,
            TraversalTransitionType.Climb,
            secondIndex,
            TraversalMedium.Solid,
            firstIndex,
            TraversalMedium.Solid,
            mapId,
            TraversalCapability.Climb,
            Fixed64.One,
            secondToFirstHints));
    }

    private static TraversalTransitionDefinition CreateTransition(
        string prefix,
        TraversalTransitionType type,
        VoxelIndex source,
        TraversalMedium sourceMedium,
        VoxelIndex destination,
        TraversalMedium destinationMedium,
        string mapId,
        TraversalCapability capability,
        Fixed64 actionCost = default,
        TraversalTransitionLocomotionHints locomotionHints = TraversalTransitionLocomotionHints.None) =>
        new(
            CreateTransitionId(prefix, type, source, sourceMedium, destination, destinationMedium),
            type,
            source,
            sourceMedium,
            new NavigationCellAddress(mapId, destination),
            destinationMedium,
            capability,
            actionCost,
            locomotionHints: locomotionHints);

    private static string CreateTransitionId(
        string prefix,
        TraversalTransitionType type,
        VoxelIndex source,
        TraversalMedium sourceMedium,
        VoxelIndex destination,
        TraversalMedium destinationMedium) =>
        string.Concat(
            prefix,
            ":", type.ToString(), ":",
            source.x.ToString(CultureInfo.InvariantCulture), "_",
            source.y.ToString(CultureInfo.InvariantCulture), "_",
            source.z.ToString(CultureInfo.InvariantCulture), "_", sourceMedium.ToString(), "->",
            destination.x.ToString(CultureInfo.InvariantCulture), "_",
            destination.y.ToString(CultureInfo.InvariantCulture), "_",
            destination.z.ToString(CultureInfo.InvariantCulture), "_", destinationMedium.ToString());

    private readonly struct ParsedTokenCell
    {
        internal NavigationTokenLegendEntry Entry { get; }
        internal NavigationCell Cell { get; }
        internal bool HasMarker { get; }

        internal ParsedTokenCell(
            NavigationTokenLegendEntry entry,
            NavigationCell cell,
            bool hasMarker)
        {
            Entry = entry;
            Cell = cell;
            HasMarker = hasMarker;
        }
    }
}
