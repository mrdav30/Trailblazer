//=======================================================================
// Navigator2D.SideViewSlope.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <content>Materializes bounded targets in the open strip beside a certified slope.</content>
public abstract partial class Navigator2D
{
    private Vector2d RoundSideViewTarget(Vector2d start, Vector2d end)
    {
        // Use the canonical stage, so subsequent rounded origins cannot change its slope.
        Vector2d delta = end - start;
        bool swap = delta.X.Abs() < delta.Y.Abs();
        Vector2d a = OrderSlopeAxes(start, swap);
        Vector2d b = OrderSlopeAxes(end, swap);
        Vector2d current = OrderSlopeAxes(Pose.Center, swap);
        Vector2d span = b - a;
        int direction = span.X > Fixed64.Zero ? 1 : -1;
        int outward = swap && (delta.X > Fixed64.Zero) == (delta.Y > Fixed64.Zero) ? -1 : 1;
        int openSide = direction * outward;

        Fixed64 distance = Multiply(_motorSettings.MoveSpeed, _context.DeltaTime);
        // Allow for major-coordinate rounding and up to two raw units of minor offset at the origin.
        Fixed64 travel = FixedMath.Max(Fixed64.Zero, distance - Fixed64.FromRaw(4));
        bool lengthFits = span.TryGetMagnitudeCeiling(out Fixed64 length);
        bool progressFits = Fixed64.TryMultiplyDivide(span.X.Abs(), travel, length, out Fixed64 progress);
        Debug.Assert(lengthFits && progressFits);
        Fixed64 remaining = (b.X - current.X).Abs();
        Fixed64 major = current.X + FixedMath.Min(remaining, FixedMath.Max(progress, Fixed64.MinIncrement)) * direction;
        Vector2d rounded = RoundSlopePoint(a, b, major, outward, openSide);

        if (Vector2d.CompareDistanceSquared(current, rounded, Vector2d.Zero, new Vector2d(distance, Fixed64.Zero)) <= 0)
            return OrderSlopeAxes(rounded, swap);

        // With a tiny cap the lattice can require two cardinal moves to reach the same rounded point.
        // This fallback advances one raw unit. If that would leave the strip, adjust the minor coordinate first.
        Vector2d majorOnly = new(major, current.Y);
        Fixed64 offset = (current.Y - rounded.Y) * outward;
        Vector2d candidate = offset >= Fixed64.Zero && offset <= Fixed64.MinIncrement ? majorOnly
            : new Vector2d(current.X, current.Y + (rounded.Y > current.Y ? Fixed64.MinIncrement : -Fixed64.MinIncrement));
        Debug.Assert(Vector2d.CompareDistanceSquared(current, candidate, Vector2d.Zero,
            new Vector2d(distance, Fixed64.Zero)) <= 0);
        return OrderSlopeAxes(candidate, swap);
    }

    private static Vector2d OrderSlopeAxes(Vector2d point, bool swap) => swap ? new Vector2d(point.Y, point.X) : point;

    private static Vector2d RoundSlopePoint(Vector2d a, Vector2d b, Fixed64 major, int outward, int openSide)
    {
        bool minorFits = Fixed64.TryMultiplyDivide(major - a.X, b.Y - a.Y, b.X - a.X, out Fixed64 minorOffset);
        Debug.Assert(minorFits);
        Vector2d rounded = new(major, a.Y + minorOffset);
        if (Vector2d.OrientationSign(a, b, rounded) == -openSide)
            rounded.Y += Fixed64.FromRaw(outward);
        return rounded;
    }
}
