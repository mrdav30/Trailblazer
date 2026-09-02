//=======================================================================
// NavigationAreaPolicyKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Identifies one immutable navigation-area policy revision.
/// </summary>
public readonly struct NavigationAreaPolicyKey : IEquatable<NavigationAreaPolicyKey>
{
    /// <summary>Creates a stable policy identity and positive revision.</summary>
    public NavigationAreaPolicyKey(string policyId, long revision)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(policyId),
            nameof(policyId),
            "Policy ID cannot be null, empty, or whitespace.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            revision <= 0,
            actualValue: null,
            nameof(revision),
            "Policy revision must be positive.");

        PolicyId = policyId;
        Revision = revision;
    }

    /// <summary>Gets the stable ordinal host-owned policy identifier.</summary>
    public string PolicyId { get; }

    /// <summary>Gets the positive immutable content revision.</summary>
    public long Revision { get; }

    /// <inheritdoc/>
    public bool Equals(NavigationAreaPolicyKey other) =>
        string.Equals(PolicyId, other.PolicyId, StringComparison.Ordinal)
        && Revision == other.Revision;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationAreaPolicyKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int idHash = PolicyId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(PolicyId);
        return SwiftHashTools.CombineHashCodes(idHash, Revision.GetHashCode());
    }

    /// <summary>Tests two policy keys for equality.</summary>
    public static bool operator ==(NavigationAreaPolicyKey left, NavigationAreaPolicyKey right) => left.Equals(right);

    /// <summary>Tests two policy keys for inequality.</summary>
    public static bool operator !=(NavigationAreaPolicyKey left, NavigationAreaPolicyKey right) => !left.Equals(right);

    internal void Validate(string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(PolicyId),
            parameterName,
            "Area policy key must contain a stable ID and positive revision.");
        SwiftThrowHelper.ThrowIfArgument(
            Revision <= 0,
            parameterName,
            "Area policy key must contain a stable ID and positive revision.");
    }
}
