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
    internal NavigationSurfaceComponentKey(
        NavigationCellAddress representative,
        TraversalMedium medium)
    {
        Representative = representative;
        Medium = medium;
    }

    internal NavigationCellAddress Representative { get; }

    internal TraversalMedium Medium { get; }

    public int CompareTo(NavigationSurfaceComponentKey other)
    {
        int value = Representative.CompareTo(other.Representative);
        if (value != 0)
            return value;
        int medium = (int)Medium;
        int otherMedium = (int)other.Medium;
        return medium < otherMedium ? -1 : medium > otherMedium ? 1 : 0;
    }

    public bool Equals(NavigationSurfaceComponentKey other) =>
        Representative.Equals(other.Representative) && Medium == other.Medium;

    public override int GetHashCode() => SwiftCollections.Utility.SwiftHashTools.CombineHashCodes(
        Representative.GetHashCode(),
        (int)Medium);

}
