using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Owns the external-grid event bridge for <see cref="PathManager"/>, including diagnostics and
/// event-signature tracking.
/// </summary>
internal static class PathManagerExternalGridBridge
{
    private static readonly SwiftDictionary<ushort, ExternalGridEventObservation> _eventObservationsByGridIndex = new();

    private static readonly SwiftDictionary<ushort, PendingExternalGridChange> _pendingGridChangesByGridIndex = new();

    private static readonly SwiftList<ushort> _pendingGridChangeOrder = new();

    private static int _gridEventsReceived;

    private static int _gridAddEventsReceived;

    private static int _gridRemoveEventsReceived;

    private static int _gridChangeEventsReceived;

    private static int _distinctObservedGridSlots;

    private static int _duplicateGridEventSignaturesObserved;

    private static int _duplicateGridAddEventSignaturesObserved;

    private static int _duplicateGridRemoveEventSignaturesObserved;

    private static int _duplicateGridChangeEventSignaturesObserved;

    private static int _maxGridEventStreak;

    private static int _gridRebuildPassesExecuted;

    private static int _gridEventsIgnoredForNoIntersectingCharts;

    private static int _totalChartsSelectedForGridRebuild;

    private static int _maxChartsSelectedForSingleGridEvent;

    internal static void Register()
    {
        GlobalGridManager.OnReset += HandleGridReset;
        GlobalGridManager.OnActiveGridAdded += HandleGridAdded;
        GlobalGridManager.OnActiveGridRemoved += HandleGridRemoved;
        GlobalGridManager.OnActiveGridChange += HandleGridChanged;
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

    private enum ExternalGridEventKind : byte
    {
        Added,
        Removed,
        Changed
    }

    private readonly struct ExternalGridEventSignature : IEquatable<ExternalGridEventSignature>
    {
        public ExternalGridEventSignature(
            ExternalGridEventKind eventKind,
            int gridSpawnToken,
            uint gridVersion,
            GridConfiguration configuration,
            Vector3d boundsMin,
            Vector3d boundsMax)
        {
            EventKind = eventKind;
            GridSpawnToken = gridSpawnToken;
            GridVersion = gridVersion;
            Configuration = configuration;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
        }

        public ExternalGridEventKind EventKind { get; }

        public int GridSpawnToken { get; }

        public uint GridVersion { get; }

        public GridConfiguration Configuration { get; }

        public Vector3d BoundsMin { get; }

        public Vector3d BoundsMax { get; }

        public bool Equals(ExternalGridEventSignature other)
        {
            return EventKind == other.EventKind
                && GridSpawnToken == other.GridSpawnToken
                && GridVersion == other.GridVersion
                && Configuration.Equals(other.Configuration)
                && BoundsMin == other.BoundsMin
                && BoundsMax == other.BoundsMax;
        }
    }

    private readonly struct ExternalGridEventObservation
    {
        public ExternalGridEventObservation(
            ExternalGridEventSignature signature,
            int identicalEventStreak)
        {
            Signature = signature;
            IdenticalEventStreak = identicalEventStreak;
        }

        public ExternalGridEventSignature Signature { get; }

        public int IdenticalEventStreak { get; }
    }

    private readonly struct PendingExternalGridChange
    {
        public PendingExternalGridChange(
            int gridSpawnToken,
            uint gridVersion,
            Vector3d boundsMin,
            Vector3d boundsMax,
            bool requiresLiveGridTouchSelection,
            bool requiresAuthoredCellBoundsSelection)
        {
            GridSpawnToken = gridSpawnToken;
            GridVersion = gridVersion;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            RequiresLiveGridTouchSelection = requiresLiveGridTouchSelection;
            RequiresAuthoredCellBoundsSelection = requiresAuthoredCellBoundsSelection;
        }

        public int GridSpawnToken { get; }

        public uint GridVersion { get; }

        public Vector3d BoundsMin { get; }

        public Vector3d BoundsMax { get; }

        public bool RequiresLiveGridTouchSelection { get; }

        public bool RequiresAuthoredCellBoundsSelection { get; }

        public bool HasSelectionCriteria => RequiresLiveGridTouchSelection || RequiresAuthoredCellBoundsSelection;

        public ExternalGridChartRebuildRequest ToRebuildRequest(ushort gridIndex)
        {
            return new ExternalGridChartRebuildRequest(
                gridIndex,
                BoundsMin,
                BoundsMax,
                RequiresLiveGridTouchSelection,
                RequiresAuthoredCellBoundsSelection);
        }
    }
}
