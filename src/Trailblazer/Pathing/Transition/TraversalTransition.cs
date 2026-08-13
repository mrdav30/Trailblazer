//=======================================================================
// TraversalTransition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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

    /// <summary>
    /// Whether taking this transition should request climb intent for the active guided leg.
    /// </summary>
    public bool RequestsClimbIntent { get; }

    /// <summary>
    /// Whether climb intent should remain active after a guided handoff follows this transition.
    /// </summary>
    public bool PreserveClimbIntentOnFollowup { get; }

    /// <summary>
    /// Initializes a new instance of the TraversalTransition class, representing a transition between two traversal
    /// anchors with optional path cost and intent modifiers.
    /// </summary>
    /// <param name="id">The unique identifier for the transition. Cannot be null or whitespace.</param>
    /// <param name="type">The type of the transition, indicating how traversal occurs between the source and destination anchors.</param>
    /// <param name="source">The anchor from which the transition originates.</param>
    /// <param name="destination">The anchor to which the transition leads.</param>
    /// <param name="pathCostModifier">An optional value that modifies the traversal path cost for this transition. Defaults to 0.</param>
    /// <param name="isBidirectional">true if the transition can be traversed in both directions; otherwise, false. Defaults to false.</param>
    /// <param name="requestsClimbIntent">true if the transition requests a climb intent during traversal; otherwise, false. Defaults to false.</param>
    /// <param name="preserveClimbIntentOnFollowup">true if climb intent should be preserved on follow-up transitions; otherwise, false. Defaults to false.</param>
    /// <exception cref="ArgumentException">Thrown if id is null or consists only of whitespace.</exception>
    public TraversalTransition(
        string id,
        TraversalTransitionType type,
        TraversalTransitionAnchor source,
        TraversalTransitionAnchor destination,
        int pathCostModifier = 0,
        bool isBidirectional = false,
        bool requestsClimbIntent = false,
        bool preserveClimbIntentOnFollowup = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Transition id cannot be null or whitespace.", nameof(id));

        Id = id;
        Type = type;
        Source = source;
        Destination = destination;
        PathCostModifier = pathCostModifier;
        IsBidirectional = isBidirectional;
        RequestsClimbIntent = requestsClimbIntent;
        PreserveClimbIntentOnFollowup = preserveClimbIntentOnFollowup;
    }
}
