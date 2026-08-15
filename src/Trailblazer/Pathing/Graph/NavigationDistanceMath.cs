//=======================================================================
// NavigationDistanceMath.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Provides conservative fixed-point bounds for exact geometric distances.</summary>
internal static class NavigationDistanceMath
{
    internal static bool TryFloor(Vector3d start, Vector3d end, out Fixed64 distance)
    {
        if (!Vector3d.TryGetDistance(start, end, out distance))
            return false;
        var scalar = new Vector3d(distance, Fixed64.Zero, Fixed64.Zero);
        return Vector3d.CompareDistanceSquared(start, end, Vector3d.Zero, scalar) >= 0
            || Fixed64.TrySubtract(distance, Fixed64.MinIncrement, out distance);
    }

    internal static bool TryCeiling(Vector3d start, Vector3d end, out Fixed64 distance)
    {
        if (!Vector3d.TryGetDistance(start, end, out distance))
            return false;
        var scalar = new Vector3d(distance, Fixed64.Zero, Fixed64.Zero);
        return Vector3d.CompareDistanceSquared(start, end, Vector3d.Zero, scalar) <= 0
            || Fixed64.TryAdd(distance, Fixed64.MinIncrement, out distance);
    }
}
