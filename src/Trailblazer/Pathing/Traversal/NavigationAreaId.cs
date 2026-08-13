//=======================================================================
// NavigationAreaId.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Identifies one host-defined navigation area independently of registration order.
/// </summary>
public readonly struct NavigationAreaId : IEquatable<NavigationAreaId>, IComparable<NavigationAreaId>
{
    /// <summary>Creates an explicitly assigned host-defined area identifier.</summary>
    public NavigationAreaId(ushort value)
    {
        Value = value;
    }

    /// <summary>Gets the stable numeric identifier. Zero denotes the default area.</summary>
    public ushort Value { get; }

    /// <inheritdoc/>
    public int CompareTo(NavigationAreaId other) => Value.CompareTo(other.Value);

    /// <inheritdoc/>
    public bool Equals(NavigationAreaId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationAreaId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tests two identifiers for equality.</summary>
    public static bool operator ==(NavigationAreaId left, NavigationAreaId right) => left.Equals(right);

    /// <summary>Tests two identifiers for inequality.</summary>
    public static bool operator !=(NavigationAreaId left, NavigationAreaId right) => !left.Equals(right);
}
