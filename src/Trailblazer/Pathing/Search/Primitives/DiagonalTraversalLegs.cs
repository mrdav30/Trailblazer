//=======================================================================
// DiagonalTraversalLegs.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Maps GridForge axis offsets to the cardinal legs required before diagonal traversal.
/// </summary>
internal static class DiagonalTraversalLegs
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangularDirection ForXOffset(int xOffset)
    {
        return xOffset > 0 ? RectangularDirection.East : RectangularDirection.West;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangularDirection ForYOffset(int yOffset)
    {
        return yOffset > 0 ? RectangularDirection.Above : RectangularDirection.Below;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangularDirection ForZOffset(int zOffset)
    {
        return zOffset > 0 ? RectangularDirection.North : RectangularDirection.South;
    }
}
