//=======================================================================
// ParsedTraversalCell.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a single cell parsed from the source map of a <see cref="TraversalAuthoringMap"/>,
/// including its interpreted legend entry and whether it was explicitly marked for transition generation.
/// This is an internal struct used during the build process of a traversal chart.
/// </summary>
internal readonly struct ParsedTraversalCell
{
    public ParsedTraversalCell(TraversalLegendEntry entry, bool hasTransitionMarker, int pathCostModifier = 0)
    {
        Entry = entry;
        HasTransitionMarker = hasTransitionMarker;
        PathCostModifier = pathCostModifier;
    }

    public TraversalLegendEntry Entry { get; }

    public bool HasTransitionMarker { get; }

    /// <summary>
    /// An inline path cost modifier parsed from the token suffix (e.g. <c>S_60</c> yields 60).
    /// Zero when no suffix was present or when the entry is a skip cell.
    /// </summary>
    public int PathCostModifier { get; }

    public bool CanGenerateTransition => TransitionMedia != TraversalMedia.None;

    public TraversalMedia TransitionMedia => HasTransitionMarker
        ? Entry.TransitionMedia
        : TraversalMedia.None;
}
