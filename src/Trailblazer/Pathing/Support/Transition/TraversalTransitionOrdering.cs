using System;
using FixedMathSharp;

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
        int spaceComparison = left.Space.CompareTo(right.Space);
        if (spaceComparison != 0)
            return spaceComparison;

        int gridComparison = left.VoxelIndex.GridIndex.CompareTo(right.VoxelIndex.GridIndex);
        if (gridComparison != 0)
            return gridComparison;

        int positionComparison = ComparePositions(left.Position, right.Position);
        if (positionComparison != 0)
            return positionComparison;

        return left.HasPointOverride.CompareTo(right.HasPointOverride);
    }

    private static int ComparePositions(Vector3d left, Vector3d right)
    {
        int xComparison = left.x.CompareTo(right.x);
        if (xComparison != 0)
            return xComparison;

        int yComparison = left.y.CompareTo(right.y);
        if (yComparison != 0)
            return yComparison;

        return left.z.CompareTo(right.z);
    }
}
