using FixedMathSharp;
using GridForge.Spatial;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides deterministic ordering for transition snapshots exposed by the registry and query layer.
/// </summary>
internal static class TraversalTransitionOrdering
{
    public static void Sort(TraversalTransition[] transitions)
    {
        if (transitions == null || transitions.Length <= 1)
            return;

        Array.Sort(transitions, Compare);
    }

    public static int Compare(TraversalTransition left, TraversalTransition right)
    {
        int idComparison = string.CompareOrdinal(left.Id, right.Id);
        if (idComparison != 0)
            return idComparison;

        int typeComparison = left.Type.CompareTo(right.Type);
        if (typeComparison != 0)
            return typeComparison;

        int sourceComparison = CompareAnchors(left.Source, right.Source);
        if (sourceComparison != 0)
            return sourceComparison;

        int destinationComparison = CompareAnchors(left.Destination, right.Destination);
        if (destinationComparison != 0)
            return destinationComparison;

        int costComparison = left.PathCostModifier.CompareTo(right.PathCostModifier);
        if (costComparison != 0)
            return costComparison;

        return left.IsBidirectional.CompareTo(right.IsBidirectional);
    }

    private static int CompareAnchors(TraversalTransitionAnchor left, TraversalTransitionAnchor right)
    {
        int mediumComparison = left.Medium.CompareTo(right.Medium);
        if (mediumComparison != 0)
            return mediumComparison;

        int voxelComparison = CompareVoxelIndices(left.VoxelIndex, right.VoxelIndex);
        if (voxelComparison != 0)
            return voxelComparison;

        int pointOverrideComparison = left.HasPointOverride.CompareTo(right.HasPointOverride);
        if (pointOverrideComparison != 0)
            return pointOverrideComparison;

        if (!left.HasPointOverride)
            return 0;

        return ComparePositions(left.PointOverride, right.PointOverride);
    }

    private static int CompareVoxelIndices(WorldVoxelIndex left, WorldVoxelIndex right)
    {
        int gridComparison = left.GridIndex.CompareTo(right.GridIndex);
        if (gridComparison != 0)
            return gridComparison;

        int xComparison = left.VoxelIndex.x.CompareTo(right.VoxelIndex.x);
        if (xComparison != 0)
            return xComparison;

        int yComparison = left.VoxelIndex.y.CompareTo(right.VoxelIndex.y);
        if (yComparison != 0)
            return yComparison;

        return left.VoxelIndex.z.CompareTo(right.VoxelIndex.z);
    }

    private static int ComparePositions(Vector3d left, Vector3d right)
    {
        int xComparison = left.X.CompareTo(right.X);
        if (xComparison != 0)
            return xComparison;

        int yComparison = left.Y.CompareTo(right.Y);
        if (yComparison != 0)
            return yComparison;

        return left.Z.CompareTo(right.Z);
    }
}
