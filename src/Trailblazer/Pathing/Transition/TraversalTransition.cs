using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents an authored handoff between chart-backed traversal and/or raw volume traversal.
/// </summary>
[Serializable]
public readonly struct TraversalTransition
{
    /// <summary>
    /// Stable identifier for this transition within the global registry.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The authored handoff this transition represents.
    /// </summary>
    public TraversalTransitionType Type { get; }

    /// <summary>
    /// The source anchor for this transition.
    /// </summary>
    public TraversalTransitionAnchor Source { get; }

    /// <summary>
    /// The destination anchor for this transition.
    /// </summary>
    public TraversalTransitionAnchor Destination { get; }

    /// <summary>
    /// Optional authored path cost adjustment for taking this transition.
    /// </summary>
    public int PathCostModifier { get; }

    /// <summary>
    /// Whether this transition may be traversed in both directions.
    /// </summary>
    public bool IsBidirectional { get; }

    public TraversalTransition(
        string id,
        TraversalTransitionType type,
        TraversalTransitionAnchor source,
        TraversalTransitionAnchor destination,
        int pathCostModifier = 0,
        bool isBidirectional = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Transition id cannot be null or whitespace.", nameof(id));

        Id = id;
        Type = type;
        Source = source;
        Destination = destination;
        PathCostModifier = pathCostModifier;
        IsBidirectional = isBidirectional;
    }
}
