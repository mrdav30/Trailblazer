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
        int maxComponentNodes,
        int maxSeamCandidateProbes,
        int maxExplicitEdges,
        int maxDependencyEntries)
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
            maxComponentNodes <= 0,
            maxComponentNodes,
            nameof(maxComponentNodes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxSeamCandidateProbes <= 0,
            maxSeamCandidateProbes,
            nameof(maxSeamCandidateProbes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxExplicitEdges <= 0,
            maxExplicitEdges,
            nameof(maxExplicitEdges));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxDependencyEntries <= 0,
            maxDependencyEntries,
            nameof(maxDependencyEntries));

        MaxConsumedEnvelopes = maxConsumedEnvelopes;
        MaxBaselineAddresses = maxBaselineAddresses;
        MaxOverlaySlots = maxOverlaySlots;
        MaxComponentNodes = maxComponentNodes;
        MaxSeamCandidateProbes = maxSeamCandidateProbes;
        MaxExplicitEdges = maxExplicitEdges;
        MaxDependencyEntries = maxDependencyEntries;
    }

    /// <summary>Gets the maximum detached GridForge envelopes consumed.</summary>
    public int MaxConsumedEnvelopes { get; }

    /// <summary>Gets the maximum address-filtered baseline entries consumed.</summary>
    public int MaxBaselineAddresses { get; }

    /// <summary>Gets the maximum semantic overlay slots processed.</summary>
    public int MaxOverlaySlots { get; }

    /// <summary>Gets the maximum structural-component nodes processed.</summary>
    public int MaxComponentNodes { get; }

    /// <summary>Gets the maximum automatic-seam pair, source-address, and target-address probes.</summary>
    public int MaxSeamCandidateProbes { get; }

    /// <summary>Gets the maximum accepted explicit, seam, or transition edges processed.</summary>
    public int MaxExplicitEdges { get; }

    /// <summary>Gets the maximum dependency-index entries processed.</summary>
    public int MaxDependencyEntries { get; }

    /// <inheritdoc/>
    public bool Equals(MaintenanceWorkBudget other) =>
        MaxConsumedEnvelopes == other.MaxConsumedEnvelopes
        && MaxBaselineAddresses == other.MaxBaselineAddresses
        && MaxOverlaySlots == other.MaxOverlaySlots
        && MaxComponentNodes == other.MaxComponentNodes
        && MaxSeamCandidateProbes == other.MaxSeamCandidateProbes
        && MaxExplicitEdges == other.MaxExplicitEdges
        && MaxDependencyEntries == other.MaxDependencyEntries;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MaintenanceWorkBudget other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(MaxConsumedEnvelopes, MaxBaselineAddresses);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxOverlaySlots);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxComponentNodes);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxSeamCandidateProbes);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxExplicitEdges);
        return SwiftHashTools.CombineHashCodes(hash, MaxDependencyEntries);
    }

    /// <summary>Tests two budgets for equality.</summary>
    public static bool operator ==(MaintenanceWorkBudget left, MaintenanceWorkBudget right) =>
        left.Equals(right);

    /// <summary>Tests two budgets for inequality.</summary>
    public static bool operator !=(MaintenanceWorkBudget left, MaintenanceWorkBudget right) =>
        !left.Equals(right);
}
