//=======================================================================
// TraversalTransitionOverlayOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;
using System;

namespace Trailblazer.Pathing;

/// <summary>Identifies the final-state action for a source-owned semantic transition.</summary>
public enum TraversalTransitionOverlayOperationKind
{
    /// <summary>Write or replace a complete transition definition.</summary>
    Upsert = 0,

    /// <summary>Tombstone the effective source-owned transition.</summary>
    Suppress = 1,

    /// <summary>Remove the override or tombstone and restore the baked definition.</summary>
    RevertToBake = 2
}

/// <summary>Describes one immutable source-owned transition overlay operation.</summary>
public readonly struct TraversalTransitionOverlayOperation
{
    private TraversalTransitionOverlayOperation(
        string id,
        TraversalTransitionOverlayOperationKind kind,
        TraversalTransitionDefinition transition)
    {
        SwiftThrowHelper.ThrowIfNull(id, nameof(id));
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(id),
            nameof(id),
            "Transition id cannot be empty or whitespace.");

        Id = id;
        Kind = kind;
        Transition = transition;
    }

    /// <summary>Gets the stable source-map-local transition identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the final-state operation kind.</summary>
    public TraversalTransitionOverlayOperationKind Kind { get; }

    /// <summary>
    /// Gets the complete definition for <see cref="TraversalTransitionOverlayOperationKind.Upsert"/>.
    /// The value is ignored for suppression and reversion.
    /// </summary>
    public TraversalTransitionDefinition Transition { get; }

    /// <summary>Creates an Upsert operation from a complete transition definition.</summary>
    public static TraversalTransitionOverlayOperation Upsert(TraversalTransitionDefinition transition) =>
        new(transition.Id, TraversalTransitionOverlayOperationKind.Upsert, transition);

    /// <summary>Creates a source-owned transition tombstone.</summary>
    public static TraversalTransitionOverlayOperation Suppress(string id) =>
        new(id, TraversalTransitionOverlayOperationKind.Suppress, default);

    /// <summary>Creates an operation that restores the baked source-owned transition.</summary>
    public static TraversalTransitionOverlayOperation RevertToBake(string id) =>
        new(id, TraversalTransitionOverlayOperationKind.RevertToBake, default);

    internal static void ValidateKind(TraversalTransitionOverlayOperationKind kind)
    {
        SwiftThrowHelper.ThrowIfArgument(
            kind is < TraversalTransitionOverlayOperationKind.Upsert or > TraversalTransitionOverlayOperationKind.RevertToBake,
            nameof(kind),
            "Unknown transition overlay operation kind.");
    }
}
