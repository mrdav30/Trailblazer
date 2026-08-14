//=======================================================================
// NavigationStructuralCompositionWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Retains the pre-change structural root and a canonical cursor while explicit dependency and
/// affected-component work is debited across fixed-step maintenance boundaries.
/// </summary>
internal sealed class NavigationStructuralCompositionWork
{
    private const long BaseRetainedBytes = 112L;

    private readonly GridWorld _world;
    private readonly int _batchChangeCount;
    private readonly NavigationWorldGraph _sourceGraph;
    private readonly NavigationOperationCandidate _candidate;
    private readonly NavigationOperationFrameChange[] _changes;
    private readonly bool _updateComposition;
    private readonly NavigationCompositionWorkspace _workspace;
    private PersistentStringMap<bool> _changedMapIds = PersistentStringMap<bool>.Empty;
    private PersistentStringMap<bool> _affectedComponents = PersistentStringMap<bool>.Empty;
    private PersistentStringMap<bool> _publishedAffectedComponents =
        PersistentStringMap<bool>.Empty;
    private PersistentStringMap<bool> _operationChangedMapIds =
        PersistentStringMap<bool>.Empty;
    private PersistentStringMap<bool> _operationAffectedComponents =
        PersistentStringMap<bool>.Empty;
    private NavigationWorldGraph.StructuralPreparationWork? _preparation;
    private NavigationAutomaticSeamRefreshWork? _seamRefresh;
    private NavigationWorldGraph? _preparedGraph;
    private NavigationCompositionIndex.UpdateWork? _update;
    private string? _pendingMapId;
    private string? _pendingComponentKey;
    private int _explicitSourceIndex;
    private int _changeIndex;
    private int _overlayIndex;
    private int _capturePhase;
    private int _seamSourceIndex;
    private int _seamCapturePhase;
    private string? _pendingSeamMapId;
    private string? _pendingSeamComponentKey;
    private bool _operationCaptureSnapshotted;
    private bool _seamCaptureComplete;
    private bool _allClosePublished;
    private bool _affectedClosurePublished;
    private bool _allCloseRepublishRequired;

    internal NavigationStructuralCompositionWork(
        GridWorld world,
        NavigationWorldGraph sourceGraph,
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        bool updateComposition,
        NavigationCompositionWorkspace workspace)
    {
        _world = world;
        _sourceGraph = sourceGraph;
        _candidate = candidate;
        _changes = changes;
        _updateComposition = updateComposition;
        _workspace = workspace;
        _workspace.Reset();
        _batchChangeCount = changeCount;
    }

    internal int CapturedChangedMapCount => _changedMapIds.Count;

    internal bool IsChangedMapCaptureComplete => _capturePhase == 4
        && _seamCaptureComplete;

    internal PersistentStringMap<bool> AffectedComponents => _affectedComponents;

    internal bool RequiresAffectedClosurePublication => _updateComposition
        && _allClosePublished
        && IsChangedMapCaptureComplete
        && !_affectedClosurePublished;

    internal bool RequiresAllClosePublication => _allCloseRepublishRequired;

    internal string GetCapturedChangedMapIdAt(int ordinal) =>
        _changedMapIds.GetKeyAt(ordinal);

    internal bool UpdatesComposition => _updateComposition;

