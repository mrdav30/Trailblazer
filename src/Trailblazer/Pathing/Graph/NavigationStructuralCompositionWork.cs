//=======================================================================
// NavigationStructuralCompositionWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Retains the pre-change structural root and a canonical cursor while explicit dependency and
/// affected-component work is debited across fixed-step maintenance boundaries.
/// </summary>
internal sealed class NavigationStructuralCompositionWork
{
    private const long BaseRetainedBytes = 72L;

    private readonly long _batchSequence;
    private readonly int _batchChangeCount;
    private readonly NavigationWorldGraph _sourceGraph;
    private readonly bool _updateComposition;
    private readonly NavigationWorldGraph.StructuralPreparationWork _preparation;
    private NavigationCompositionIndex.UpdateWork? _update;

    internal NavigationStructuralCompositionWork(
        NavigationWorldGraph sourceGraph,
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        bool updateComposition)
    {
        _sourceGraph = sourceGraph;
        _updateComposition = updateComposition;
        _batchChangeCount = changeCount;
        for (int i = 0; i < changeCount; i++)
        {
            if (changes[i].OperationSequence > _batchSequence)
                _batchSequence = changes[i].OperationSequence;
        }

        ChangedMapIds = CaptureChangedMapIds(
            candidate,
            changes,
            changeCount);
        _preparation = new NavigationWorldGraph.StructuralPreparationWork(
            sourceGraph,
            candidate,
            changes,
            changeCount,
            sourceGraph.GraphVersion + 1);
    }

    internal string[] ChangedMapIds { get; }

    internal ReadOnlySpan<string> AffectedMapIds => _update == null
        ? ReadOnlySpan<string>.Empty
        : _update.AffectedMapIds;

    internal bool UpdatesComposition => _updateComposition;

    internal NavigationWorldGraph PreparedGraph => _preparation.Result;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _preparation.RetainedBytes
        + GetUpdateAdditionalRetainedBytes()
        + ((long)(ChangedMapIds.Length + AffectedMapIds.Length) * IntPtr.Size));

    internal int PersistentPageCount => checked(
        1 + _preparation.PersistentPageCount + GetUpdateAdditionalPersistentPages());

    internal bool IsComplete => _preparation.IsComplete
        && (!_updateComposition || (_update?.IsComplete ?? false));

    internal NavigationWorldGraph Result => _updateComposition
        ? PreparedGraph.WithComposition(_update!.Result)
        : PreparedGraph;

    internal int CopiedNodeRecords => _update?.CopiedNodeRecords ?? 0;

    internal int CopiedReverseRecords => _update?.CopiedReverseRecords ?? 0;

    internal int CopiedComponentRecords => _update?.CopiedComponentRecords ?? 0;

    internal int CopiedMembershipRecords => _update?.CopiedMembershipRecords ?? 0;

    private long GetUpdateAdditionalRetainedBytes()
    {
        if (_update == null)
            return 0;
        return Math.Max(0L, _update.RetainedBytes - _sourceGraph.Composition.RetainedBytes);
    }

    private int GetUpdateAdditionalPersistentPages()
    {
        if (_update == null)
            return 0;
        return Math.Max(
            0,
            _update.PersistentPageCount - _sourceGraph.Composition.PersistentPageCount);
    }

    internal bool Matches(NavigationOperationFrameChange[] changes, int changeCount)
    {
        if (changeCount != _batchChangeCount)
            return false;
        long sequence = 0;
        for (int i = 0; i < changeCount; i++)
        {
            if (changes[i].OperationSequence > sequence)
                sequence = changes[i].OperationSequence;
        }
        return sequence == _batchSequence;
    }

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        // Topology-native edges, seams, and cache invalidations enter the same meter in Phases 3
        // and 4; Phase 2 owns only explicit edges, reverse dependencies, and weak components.
        if (!_preparation.IsComplete && !_preparation.Advance(meter))
            return false;
        if (!_updateComposition)
            return true;
        _update ??= PreparedGraph.BeginCompositionUpdate(
            _sourceGraph,
            ChangedMapIds,
            PreparedGraph.GraphVersion);
        return _update.Advance(meter);
    }

    internal static long GetMinimumScratchBytes(
        int sourceMapCount,
        int candidateMapCount,
        int changedMapCount,
        long overlayCellCount)
    {
        long semanticPages = overlayCellCount == 0 ? 0 : ((overlayCellCount + 63) / 64) + 1;
        return checked(
            NavigationCompositionIndex.UpdateWork.GetMinimumScratchBytes(
                sourceMapCount,
                candidateMapCount,
                changedMapCount)
            + 256L
            + ((long)candidateMapCount * 256L)
            + (semanticPages * 4_504L));
    }

    internal static int GetMinimumScratchPages(
        int candidateMapCount,
        long overlayCellCount)
    {
        long semanticPages = overlayCellCount == 0 ? 0 : ((overlayCellCount + 63) / 64) + 1;
        return checked(
            2
            + (candidateMapCount * 2)
            + checked((int)Math.Min(int.MaxValue, semanticPages * 2L)));
    }

    private static string[] CaptureChangedMapIds(
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        int capacity = candidate.ExplicitChangedSourceCount;
        for (int i = 0; i < changeCount; i++)
        {
            capacity = checked(capacity + (changes[i].Kind == NavigationOperationFrameChangeKind.Overlay
                ? changes[i].PreparedOverlay!.Transaction.MapSpan.Length
                : 1));
        }
        var mapIds = new string[capacity];
        int offset = 0;
        for (int i = 0; i < candidate.ExplicitChangedSourceCount; i++)
            mapIds[offset++] = candidate.GetExplicitChangedSourceAt(i);
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
            {
                mapIds[offset++] = change.MapId!;
                continue;
            }
            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                change.PreparedOverlay!.Transaction.MapSpan;
            for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
            {
                NavigationMapOverlayDelta map = maps[mapIndex];
                mapIds[offset++] = map.MapId;
            }
        }
        Array.Sort(mapIds, StringComparer.Ordinal);
        if (mapIds.Length == 0)
            return mapIds;
        int count = 1;
        for (int i = 1; i < mapIds.Length; i++)
        {
            if (!string.Equals(mapIds[count - 1], mapIds[i], StringComparison.Ordinal))
                mapIds[count++] = mapIds[i];
        }
        if (count != mapIds.Length)
            Array.Resize(ref mapIds, count);
        return mapIds;
    }

}
