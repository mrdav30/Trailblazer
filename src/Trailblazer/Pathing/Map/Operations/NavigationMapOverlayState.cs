//=======================================================================
// NavigationMapOverlayState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

internal sealed class NavigationMapOverlayState
{
    internal static readonly NavigationMapOverlayState Empty = new(
        Array.Empty<NavigationCellOverlayOperation>(),
        Array.Empty<NavigationConnectionOverlayOperation>(),
        Array.Empty<TraversalTransitionOverlayOperation>(),
        highWaterSequence: 0);

    private NavigationMapOverlayState(
        NavigationCellOverlayOperation[] cells,
        NavigationConnectionOverlayOperation[] connections,
        TraversalTransitionOverlayOperation[] transitions,
        long highWaterSequence)
    {
        Cells = cells;
        Connections = connections;
        Transitions = transitions;
        HighWaterSequence = highWaterSequence;
    }

    internal NavigationCellOverlayOperation[] Cells { get; }

    internal NavigationConnectionOverlayOperation[] Connections { get; }

    internal TraversalTransitionOverlayOperation[] Transitions { get; }

    internal long HighWaterSequence { get; }

    internal NavigationMapOverlayState Apply(NavigationMapOverlayDelta delta, long operationSequence)
    {
        return new NavigationMapOverlayState(
            MergeCells(Cells, delta.CellSpan),
            MergeConnections(Connections, delta.ConnectionSpan),
            MergeTransitions(Transitions, delta.TransitionSpan),
            operationSequence);
    }

    private static NavigationCellOverlayOperation[] MergeCells(
        NavigationCellOverlayOperation[] current,
        ReadOnlySpan<NavigationCellOverlayOperation> changes)
    {
        var result = new NavigationCellOverlayOperation[current.Length + changes.Length];
        int currentIndex = 0;
        int changeIndex = 0;
        int resultCount = 0;

        while (currentIndex < current.Length || changeIndex < changes.Length)
        {
            int comparison = currentIndex >= current.Length
                ? 1
                : changeIndex >= changes.Length
                    ? -1
                    : current[currentIndex].Index.CompareTo(changes[changeIndex].Index);

            if (comparison < 0)
            {
                result[resultCount++] = current[currentIndex++];
                continue;
            }

            NavigationCellOverlayOperation change = changes[changeIndex++];
            if (comparison == 0)
                currentIndex++;
            if (change.Kind != NavigationCellOverlayOperationKind.RevertToBake)
                result[resultCount++] = change;
        }

        return Trim(result, resultCount);
    }

    private static NavigationConnectionOverlayOperation[] MergeConnections(
        NavigationConnectionOverlayOperation[] current,
        ReadOnlySpan<NavigationConnectionOverlayOperation> changes)
    {
        var result = new NavigationConnectionOverlayOperation[current.Length + changes.Length];
        int currentIndex = 0;
        int changeIndex = 0;
        int resultCount = 0;

        while (currentIndex < current.Length || changeIndex < changes.Length)
        {
            int comparison = currentIndex >= current.Length
                ? 1
                : changeIndex >= changes.Length
                    ? -1
                    : string.CompareOrdinal(current[currentIndex].Id, changes[changeIndex].Id);

            if (comparison < 0)
            {
                result[resultCount++] = current[currentIndex++];
                continue;
            }

            NavigationConnectionOverlayOperation change = changes[changeIndex++];
            if (comparison == 0)
                currentIndex++;
            if (change.Kind != NavigationConnectionOverlayOperationKind.RevertToBake)
                result[resultCount++] = change;
        }

        return Trim(result, resultCount);
    }

    private static TraversalTransitionOverlayOperation[] MergeTransitions(
        TraversalTransitionOverlayOperation[] current,
        ReadOnlySpan<TraversalTransitionOverlayOperation> changes)
    {
        var result = new TraversalTransitionOverlayOperation[current.Length + changes.Length];
        int currentIndex = 0;
        int changeIndex = 0;
        int resultCount = 0;

        while (currentIndex < current.Length || changeIndex < changes.Length)
        {
            int comparison = currentIndex >= current.Length
                ? 1
                : changeIndex >= changes.Length
                    ? -1
                    : string.CompareOrdinal(current[currentIndex].Id, changes[changeIndex].Id);

            if (comparison < 0)
            {
                result[resultCount++] = current[currentIndex++];
                continue;
            }

            TraversalTransitionOverlayOperation change = changes[changeIndex++];
            if (comparison == 0)
                currentIndex++;
            if (change.Kind != TraversalTransitionOverlayOperationKind.RevertToBake)
                result[resultCount++] = change;
        }

        return Trim(result, resultCount);
    }

    private static T[] Trim<T>(T[] values, int count)
    {
        if (count == values.Length)
            return values;
        if (count == 0)
            return Array.Empty<T>();

        var trimmed = new T[count];
        Array.Copy(values, trimmed, count);
        return trimmed;
    }
}
