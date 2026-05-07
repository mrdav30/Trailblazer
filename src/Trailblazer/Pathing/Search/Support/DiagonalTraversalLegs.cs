using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Maps GridForge axis offsets to the cardinal legs required before diagonal traversal.
/// </summary>
internal static class DiagonalTraversalLegs
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SpatialDirection ForXOffset(int xOffset)
    {
        return xOffset > 0 ? SpatialDirection.East : SpatialDirection.West;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SpatialDirection ForYOffset(int yOffset)
    {
        return yOffset > 0 ? SpatialDirection.Above : SpatialDirection.Below;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SpatialDirection ForZOffset(int zOffset)
    {
        return zOffset > 0 ? SpatialDirection.North : SpatialDirection.South;
    }
}
