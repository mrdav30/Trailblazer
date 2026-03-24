namespace Trailblazer.Pathing;

/// <summary>
/// Represents a single cell parsed from the source map of a <see cref="TraversalAuthoringMap"/>, 
/// including its interpreted legend entry and whether it was explicitly marked for transition generation. 
/// This is an internal struct used during the build process of a traversal chart.
/// </summary>
internal readonly struct ParsedTraversalCell
{
    public ParsedTraversalCell(TraversalLegendEntry entry, bool hasTransitionMarker)
    {
        Entry = entry;
        HasTransitionMarker = hasTransitionMarker;
    }

    public TraversalLegendEntry Entry { get; }

    public bool HasTransitionMarker { get; }

    public bool CanGenerateTransition => HasTransitionMarker && Entry.HasAnchorSpace;
}