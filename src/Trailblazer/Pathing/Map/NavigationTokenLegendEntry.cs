//=======================================================================
// NavigationTokenLegendEntry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes the complete navigation payload and optional transition marker media for one token.
/// </summary>
public readonly struct NavigationTokenLegendEntry
{
    /// <summary>
    /// Whether this entry emits an authored navigation cell.
    /// </summary>
    public bool EmitsCell { get; }

    /// <summary>
    /// The complete cell payload emitted when <see cref="EmitsCell"/> is true.
    /// </summary>
    public NavigationCell Cell { get; }

    /// <summary>
    /// The subset of authored media exposed when the token carries a trailing marker.
    /// </summary>
    public TraversalMedia TransitionMedia { get; }

    /// <summary>
    /// Creates an entry that emits a complete cell payload.
    /// </summary>
    public NavigationTokenLegendEntry(
        NavigationCell cell,
        TraversalMedia transitionMedia = TraversalMedia.None)
    {
        SwiftThrowHelper.ThrowIfArgument(
            cell.Media == TraversalMedia.None,
            nameof(cell),
            "The emitted cell must contain at least one known traversal-medium bit.");

        System.Diagnostics.Debug.Assert((cell.Media & ~NavigationCell.KnownMedia) == 0);
        System.Diagnostics.Debug.Assert((cell.RequiredCapabilities & ~NavigationCell.KnownCapabilities) == 0);
        System.Diagnostics.Debug.Assert((cell.Flags & ~NavigationCell.KnownFlags) == 0);
        System.Diagnostics.Debug.Assert(cell.EnterCost >= Fixed64.Zero);
        System.Diagnostics.Debug.Assert(cell.RadiusClearance >= Fixed64.Zero);
        System.Diagnostics.Debug.Assert(cell.HeightClearance >= Fixed64.Zero);

        SwiftThrowHelper.ThrowIfArgument(
            (transitionMedia & ~NavigationCell.KnownMedia) != 0
            || (transitionMedia & ~cell.Media) != 0,
            nameof(transitionMedia),
            "Transition media must be a known subset of the emitted cell media.");

        EmitsCell = true;
        Cell = cell;
        TransitionMedia = transitionMedia;
    }

    /// <summary>
    /// Creates an entry that emits no cell or transition metadata.
    /// </summary>
    public static NavigationTokenLegendEntry SkipCell() => default;

    internal NavigationCell CreateCell(
        Fixed64 inlineCost,
        bool hasInlineCost,
        bool hasTransitionMarker)
    {
        NavigationCellFlags flags = Cell.Flags;
        if (hasTransitionMarker)
        {
            flags |= NavigationCellFlags.TransitionSourceHint
                | NavigationCellFlags.TransitionDestinationHint;
        }

        return new NavigationCell(
            Cell.Media,
            Cell.RequiredCapabilities,
            Cell.Area,
            hasInlineCost ? inlineCost : Cell.EnterCost,
            Cell.RadiusClearance,
            Cell.HeightClearance,
            flags);
    }
}
