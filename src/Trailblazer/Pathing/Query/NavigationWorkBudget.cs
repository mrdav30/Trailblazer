//=======================================================================
// NavigationWorkBudget.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines finite deterministic work limits shared by a complete navigation query.
/// </summary>
public readonly struct NavigationWorkBudget : IEquatable<NavigationWorkBudget>
{
    /// <summary>Gets the maximum number of address or lookup probes.</summary>
    public int MaxLookupProbes { get; }

    /// <summary>Gets the maximum number of endpoint candidates examined.</summary>
    public int MaxEndpointCandidates { get; }

    /// <summary>Gets the maximum number of graph nodes expanded.</summary>
    public int MaxExpandedNodes { get; }

    /// <summary>Gets the maximum number of graph edges evaluated.</summary>
    public int MaxEvaluatedEdges { get; }

    /// <summary>Gets the maximum number of connection witness or polyline legs evaluated.</summary>
    public int MaxConnectionLegs { get; }

    /// <summary>Gets the maximum number of transition candidates examined.</summary>
    public int MaxTransitionCandidates { get; }

    /// <summary>Gets the maximum number of transition pairs evaluated.</summary>
    public int MaxTransitionPairs { get; }

    /// <summary>Gets the maximum number of staged or nested path legs attempted.</summary>
    public int MaxStagedLegAttempts { get; }

    /// <summary>Gets the maximum number of navigation-ray trace intervals visited.</summary>
    public int MaxTraceIntervals { get; }

    /// <summary>Gets the maximum number of covered-voxel intervals visited.</summary>
    public int MaxCoveredVoxelIntervals { get; }

    /// <summary>Gets the maximum number of path-simplification rays evaluated.</summary>
    public int MaxSimplificationRays { get; }

    /// <summary>
    /// Creates a finite navigation work budget. Zero disables the corresponding category of work.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any limit is negative.</exception>
    public NavigationWorkBudget(
        int maxLookupProbes,
        int maxEndpointCandidates,
        int maxExpandedNodes,
        int maxEvaluatedEdges,
        int maxConnectionLegs,
        int maxTransitionCandidates,
        int maxTransitionPairs,
        int maxStagedLegAttempts,
        int maxTraceIntervals,
        int maxCoveredVoxelIntervals,
        int maxSimplificationRays)
    {
        SwiftThrowHelper.ThrowIfNegative(maxLookupProbes, nameof(maxLookupProbes));
        SwiftThrowHelper.ThrowIfNegative(maxEndpointCandidates, nameof(maxEndpointCandidates));
        SwiftThrowHelper.ThrowIfNegative(maxExpandedNodes, nameof(maxExpandedNodes));
        SwiftThrowHelper.ThrowIfNegative(maxEvaluatedEdges, nameof(maxEvaluatedEdges));
        SwiftThrowHelper.ThrowIfNegative(maxConnectionLegs, nameof(maxConnectionLegs));
        SwiftThrowHelper.ThrowIfNegative(maxTransitionCandidates, nameof(maxTransitionCandidates));
        SwiftThrowHelper.ThrowIfNegative(maxTransitionPairs, nameof(maxTransitionPairs));
        SwiftThrowHelper.ThrowIfNegative(maxStagedLegAttempts, nameof(maxStagedLegAttempts));
        SwiftThrowHelper.ThrowIfNegative(maxTraceIntervals, nameof(maxTraceIntervals));
        SwiftThrowHelper.ThrowIfNegative(maxCoveredVoxelIntervals, nameof(maxCoveredVoxelIntervals));
        SwiftThrowHelper.ThrowIfNegative(maxSimplificationRays, nameof(maxSimplificationRays));

        MaxLookupProbes = maxLookupProbes;
        MaxEndpointCandidates = maxEndpointCandidates;
        MaxExpandedNodes = maxExpandedNodes;
        MaxEvaluatedEdges = maxEvaluatedEdges;
        MaxConnectionLegs = maxConnectionLegs;
        MaxTransitionCandidates = maxTransitionCandidates;
        MaxTransitionPairs = maxTransitionPairs;
        MaxStagedLegAttempts = maxStagedLegAttempts;
        MaxTraceIntervals = maxTraceIntervals;
        MaxCoveredVoxelIntervals = maxCoveredVoxelIntervals;
        MaxSimplificationRays = maxSimplificationRays;
    }

    /// <inheritdoc/>
    public bool Equals(NavigationWorkBudget other) =>
        MaxLookupProbes == other.MaxLookupProbes
        && MaxEndpointCandidates == other.MaxEndpointCandidates
        && MaxExpandedNodes == other.MaxExpandedNodes
        && MaxEvaluatedEdges == other.MaxEvaluatedEdges
        && MaxConnectionLegs == other.MaxConnectionLegs
        && MaxTransitionCandidates == other.MaxTransitionCandidates
        && MaxTransitionPairs == other.MaxTransitionPairs
        && MaxStagedLegAttempts == other.MaxStagedLegAttempts
        && MaxTraceIntervals == other.MaxTraceIntervals
        && MaxCoveredVoxelIntervals == other.MaxCoveredVoxelIntervals
        && MaxSimplificationRays == other.MaxSimplificationRays;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationWorkBudget other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(MaxLookupProbes, MaxEndpointCandidates);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxExpandedNodes);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxEvaluatedEdges);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxConnectionLegs);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxTransitionCandidates);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxTransitionPairs);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxStagedLegAttempts);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxTraceIntervals);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxCoveredVoxelIntervals);
        return SwiftHashTools.CombineHashCodes(hash, MaxSimplificationRays);
    }

    /// <summary>Returns whether two budgets have exactly equal limits.</summary>
    public static bool operator ==(NavigationWorkBudget left, NavigationWorkBudget right) => left.Equals(right);

    /// <summary>Returns whether two budgets differ.</summary>
    public static bool operator !=(NavigationWorkBudget left, NavigationWorkBudget right) => !left.Equals(right);

    internal void Validate(string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgument(
            MaxLookupProbes < 0
                || MaxEndpointCandidates < 0
                || MaxExpandedNodes < 0
                || MaxEvaluatedEdges < 0
                || MaxConnectionLegs < 0
                || MaxTransitionCandidates < 0
                || MaxTransitionPairs < 0
                || MaxStagedLegAttempts < 0
                || MaxTraceIntervals < 0
                || MaxCoveredVoxelIntervals < 0
                || MaxSimplificationRays < 0,
            parameterName,
            "Navigation work budget contains a negative limit.");
    }
}
