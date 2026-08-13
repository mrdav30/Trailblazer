//=======================================================================
// NavigationAreaRule.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines one immutable query-specific admission and enter-cost rule.
/// </summary>
public readonly struct NavigationAreaRule : IEquatable<NavigationAreaRule>
{
    private readonly bool _isDenied;

    /// <summary>Creates an immutable navigation-area rule.</summary>
    public NavigationAreaRule(bool isAllowed, Fixed64 additionalEnterCost)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            additionalEnterCost < Fixed64.Zero,
            actualValue: null,
            nameof(additionalEnterCost),
            "Additional enter cost must be non-negative.");

        _isDenied = !isAllowed;
        AdditionalEnterCost = additionalEnterCost;
    }

    /// <summary>Gets whether the query may enter this area.</summary>
    public bool IsAllowed => !_isDenied;

    /// <summary>Gets the non-negative query-specific enter-cost surcharge.</summary>
    public Fixed64 AdditionalEnterCost { get; }

    /// <inheritdoc/>
    public bool Equals(NavigationAreaRule other) =>
        _isDenied == other._isDenied && AdditionalEnterCost == other.AdditionalEnterCost;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationAreaRule other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(_isDenied ? 1 : 0, AdditionalEnterCost.GetHashCode());

    /// <summary>Tests two rules for equality.</summary>
    public static bool operator ==(NavigationAreaRule left, NavigationAreaRule right) => left.Equals(right);

    /// <summary>Tests two rules for inequality.</summary>
    public static bool operator !=(NavigationAreaRule left, NavigationAreaRule right) => !left.Equals(right);
}
