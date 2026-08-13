//=======================================================================
// MaintenanceWorkBudget.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the finite deterministic work counters consumed at one graph-maintenance boundary.
/// </summary>
public readonly struct MaintenanceWorkBudget : IEquatable<MaintenanceWorkBudget>
{
    /// <summary>Creates an explicit positive maintenance budget.</summary>
    public MaintenanceWorkBudget(
        int maxConsumedEnvelopes,
        int maxBaselineAddresses,
        int maxOverlaySlots,
        int maxSeamCandidates,
        int maxComponentNodes,
        int maxImplicitEdges,
        int maxExplicitEdges,
        int maxDependencyEntries,
        int maxCacheInvalidations)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxConsumedEnvelopes <= 0,
            maxConsumedEnvelopes,
            nameof(maxConsumedEnvelopes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxBaselineAddresses <= 0,
            maxBaselineAddresses,
            nameof(maxBaselineAddresses));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxOverlaySlots <= 0,
            maxOverlaySlots,
            nameof(maxOverlaySlots));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxSeamCandidates <= 0,
            maxSeamCandidates,
            nameof(maxSeamCandidates));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxComponentNodes <= 0,
            maxComponentNodes,
            nameof(maxComponentNodes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxImplicitEdges <= 0,
            maxImplicitEdges,
            nameof(maxImplicitEdges));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxExplicitEdges <= 0,
            maxExplicitEdges,
            nameof(maxExplicitEdges));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxDependencyEntries <= 0,
            maxDependencyEntries,
            nameof(maxDependencyEntries));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxCacheInvalidations <= 0,
            maxCacheInvalidations,
            nameof(maxCacheInvalidations));

        MaxConsumedEnvelopes = maxConsumedEnvelopes;
        MaxBaselineAddresses = maxBaselineAddresses;
        MaxOverlaySlots = maxOverlaySlots;
        MaxSeamCandidates = maxSeamCandidates;
        MaxComponentNodes = maxComponentNodes;
        MaxImplicitEdges = maxImplicitEdges;
        MaxExplicitEdges = maxExplicitEdges;
        MaxDependencyEntries = maxDependencyEntries;
        MaxCacheInvalidations = maxCacheInvalidations;
    }

    /// <summary>Gets the maximum detached GridForge envelopes consumed.</summary>
    public int MaxConsumedEnvelopes { get; }

    /// <summary>Gets the maximum address-filtered baseline entries consumed.</summary>
    public int MaxBaselineAddresses { get; }

    /// <summary>Gets the maximum semantic overlay slots processed.</summary>
    public int MaxOverlaySlots { get; }

    /// <summary>Gets the maximum automatic or explicit seam candidates processed.</summary>
    public int MaxSeamCandidates { get; }

    /// <summary>Gets the maximum structural-component nodes processed.</summary>
    public int MaxComponentNodes { get; }

    /// <summary>Gets the maximum implicit topology-native edges processed.</summary>
    public int MaxImplicitEdges { get; }

    /// <summary>Gets the maximum explicit, seam, or transition edges processed.</summary>
    public int MaxExplicitEdges { get; }

    /// <summary>Gets the maximum dependency-index entries processed.</summary>
    public int MaxDependencyEntries { get; }

    /// <summary>Gets the maximum dependency-indexed cache invalidations processed.</summary>
    public int MaxCacheInvalidations { get; }

    /// <inheritdoc/>
    public bool Equals(MaintenanceWorkBudget other) =>
        MaxConsumedEnvelopes == other.MaxConsumedEnvelopes
        && MaxBaselineAddresses == other.MaxBaselineAddresses
        && MaxOverlaySlots == other.MaxOverlaySlots
        && MaxSeamCandidates == other.MaxSeamCandidates
        && MaxComponentNodes == other.MaxComponentNodes
        && MaxImplicitEdges == other.MaxImplicitEdges
        && MaxExplicitEdges == other.MaxExplicitEdges
        && MaxDependencyEntries == other.MaxDependencyEntries
        && MaxCacheInvalidations == other.MaxCacheInvalidations;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MaintenanceWorkBudget other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(MaxConsumedEnvelopes, MaxBaselineAddresses);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxOverlaySlots);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxSeamCandidates);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxComponentNodes);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxImplicitEdges);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxExplicitEdges);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxDependencyEntries);
        return SwiftHashTools.CombineHashCodes(hash, MaxCacheInvalidations);
    }

    /// <summary>Tests two budgets for equality.</summary>
    public static bool operator ==(MaintenanceWorkBudget left, MaintenanceWorkBudget right) =>
        left.Equals(right);

    /// <summary>Tests two budgets for inequality.</summary>
    public static bool operator !=(MaintenanceWorkBudget left, MaintenanceWorkBudget right) =>
        !left.Equals(right);
}