    internal NavigationWorldGraph PreparedGraph => _preparedGraph!;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _changedMapIds.RetainedBytes
        + Math.Max(
            0L,
            _affectedComponents.RetainedBytes - _publishedAffectedComponents.RetainedBytes)
        + (_preparation?.RetainedBytes ?? 0)
        + (_seamRefresh?.RetainedBytes ?? 0)
        + GetUpdateAdditionalRetainedBytes()
        );

    internal int PersistentPageCount => checked(
        1
        + _changedMapIds.PersistentNodeCount
        + Math.Max(
            0,
            _affectedComponents.PersistentNodeCount
                - _publishedAffectedComponents.PersistentNodeCount)
        + (_preparation?.PersistentPageCount ?? 0)
        + (_seamRefresh?.PersistentPageCount ?? 0)
        + GetUpdateAdditionalPersistentPages());

    internal bool IsComplete => IsChangedMapCaptureComplete
        && (_preparation?.IsComplete ?? false)
        && (_seamRefresh?.IsComplete ?? false)
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
        return checked(
            Math.Max(
                0L,
                _update.NonPayloadRetainedBytes
                    - _sourceGraph.Composition.RootAndValueRetainedBytes)
            + _update.PayloadAdditionalRetainedBytes);
    }

    private int GetUpdateAdditionalPersistentPages()
    {
        if (_update == null)
            return 0;
        return checked(
            Math.Max(
                0,
                _update.NonPayloadPersistentPageCount
                    - _sourceGraph.Composition.PersistentPageCount)
            + _update.PayloadAdditionalPersistentPages);
    }

    internal bool Matches(NavigationOperationFrameChange[] changes, int changeCount)
    {
        if (changeCount != _batchChangeCount)
            return false;
        return ReferenceEquals(changes, _changes);
    }

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        return Advance(
            meter,
            long.MaxValue,
            int.MaxValue,
            out _);
    }

    internal bool Advance(
        MaintenanceWorkMeter meter,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        out bool capacityExceeded)
    {
        capacityExceeded = false;
        if (_capturePhase != 4 && !AdvanceChangedMapCapture(meter))
            return false;
        if (!_operationCaptureSnapshotted)
        {
            _operationChangedMapIds = _changedMapIds;
            _operationAffectedComponents = _affectedComponents;
            _operationCaptureSnapshotted = true;
        }
        _preparation ??= new NavigationWorldGraph.StructuralPreparationWork(
            _sourceGraph,
            _candidate,
            _changes,
            _batchChangeCount,
            _changedMapIds,
            _sourceGraph.GraphVersion + 1);
        // Topology-native edges, seams, and cache invalidations enter the same meter in Phases 3
        // and 4; Phase 2 owns only explicit edges, reverse dependencies, and weak components.
        if (!_preparation.IsComplete && !_preparation.Advance(meter))
            return false;
        _seamRefresh ??= new NavigationAutomaticSeamRefreshWork(
            _world,
            _sourceGraph,
            _preparation.Result,
            _changes,
            _batchChangeCount);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
        {
            capacityExceeded = true;
            return false;
        }
        while (!_seamRefresh.IsComplete)
        {
            long seamRevision = _seamRefresh.Revision;
            NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus seamStatus =
                _seamRefresh.AdvanceOne(meter);
            if (_seamRefresh.Revision != seamRevision)
            {
                ResetSeamState();
                return false;
            }
            if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
            {
                capacityExceeded = true;
                return false;
            }
            if (seamStatus == NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus.Blocked)
                return false;
        }
        if (!_seamCaptureComplete && !AdvanceSeamChangedMapCapture(meter))
            return false;
        _preparedGraph ??= _preparation.Result.WithAutomaticSeams(_seamRefresh.Result);
        if (RequiresAffectedClosurePublication || RequiresAllClosePublication)
            return false;
        if (!_updateComposition)
            return true;
        _update ??= PreparedGraph.BeginCompositionUpdate(
            _sourceGraph,
            _changedMapIds,
            _preparation.CompositionChanged
                || _seamRefresh.StructuralLinksChanged
                ? PreparedGraph.GraphVersion
                : _sourceGraph.Composition.Version,
            PreparedGraph.GraphVersion,
            _workspace);
        bool complete = _update.Advance(meter);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
        {
            capacityExceeded = true;
            return false;
        }
        return complete;
    }

    private bool ExceedsCapacity(long maximumRetainedBytes, int maximumPersistentPages) =>
        RetainedBytes > maximumRetainedBytes
        || PersistentPageCount > maximumPersistentPages;

    internal void MarkAffectedClosurePublished()
    {
        _publishedAffectedComponents = _affectedComponents;
        _affectedClosurePublished = true;
    }

    internal void MarkAllClosePublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
    }

    internal void MarkAllCloseRepublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
        _affectedClosurePublished = false;
        _publishedAffectedComponents = PersistentStringMap<bool>.Empty;
    }

    internal bool RevalidateAutomaticSeamsForPublication()
    {
        if (_seamRefresh == null || !_seamCaptureComplete)
            return true;
        long revision = _seamRefresh.Revision;
        if (_seamRefresh.RevalidateForPublication())
            return true;
        if (_seamRefresh.Revision != revision)
            ResetSeamState();
        return false;
    }

    internal void AdoptPublishedAffectedClosure(PersistentStringMap<bool> components)
    {
        _affectedComponents = components;
        _publishedAffectedComponents = components;
        _affectedClosurePublished = true;
    }

    private bool AdvanceChangedMapCapture(MaintenanceWorkMeter meter)
    {
        while (_capturePhase < 4)
        {
            if (_capturePhase == 0)
            {
                if (!HasNextRawMapId())
                {
                    _capturePhase = 4;
                    return true;
                }
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _pendingMapId = TakeNextRawMapId();
                string pendingMapId = _pendingMapId!;
                _pendingComponentKey = _sourceGraph.Composition.TryGetComponentKey(
                    pendingMapId,
                    out string componentKey)
                    ? componentKey
                    : null;
                _capturePhase = 1;
            }
            if (_capturePhase == 1)
            {
                if (!_changedMapIds.ContainsKey(_pendingMapId!))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _changedMapIds = _changedMapIds.Set(_pendingMapId!, true);
                }
                _capturePhase = 2;
            }
            if (_capturePhase == 2)
            {
                if (_pendingComponentKey != null
                    && !_affectedComponents.ContainsKey(_pendingComponentKey))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _affectedComponents = _affectedComponents.Set(_pendingComponentKey, true);
                }
                _pendingMapId = null;
                _pendingComponentKey = null;
                _capturePhase = 0;
            }
        }
        return true;
    }

    private bool AdvanceSeamChangedMapCapture(MaintenanceWorkMeter meter)
    {
        while (_seamSourceIndex < _seamRefresh!.ChangedMapCount || _seamCapturePhase != 0)
        {
            if (_seamCapturePhase == 0)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _pendingSeamMapId = _seamRefresh.GetChangedMapIdAt(_seamSourceIndex++);
                _pendingSeamComponentKey = _sourceGraph.Composition.TryGetComponentKey(
                    _pendingSeamMapId,
                    out string componentKey)
                    ? componentKey
                    : null;
                _seamCapturePhase = 1;
            }
            if (_seamCapturePhase == 1)
            {
                if (!_changedMapIds.ContainsKey(_pendingSeamMapId!))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _changedMapIds = _changedMapIds.Set(_pendingSeamMapId!, true);
                }
                _seamCapturePhase = 2;
            }
            if (_seamCapturePhase == 2)
            {
                if (_pendingSeamComponentKey != null
                    && !_affectedComponents.ContainsKey(_pendingSeamComponentKey))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _affectedComponents = _affectedComponents.Set(
                        _pendingSeamComponentKey,
                        true);
                }
                _pendingSeamMapId = null;
                _pendingSeamComponentKey = null;
                _seamCapturePhase = 0;
            }
        }
        _seamCaptureComplete = true;
        return true;
    }

    private void ResetSeamState()
    {
        _changedMapIds = _operationChangedMapIds;
        _affectedComponents = _operationAffectedComponents;
        _seamSourceIndex = 0;
        _seamCapturePhase = 0;
        _pendingSeamMapId = null;
        _pendingSeamComponentKey = null;
        _seamCaptureComplete = false;
        _preparation = null;
        _seamRefresh = null;
        _preparedGraph = null;
        _update = null;
        if (_affectedClosurePublished)
            _allCloseRepublishRequired = true;
        _affectedClosurePublished = false;
        _publishedAffectedComponents = PersistentStringMap<bool>.Empty;
    }

    private bool HasNextRawMapId() =>
        _explicitSourceIndex < _candidate.ExplicitChangedSourceCount
        || _changeIndex < _batchChangeCount;

    private string TakeNextRawMapId()
    {
        if (_explicitSourceIndex < _candidate.ExplicitChangedSourceCount)
            return _candidate.GetExplicitChangedSourceAt(_explicitSourceIndex++);
        NavigationOperationFrameChange change = _changes[_changeIndex];
        if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
        {
            _changeIndex++;
            return change.MapId!;
        }
        ReadOnlySpan<NavigationMapOverlayDelta> maps =
            change.PreparedOverlay!.Transaction.MapSpan;
        string mapId = maps[_overlayIndex++].MapId;
        if (_overlayIndex == maps.Length)
        {
            _overlayIndex = 0;
            _changeIndex++;
        }
        return mapId;
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
            + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes
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
            2 + 4
            + (candidateMapCount * 2)
            + checked((int)Math.Min(int.MaxValue, semanticPages * 2L)));
    }

}
