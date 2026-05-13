using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Routes external-grid bridge diagnostics and event-signature tracking through the active pathing context.
/// </summary>
internal static class PathManagerExternalGridBridge
{
    private static SwiftDictionary<ushort, ExternalGridEventObservation> _eventObservationsByGridIndex =>
        PathManager.ActiveState.ExternalGridEventObservationsByGridIndex;

    private static SwiftDictionary<ushort, PendingExternalGridChange> _pendingGridChangesByGridIndex =>
        PathManager.ActiveState.PendingGridChangesByGridIndex;

    private static SwiftList<ushort> _pendingGridChangeOrder =>
        PathManager.ActiveState.PendingGridChangeOrder;

    private static int _gridEventsReceived
    {
        get => PathManager.ActiveState.GridEventsReceived;
        set => PathManager.ActiveState.GridEventsReceived = value;
    }

    private static int _gridAddEventsReceived
    {
        get => PathManager.ActiveState.GridAddEventsReceived;
        set => PathManager.ActiveState.GridAddEventsReceived = value;
    }

    private static int _gridRemoveEventsReceived
    {
        get => PathManager.ActiveState.GridRemoveEventsReceived;
        set => PathManager.ActiveState.GridRemoveEventsReceived = value;
    }

    private static int _gridChangeEventsReceived
    {
        get => PathManager.ActiveState.GridChangeEventsReceived;
        set => PathManager.ActiveState.GridChangeEventsReceived = value;
    }

    private static int _distinctObservedGridSlots
    {
        get => PathManager.ActiveState.DistinctObservedGridSlots;
        set => PathManager.ActiveState.DistinctObservedGridSlots = value;
    }

    private static int _duplicateGridEventSignaturesObserved
    {
        get => PathManager.ActiveState.DuplicateGridEventSignaturesObserved;
        set => PathManager.ActiveState.DuplicateGridEventSignaturesObserved = value;
    }

    private static int _duplicateGridAddEventSignaturesObserved
    {
        get => PathManager.ActiveState.DuplicateGridAddEventSignaturesObserved;
        set => PathManager.ActiveState.DuplicateGridAddEventSignaturesObserved = value;
    }

    private static int _duplicateGridRemoveEventSignaturesObserved
    {
        get => PathManager.ActiveState.DuplicateGridRemoveEventSignaturesObserved;
        set => PathManager.ActiveState.DuplicateGridRemoveEventSignaturesObserved = value;
    }

    private static int _duplicateGridChangeEventSignaturesObserved
    {
        get => PathManager.ActiveState.DuplicateGridChangeEventSignaturesObserved;
        set => PathManager.ActiveState.DuplicateGridChangeEventSignaturesObserved = value;
    }

    private static int _maxGridEventStreak
    {
        get => PathManager.ActiveState.MaxGridEventStreak;
        set => PathManager.ActiveState.MaxGridEventStreak = value;
    }

    private static int _gridRebuildPassesExecuted
    {
        get => PathManager.ActiveState.GridRebuildPassesExecuted;
        set => PathManager.ActiveState.GridRebuildPassesExecuted = value;
    }

    private static int _gridEventsIgnoredForNoIntersectingCharts
    {
        get => PathManager.ActiveState.GridEventsIgnoredForNoIntersectingCharts;
        set => PathManager.ActiveState.GridEventsIgnoredForNoIntersectingCharts = value;
    }

    private static int _totalChartsSelectedForGridRebuild
    {
        get => PathManager.ActiveState.TotalChartsSelectedForGridRebuild;
        set => PathManager.ActiveState.TotalChartsSelectedForGridRebuild = value;
    }

    private static int _maxChartsSelectedForSingleGridEvent
    {
        get => PathManager.ActiveState.MaxChartsSelectedForSingleGridEvent;
        set => PathManager.ActiveState.MaxChartsSelectedForSingleGridEvent = value;
    }

