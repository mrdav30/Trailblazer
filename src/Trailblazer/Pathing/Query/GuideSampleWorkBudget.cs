//=======================================================================
// GuideSampleWorkBudget.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines finite deterministic work limits for one guide sampling operation.
/// </summary>
public readonly struct GuideSampleWorkBudget : IEquatable<GuideSampleWorkBudget>
{
    /// <summary>Gets the maximum number of current-node lookup probes.</summary>
    public int MaxCurrentNodeLookupProbes { get; }

    /// <summary>Gets the maximum number of cursor-leg scans.</summary>
    public int MaxCursorLegScans { get; }

    /// <summary>Gets the maximum number of cursor rebases.</summary>
    public int MaxCursorRebases { get; }

    /// <summary>Gets the maximum number of portal checks.</summary>
    public int MaxPortalChecks { get; }

    /// <summary>Gets the maximum number of prism checks.</summary>
    public int MaxPrismChecks { get; }

    /// <summary>Gets the maximum number of trace intervals visited.</summary>
    public int MaxTraceIntervals { get; }

    /// <summary>Gets the maximum number of local recovery attempts.</summary>
    public int MaxLocalRecoveryAttempts { get; }

    /// <summary>
    /// Creates a finite guide sample work budget. Zero disables the corresponding category of work.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any limit is negative.</exception>
    public GuideSampleWorkBudget(
        int maxCurrentNodeLookupProbes,
        int maxCursorLegScans,
        int maxCursorRebases,
        int maxPortalChecks,
        int maxPrismChecks,
        int maxTraceIntervals,
        int maxLocalRecoveryAttempts)
    {
        SwiftThrowHelper.ThrowIfNegative(maxCurrentNodeLookupProbes, nameof(maxCurrentNodeLookupProbes));
        SwiftThrowHelper.ThrowIfNegative(maxCursorLegScans, nameof(maxCursorLegScans));
        SwiftThrowHelper.ThrowIfNegative(maxCursorRebases, nameof(maxCursorRebases));
        SwiftThrowHelper.ThrowIfNegative(maxPortalChecks, nameof(maxPortalChecks));
        SwiftThrowHelper.ThrowIfNegative(maxPrismChecks, nameof(maxPrismChecks));
        SwiftThrowHelper.ThrowIfNegative(maxTraceIntervals, nameof(maxTraceIntervals));
        SwiftThrowHelper.ThrowIfNegative(maxLocalRecoveryAttempts, nameof(maxLocalRecoveryAttempts));

        MaxCurrentNodeLookupProbes = maxCurrentNodeLookupProbes;
        MaxCursorLegScans = maxCursorLegScans;
        MaxCursorRebases = maxCursorRebases;
        MaxPortalChecks = maxPortalChecks;
        MaxPrismChecks = maxPrismChecks;
        MaxTraceIntervals = maxTraceIntervals;
        MaxLocalRecoveryAttempts = maxLocalRecoveryAttempts;
    }

    /// <inheritdoc/>
    public bool Equals(GuideSampleWorkBudget other) =>
        MaxCurrentNodeLookupProbes == other.MaxCurrentNodeLookupProbes
        && MaxCursorLegScans == other.MaxCursorLegScans
        && MaxCursorRebases == other.MaxCursorRebases
        && MaxPortalChecks == other.MaxPortalChecks
        && MaxPrismChecks == other.MaxPrismChecks
        && MaxTraceIntervals == other.MaxTraceIntervals
        && MaxLocalRecoveryAttempts == other.MaxLocalRecoveryAttempts;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GuideSampleWorkBudget other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(MaxCurrentNodeLookupProbes, MaxCursorLegScans);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxCursorRebases);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxPortalChecks);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxPrismChecks);
        hash = SwiftHashTools.CombineHashCodes(hash, MaxTraceIntervals);
        return SwiftHashTools.CombineHashCodes(hash, MaxLocalRecoveryAttempts);
    }

    /// <summary>Returns whether two budgets have exactly equal limits.</summary>
    public static bool operator ==(GuideSampleWorkBudget left, GuideSampleWorkBudget right) => left.Equals(right);

    /// <summary>Returns whether two budgets differ.</summary>
    public static bool operator !=(GuideSampleWorkBudget left, GuideSampleWorkBudget right) => !left.Equals(right);
}
