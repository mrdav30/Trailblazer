//=======================================================================
// NavigationSurfaceComponentKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Identifies one exact weak surface component by its minimum member address.</summary>
internal readonly struct NavigationSurfaceComponentKey :
    IEquatable<NavigationSurfaceComponentKey>,
    IComparable<NavigationSurfaceComponentKey>
{
    internal NavigationSurfaceComponentKey(NavigationCellAddress representative) =>
        Representative = representative;

    internal NavigationCellAddress Representative { get; }

    public int CompareTo(NavigationSurfaceComponentKey other) =>
        Representative.CompareTo(other.Representative);

    public bool Equals(NavigationSurfaceComponentKey other) =>
        Representative.Equals(other.Representative);

    public override bool Equals(object? obj) =>
        obj is NavigationSurfaceComponentKey other && Equals(other);

    public override int GetHashCode() => Representative.GetHashCode();

    public static bool operator ==(
        NavigationSurfaceComponentKey left,
        NavigationSurfaceComponentKey right) => left.Equals(right);

    public static bool operator !=(
        NavigationSurfaceComponentKey left,
        NavigationSurfaceComponentKey right) => !left.Equals(right);
}
