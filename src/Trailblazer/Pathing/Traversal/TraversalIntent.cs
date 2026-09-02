//=======================================================================
// TraversalIntent.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the exact starting medium and allowed target media for a path query.
/// </summary>
public readonly struct TraversalIntent : IEquatable<TraversalIntent>
{
    /// <summary>
    /// Gets the exact traversal medium at the query start.
    /// </summary>
    public TraversalMedium StartMedium { get; }

    /// <summary>
    /// Gets the nonempty set of known traversal media allowed at the target.
    /// </summary>
    public TraversalMedia TargetMedia { get; }

    /// <summary>
    /// Creates immutable traversal intent.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when target media are empty or contain an unknown bit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the start medium is unknown.</exception>
    public TraversalIntent(
        TraversalMedium startMedium,
        TraversalMedia targetMedia)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            !TraversalTransitionDefinition.IsKnownMedium(startMedium),
            (int)startMedium,
            nameof(startMedium),
            "Start medium must identify one known traversal medium.");
        SwiftThrowHelper.ThrowIfArgument(
            targetMedia == TraversalMedia.None
                || (targetMedia & ~NavigationCell.KnownMedia) != 0,
            nameof(targetMedia),
            "Target media must contain at least one known traversal-medium bit.");

        StartMedium = startMedium;
        TargetMedia = targetMedia;
    }

    /// <inheritdoc/>
    public bool Equals(TraversalIntent other) =>
        StartMedium == other.StartMedium
        && TargetMedia == other.TargetMedia;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TraversalIntent other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return SwiftHashTools.CombineHashCodes((int)StartMedium, (int)TargetMedia);
    }

    /// <summary>
    /// Returns whether two traversal intents have exactly equal values.
    /// </summary>
    public static bool operator ==(TraversalIntent left, TraversalIntent right) => left.Equals(right);

    /// <summary>
    /// Returns whether two traversal intents differ.
    /// </summary>
    public static bool operator !=(TraversalIntent left, TraversalIntent right) => !left.Equals(right);

    internal void Validate(string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgument(
            !TraversalTransitionDefinition.IsKnownMedium(StartMedium),
            parameterName,
            "Traversal intent must contain an exact known start medium and nonempty known target media.");
        SwiftThrowHelper.ThrowIfArgument(
            TargetMedia == TraversalMedia.None,
            parameterName,
            "Traversal intent must contain an exact known start medium and nonempty known target media.");
        SwiftThrowHelper.ThrowIfArgument(
            (TargetMedia & ~NavigationCell.KnownMedia) != 0,
            parameterName,
            "Traversal intent must contain an exact known start medium and nonempty known target media.");
    }
}
