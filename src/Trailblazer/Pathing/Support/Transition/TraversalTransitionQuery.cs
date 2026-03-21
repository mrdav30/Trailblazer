using System;
using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides deterministic transition snapshots for planners that need directed traversal edges.
/// </summary>
internal static class TraversalTransitionQuery
{
    public static TraversalTransition[] GetDirectedTransitions()
    {
        TraversalTransition[] transitions = TraversalTransitionRegistry.AllTransitions;
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);

        for (int i = 0; i < transitions.Length; i++)
        {
            directed.Add(transitions[i]);

            if (transitions[i].IsBidirectional)
            {
                directed.Add(new TraversalTransition(
                    transitions[i].Id,
                    transitions[i].Type,
                    transitions[i].Destination,
                    transitions[i].Source,
                    transitions[i].PathCostModifier,
                    transitions[i].IsBidirectional));
            }
        }

        TraversalTransition[] ordered = directed.ToArray();
        Array.Sort(ordered, CompareTransitions);
        return ordered;
    }

    private static int CompareTransitions(TraversalTransition left, TraversalTransition right)
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
        int kindComparison = left.Kind.CompareTo(right.Kind);
        if (kindComparison != 0)
            return kindComparison;

        int volumeComparison = left.VolumeMode.CompareTo(right.VolumeMode);
        if (volumeComparison != 0)
            return volumeComparison;

        return ComparePositions(left.Position, right.Position);
    }

    private static int ComparePositions(Vector3d left, Vector3d right)
    {
        int xComparison = CompareFixed(left.x, right.x);
        if (xComparison != 0)
            return xComparison;

        int yComparison = CompareFixed(left.y, right.y);
        if (yComparison != 0)
            return yComparison;

        return CompareFixed(left.z, right.z);
    }

    private static int CompareFixed(Fixed64 left, Fixed64 right)
    {
        if (left < right)
            return -1;

        if (left > right)
            return 1;

        return 0;
    }
}