    internal static ExternalGridBridgeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new ExternalGridBridgeDiagnosticsSnapshot(
            totalGridEventsReceived: _gridEventsReceived,
            addedEventsReceived: _gridAddEventsReceived,
            removedEventsReceived: _gridRemoveEventsReceived,
            changedEventsReceived: _gridChangeEventsReceived,
            distinctGridSlotsObserved: _distinctObservedGridSlots,
            duplicateEventSignaturesObserved: _duplicateGridEventSignaturesObserved,
            duplicateAddEventSignaturesObserved: _duplicateGridAddEventSignaturesObserved,
            duplicateRemoveEventSignaturesObserved: _duplicateGridRemoveEventSignaturesObserved,
            duplicateChangeEventSignaturesObserved: _duplicateGridChangeEventSignaturesObserved,
            maxIdenticalEventStreak: _maxGridEventStreak,
            rebuildPassesExecuted: _gridRebuildPassesExecuted,
            eventsIgnoredForNoIntersectingCharts: _gridEventsIgnoredForNoIntersectingCharts,
            totalChartsSelectedForRebuild: _totalChartsSelectedForGridRebuild,
            maxChartsSelectedForSingleEvent: _maxChartsSelectedForSingleGridEvent);
    }

    internal static void ResetDiagnostics()
    {
        _eventObservationsByGridIndex.Clear();
        _pendingGridChangesByGridIndex.Clear();
        _pendingGridChangeOrder.Clear();
        _gridEventsReceived = 0;
        _gridAddEventsReceived = 0;
        _gridRemoveEventsReceived = 0;
        _gridChangeEventsReceived = 0;
        _distinctObservedGridSlots = 0;
        _duplicateGridEventSignaturesObserved = 0;
        _duplicateGridAddEventSignaturesObserved = 0;
        _duplicateGridRemoveEventSignaturesObserved = 0;
        _duplicateGridChangeEventSignaturesObserved = 0;
        _maxGridEventStreak = 0;
        _gridRebuildPassesExecuted = 0;
        _gridEventsIgnoredForNoIntersectingCharts = 0;
        _totalChartsSelectedForGridRebuild = 0;
        _maxChartsSelectedForSingleGridEvent = 0;
    }

    internal static void HandleGridAdded(GridEventInfo eventInfo)
    {
        HandleGridChange(eventInfo, ExternalGridEventKind.Added);
    }

    internal static void HandleGridRemoved(GridEventInfo eventInfo)
    {
        HandleGridChange(eventInfo, ExternalGridEventKind.Removed);
    }

    internal static void HandleGridChanged(GridEventInfo eventInfo)
    {
        HandleGridChange(eventInfo, ExternalGridEventKind.Changed);
    }

    internal static void FlushPendingGridChanges()
    {
        if (_pendingGridChangeOrder.Count == 0)
            return;

        int requestCount = 0;
        for (int i = 0; i < _pendingGridChangeOrder.Count; i++)
        {
            ushort gridIndex = _pendingGridChangeOrder[i];
            if (_pendingGridChangesByGridIndex.TryGetValue(gridIndex, out PendingExternalGridChange pendingChange)
                && pendingChange.HasSelectionCriteria)
            {
                requestCount++;
            }
        }

        if (requestCount == 0)
        {
            ClearPendingGridChanges();
            return;
        }

        ExternalGridChartRebuildRequest[] rebuildRequests = new ExternalGridChartRebuildRequest[requestCount];
        int requestIndex = 0;
        for (int i = 0; i < _pendingGridChangeOrder.Count; i++)
        {
            ushort gridIndex = _pendingGridChangeOrder[i];
            if (!_pendingGridChangesByGridIndex.TryGetValue(gridIndex, out PendingExternalGridChange pendingChange)
                || !pendingChange.HasSelectionCriteria)
            {
                continue;
            }

            rebuildRequests[requestIndex++] = pendingChange.ToRebuildRequest(gridIndex);
        }

        ClearPendingGridChanges();
        RecordGridRebuildSelection(PathManager.RebuildInitializedChartsAgainstExternalGridRequests(rebuildRequests));
    }

    private static void HandleGridReset()
    {
        PathManager.Reset();
    }

    private static void HandleGridChange(GridEventInfo eventInfo, ExternalGridEventKind eventKind)
    {
        if (RecordGridEvent(eventInfo, eventKind))
            return;

        QueuePendingGridChange(eventInfo, eventKind);
    }

    private static bool RecordGridEvent(GridEventInfo eventInfo, ExternalGridEventKind eventKind)
    {
        _gridEventsReceived++;

        switch (eventKind)
        {
            case ExternalGridEventKind.Added:
                _gridAddEventsReceived++;
                break;
            case ExternalGridEventKind.Removed:
                _gridRemoveEventsReceived++;
                break;
            default:
                _gridChangeEventsReceived++;
                break;
        }

        ExternalGridEventSignature signature = new(
            eventKind,
            eventInfo.GridSpawnToken,
            eventInfo.GridVersion,
            eventInfo.Configuration,
            eventInfo.BoundsMin,
            eventInfo.BoundsMax);

        if (_eventObservationsByGridIndex.TryGetValue(eventInfo.GridIndex, out ExternalGridEventObservation observation))
        {
            if (observation.Signature.Equals(signature))
            {
                _duplicateGridEventSignaturesObserved++;
                switch (eventKind)
                {
                    case ExternalGridEventKind.Added:
                        _duplicateGridAddEventSignaturesObserved++;
                        break;
                    case ExternalGridEventKind.Removed:
                        _duplicateGridRemoveEventSignaturesObserved++;
                        break;
                    default:
                        _duplicateGridChangeEventSignaturesObserved++;
                        break;
                }

                observation = new ExternalGridEventObservation(signature, observation.IdenticalEventStreak + 1);
                _eventObservationsByGridIndex[eventInfo.GridIndex] = observation;
                if (observation.IdenticalEventStreak > _maxGridEventStreak)
                    _maxGridEventStreak = observation.IdenticalEventStreak;

                return true;
            }
            else
            {
                observation = new ExternalGridEventObservation(signature, identicalEventStreak: 1);
            }
        }
        else
        {
            _distinctObservedGridSlots++;
            observation = new ExternalGridEventObservation(signature, identicalEventStreak: 1);
        }

        _eventObservationsByGridIndex[eventInfo.GridIndex] = observation;
        if (observation.IdenticalEventStreak > _maxGridEventStreak)
            _maxGridEventStreak = observation.IdenticalEventStreak;

        return false;
    }

    private static void RecordGridRebuildSelection(int chartCount)
    {
        if (chartCount <= 0)
        {
            _gridEventsIgnoredForNoIntersectingCharts++;
            return;
        }

        _gridRebuildPassesExecuted++;
        _totalChartsSelectedForGridRebuild += chartCount;
        if (chartCount > _maxChartsSelectedForSingleGridEvent)
            _maxChartsSelectedForSingleGridEvent = chartCount;
    }

    private static void QueuePendingGridChange(GridEventInfo eventInfo, ExternalGridEventKind eventKind)
    {
        ushort gridIndex = eventInfo.GridIndex;
        if (_pendingGridChangesByGridIndex.TryGetValue(gridIndex, out PendingExternalGridChange pendingChange))
        {
            _pendingGridChangesByGridIndex[gridIndex] = MergePendingGridChange(pendingChange, eventInfo, eventKind);
            return;
        }

        _pendingGridChangesByGridIndex[gridIndex] = CreatePendingGridChange(eventInfo, eventKind);
        _pendingGridChangeOrder.Add(gridIndex);
    }

    private static PendingExternalGridChange CreatePendingGridChange(
        GridEventInfo eventInfo,
        ExternalGridEventKind eventKind)
    {
        return new PendingExternalGridChange(
            eventInfo.GridSpawnToken,
            eventInfo.GridVersion,
            eventInfo.BoundsMin,
            eventInfo.BoundsMax,
            requiresLiveGridTouchSelection: eventKind != ExternalGridEventKind.Added,
            requiresAuthoredCellBoundsSelection: eventKind == ExternalGridEventKind.Added);
    }

    private static PendingExternalGridChange MergePendingGridChange(
        PendingExternalGridChange pendingChange,
        GridEventInfo eventInfo,
        ExternalGridEventKind eventKind)
    {
        if (pendingChange.GridSpawnToken != eventInfo.GridSpawnToken)
            return MergePendingGridChangeAcrossSpawnTokens(pendingChange, eventInfo, eventKind);

        return MergePendingGridChangeForSameSpawnToken(pendingChange, eventInfo, eventKind);
    }

    private static PendingExternalGridChange MergePendingGridChangeAcrossSpawnTokens(
        PendingExternalGridChange pendingChange,
        GridEventInfo eventInfo,
        ExternalGridEventKind eventKind)
    {
        return new PendingExternalGridChange(
            eventInfo.GridSpawnToken,
            eventInfo.GridVersion,
            eventInfo.BoundsMin,
            eventInfo.BoundsMax,
            requiresLiveGridTouchSelection: pendingChange.RequiresLiveGridTouchSelection,
            requiresAuthoredCellBoundsSelection: eventKind != ExternalGridEventKind.Removed);
    }

    private static PendingExternalGridChange MergePendingGridChangeForSameSpawnToken(
        PendingExternalGridChange pendingChange,
        GridEventInfo eventInfo,
        ExternalGridEventKind eventKind)
    {
        bool requiresLiveGridTouchSelection = pendingChange.RequiresLiveGridTouchSelection;
        bool requiresAuthoredCellBoundsSelection = pendingChange.RequiresAuthoredCellBoundsSelection;

        switch (eventKind)
        {
            case ExternalGridEventKind.Added:
                requiresAuthoredCellBoundsSelection = true;
                break;

            case ExternalGridEventKind.Changed:
                if (!requiresAuthoredCellBoundsSelection)
                    requiresLiveGridTouchSelection = true;

                break;

            case ExternalGridEventKind.Removed:
                if (requiresAuthoredCellBoundsSelection && !requiresLiveGridTouchSelection)
                    requiresAuthoredCellBoundsSelection = false;
                else
                {
                    requiresLiveGridTouchSelection = true;
                    requiresAuthoredCellBoundsSelection = false;
                }

                break;
        }

        Vector3d boundsMin = eventInfo.BoundsMin;
        Vector3d boundsMax = eventInfo.BoundsMax;
        if (requiresAuthoredCellBoundsSelection)
        {
            boundsMin = MinBounds(pendingChange.BoundsMin, eventInfo.BoundsMin);
            boundsMax = MaxBounds(pendingChange.BoundsMax, eventInfo.BoundsMax);
        }

        return new PendingExternalGridChange(
            eventInfo.GridSpawnToken,
            eventInfo.GridVersion,
            boundsMin,
            boundsMax,
            requiresLiveGridTouchSelection,
            requiresAuthoredCellBoundsSelection);
    }

    private static void ClearPendingGridChanges()
    {
        _pendingGridChangesByGridIndex.Clear();
        _pendingGridChangeOrder.Clear();
    }

    private static Vector3d MinBounds(Vector3d left, Vector3d right)
    {
        return new Vector3d(
            left.x <= right.x ? left.x : right.x,
            left.y <= right.y ? left.y : right.y,
            left.z <= right.z ? left.z : right.z);
    }

    private static Vector3d MaxBounds(Vector3d left, Vector3d right)
    {
        return new Vector3d(
            left.x >= right.x ? left.x : right.x,
            left.y >= right.y ? left.y : right.y,
            left.z >= right.z ? left.z : right.z);
    }

}
