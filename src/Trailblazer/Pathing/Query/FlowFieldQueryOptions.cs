//=======================================================================
// FlowFieldQueryOptions.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines immutable options specific to destination-centric flow field queries.
/// </summary>
public readonly struct FlowFieldQueryOptions : IEquatable<FlowFieldQueryOptions>
{
    /// <summary>
    /// Gets the non-negative additional integration cost applied by the flow query.
    /// </summary>
    public Fixed64 ExtraIntegrationCost { get; }

    /// <summary>
    /// Creates immutable flow field query options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the extra integration cost is negative.</exception>
    public FlowFieldQueryOptions(Fixed64 extraIntegrationCost)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            extraIntegrationCost < Fixed64.Zero,
            actualValue: null,
            nameof(extraIntegrationCost),
            "Extra integration cost cannot be negative.");

        ExtraIntegrationCost = extraIntegrationCost;
    }

    /// <inheritdoc/>
    public bool Equals(FlowFieldQueryOptions other) => ExtraIntegrationCost == other.ExtraIntegrationCost;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FlowFieldQueryOptions other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ExtraIntegrationCost.GetHashCode();

    /// <summary>Returns whether two option values are exactly equal.</summary>
    public static bool operator ==(FlowFieldQueryOptions left, FlowFieldQueryOptions right) => left.Equals(right);

    /// <summary>Returns whether two option values differ.</summary>
    public static bool operator !=(FlowFieldQueryOptions left, FlowFieldQueryOptions right) => !left.Equals(right);

}
