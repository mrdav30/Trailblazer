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
/// Defines the requested starting and target traversal domains for a path query.
/// </summary>
public readonly struct TraversalIntent : IEquatable<TraversalIntent>
{
    /// <summary>
    /// Gets the requested starting graph domain.
    /// </summary>
    public TraversalDomain StartDomain { get; }

    /// <summary>
    /// Gets the current medium, or <see cref="TraversalMedium.Unknown"/> for automatic selection.
    /// </summary>
    public TraversalMedium CurrentMedium { get; }

    /// <summary>
    /// Gets the requested target graph domain.
    /// </summary>
    public TraversalDomain TargetDomain { get; }

    /// <summary>
    /// Creates immutable traversal intent.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the starting domain conflicts with the current medium.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a domain or medium is unknown.</exception>
    public TraversalIntent(
        TraversalDomain startDomain,
        TraversalMedium currentMedium,
        TraversalDomain targetDomain)
    {
        ValidateDomain(startDomain, nameof(startDomain));
        ValidateDomain(targetDomain, nameof(targetDomain));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            currentMedium is < TraversalMedium.Unknown or > TraversalMedium.Liquid,
            (int)currentMedium,
            nameof(currentMedium),
            "Traversal medium is unknown.");
        SwiftThrowHelper.ThrowIfArgument(
            startDomain == TraversalDomain.Surface
                && currentMedium is TraversalMedium.Gas or TraversalMedium.Liquid,
            nameof(currentMedium),
            "A surface starting domain cannot use a volume current medium.");
        SwiftThrowHelper.ThrowIfArgument(
            startDomain == TraversalDomain.Volume && currentMedium == TraversalMedium.Solid,
            nameof(currentMedium),
            "A volume starting domain cannot use a solid current medium.");

        StartDomain = startDomain;
        CurrentMedium = currentMedium;
        TargetDomain = targetDomain;
    }

    /// <inheritdoc/>
    public bool Equals(TraversalIntent other) =>
        StartDomain == other.StartDomain
        && CurrentMedium == other.CurrentMedium
        && TargetDomain == other.TargetDomain;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TraversalIntent other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes((int)StartDomain, (int)CurrentMedium);
        return SwiftHashTools.CombineHashCodes(hash, (int)TargetDomain);
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
        bool domainIsValid = StartDomain is >= TraversalDomain.Automatic and <= TraversalDomain.Volume
            && TargetDomain is >= TraversalDomain.Automatic and <= TraversalDomain.Volume;
        bool mediumIsValid = CurrentMedium is >= TraversalMedium.Unknown and <= TraversalMedium.Liquid;
        bool combinationIsValid = StartDomain != TraversalDomain.Surface
            || CurrentMedium is not (TraversalMedium.Gas or TraversalMedium.Liquid);
        combinationIsValid &= StartDomain != TraversalDomain.Volume || CurrentMedium != TraversalMedium.Solid;

        SwiftThrowHelper.ThrowIfArgument(
            !domainIsValid || !mediumIsValid || !combinationIsValid,
            parameterName,
            "Traversal intent contains an unknown or conflicting value.");
    }

    private static void ValidateDomain(TraversalDomain domain, string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            domain is < TraversalDomain.Automatic or > TraversalDomain.Volume,
            (int)domain,
            parameterName,
            "Traversal domain is unknown.");
    }
}
