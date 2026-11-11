//=======================================================================
// NavigationPlanarSupport.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

internal readonly struct NavigationPlanarSupport : IEquatable<NavigationPlanarSupport>
{
    internal NavigationPlanarSupport(VoxelIndex index, Vector2d left, Vector2d right, Vector2d normal)
    {
        Index = index;
        Left = left;
        Right = right;
        Normal = normal;
    }

    internal VoxelIndex Index { get; }
    internal Vector2d Left { get; }
    internal Vector2d Right { get; }
    internal Vector2d Normal { get; }

    internal NavigationVolumeAnchorStatus CheckClearance(Vector2d start, Vector2d end, Fixed64 axisLength, Fixed64 radius)
    {
        Vector2d span = Right - Left;
        if (!Vector2d.TrySubtract(start, end, out Vector2d inverseTravel)
            || !Vector2d.TryAdd(span, inverseTravel, out Vector2d farCorner))
            return NavigationVolumeAnchorStatus.CostOverflow;
        // Holding the capsule still and sweeping the support in reverse gives the same overlap.
        Span<Vector2d> hull = stackalloc Vector2d[4] { Vector2d.Zero, span, farCorner, inverseTravel };
        return FixedSegment2d.DoesCenteredCapsulePenetrateConvex(start, Vector2d.Forward, axisLength,
            radius, Left, Fixed64.Zero, hull)
            ? NavigationVolumeAnchorStatus.Unavailable : NavigationVolumeAnchorStatus.Success;
    }

    public bool Equals(NavigationPlanarSupport other) =>
        Index.Equals(other.Index) && Left == other.Left && Right == other.Right;

    public override bool Equals(object? obj) => obj is NavigationPlanarSupport other && Equals(other);

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Index.GetHashCode(), Left.GetHashCode(), Right.GetHashCode());
}
