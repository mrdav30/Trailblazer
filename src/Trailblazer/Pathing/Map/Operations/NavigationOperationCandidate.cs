//=======================================================================
// NavigationOperationCandidate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationOperationCandidate
{
    private readonly int _navigationAreaCount;
    private PersistentStringMap<MapState> _maps = PersistentStringMap<MapState>.Empty;
    private PersistentStringMap<long> _bakeVersionHighWater = PersistentStringMap<long>.Empty;
    private PersistentStringMap<PersistentStringMap<bool>> _incomingSources =
        PersistentStringMap<PersistentStringMap<bool>>.Empty;
    private PersistentGridConfigurationMap<string> _gridBindings = PersistentGridConfigurationMap<string>.Empty;
    private NavigationExplicitConnectionIndex _explicitConnections =
        NavigationExplicitConnectionIndex.Empty;
    private PersistentStringMap<bool> _explicitChangedSources =
        PersistentStringMap<bool>.Empty;
    private NavigationConnectionOwnerKeySet _explicitChangedOwners =
        NavigationConnectionOwnerKeySet.Empty;
    private long _overlaySlotCount;
    private long _overlayConnectionCount;
    private long _overlayTransitionCount;
    private long _transitionRuleCount;
    private long _seamCandidateCount;
    private long _explicitEdgeCount;
    private int _dynamicCellCount;
    private long _mapStateRetainedBytes;
    private int _mapStatePersistentPages;
    private long _incomingSetRetainedBytes;
    private int _incomingSetPersistentPages;
    private long _workCopiedPersistentBytes;
    private int _workCopiedPersistentPages;
    private NavigationExplicitConnectionIndex _workPublishedExplicitConnections =
        NavigationExplicitConnectionIndex.Empty;
    private PersistentStringMap<MapState> _workPublishedMaps = PersistentStringMap<MapState>.Empty;
    private long _workOwnedExplicitPayloadBytes;
    private int _workOwnedExplicitPayloadPages;
    private long _workOwnedMapStatePayloadBytes;
    private int _workOwnedMapStatePayloadPages;

    internal NavigationOperationCandidate(int navigationAreaCount = ushort.MaxValue + 1)
    {
        _navigationAreaCount = navigationAreaCount;
    }

    internal int MapCount => _maps.Count;

    internal long OverlayCellCount => _overlaySlotCount;

    internal long OverlayConnectionCount => _overlayConnectionCount;

    internal long OverlayTransitionCount => _overlayTransitionCount;

    internal long TransitionRuleCount => _transitionRuleCount;

    internal int NavigationAreaCount => _navigationAreaCount;

    internal long RetainedBytes => checked(
        112L
        + _maps.RetainedBytes
        + _bakeVersionHighWater.RetainedBytes
        + _incomingSources.RetainedBytes
        + _gridBindings.RetainedBytes
        + _explicitConnections.RetainedBytes
        + _explicitChangedSources.RetainedBytes
        + (ReferenceEquals(_explicitChangedOwners, NavigationConnectionOwnerKeySet.Empty)
            ? 0L
            : _explicitChangedOwners.RetainedBytes)
        + _mapStateRetainedBytes
        + _incomingSetRetainedBytes);

    internal int PersistentPageCount => checked(
        4
        + _maps.PersistentNodeCount
        + _bakeVersionHighWater.PersistentNodeCount
        + _incomingSources.PersistentNodeCount
        + _gridBindings.Count
        + _explicitConnections.PersistentPageCount
        + _explicitChangedSources.PersistentNodeCount
        + (ReferenceEquals(_explicitChangedOwners, NavigationConnectionOwnerKeySet.Empty)
            ? 0
            : _explicitChangedOwners.PersistentPageCount)
        + _mapStatePersistentPages
        + _incomingSetPersistentPages);

    internal long WorkCopiedPersistentBytes => _workCopiedPersistentBytes;

    internal int WorkCopiedPersistentPages => _workCopiedPersistentPages;

    internal long WorkOwnedExplicitPayloadBytes => _workOwnedExplicitPayloadBytes;

    internal int WorkOwnedExplicitPayloadPages => _workOwnedExplicitPayloadPages;

    internal long WorkOwnedMapStatePayloadBytes => _workOwnedMapStatePayloadBytes;

    internal int WorkOwnedMapStatePayloadPages => _workOwnedMapStatePayloadPages;

    internal long NonPayloadRetainedBytes => checked(
        RetainedBytes
        - _explicitConnections.PayloadRetainedBytes
        - _mapStateRetainedBytes);

    internal int NonPayloadPersistentPageCount => checked(
        PersistentPageCount
        - _explicitConnections.PayloadPersistentPageCount
        - _mapStatePersistentPages);

    internal void ResetWorkCopiedPersistentOwnership()
    {
        _workCopiedPersistentBytes = 0;
        _workCopiedPersistentPages = 0;
        _workOwnedExplicitPayloadBytes = 0;
        _workOwnedExplicitPayloadPages = 0;
        _workOwnedMapStatePayloadBytes = 0;
        _workOwnedMapStatePayloadPages = 0;
        _workPublishedExplicitConnections = _explicitConnections;
        _workPublishedMaps = _maps;
        _explicitChangedSources = PersistentStringMap<bool>.Empty;
        _explicitChangedOwners = NavigationConnectionOwnerKeySet.Empty;
    }

    internal void RecordPersistentCopies(int copiedNodes, long bytesPerNode = 64L)
    {
        _workCopiedPersistentPages = checked(_workCopiedPersistentPages + copiedNodes);
        _workCopiedPersistentBytes = checked(
            _workCopiedPersistentBytes + (copiedNodes * bytesPerNode));
    }

    internal void RecordExplicitRecordOwnership(
        NavigationConnectionOwnerKey owner,
        NavigationExplicitConnectionRecord? next,
        NavigationExplicitConnectionIndex foldSource,
        ref long displacedBytes,
        ref int displacedPages)
    {
        _explicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord prior);
        _workPublishedExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord published);
        foldSource.TryGet(owner, out NavigationExplicitConnectionRecord source);
        ReplaceCurrentPayloadOwnership(
            prior,
            next,
            published,
            ref _workOwnedExplicitPayloadBytes,
            ref _workOwnedExplicitPayloadPages);
        ReplaceDisplacedPayloadOwnership(
            prior,
            next,
            published,
            source,
            ref displacedBytes,
            ref displacedPages);
    }

    internal void RecordExplicitIncidenceOwnership(
        NavigationCellAddress address,
        NavigationPagedSequence<NavigationConnectionOwnerKey> next,
        NavigationExplicitConnectionIndex foldSource,
        ref long displacedBytes,
        ref int displacedPages)
    {
        NavigationPagedSequence<NavigationConnectionOwnerKey> prior =
            _explicitConnections.GetIncidentOwnerRow(address);
        NavigationPagedSequence<NavigationConnectionOwnerKey> published =
            _workPublishedExplicitConnections.GetIncidentOwnerRow(address);
        NavigationPagedSequence<NavigationConnectionOwnerKey> source =
            foldSource.GetIncidentOwnerRow(address);
        ReplaceCurrentPayloadOwnership(
            prior,
            next,
            published,
            ref _workOwnedExplicitPayloadBytes,
            ref _workOwnedExplicitPayloadPages);
        ReplaceDisplacedPayloadOwnership(
            prior,
            next,
            published,
            source,
            ref displacedBytes,
            ref displacedPages);
    }

    internal void RecordExplicitEndpointOwnership(
        NavigationCellAddress address,
        NavigationPagedSequence<NavigationConnectionOwnerKey> next,
        NavigationExplicitConnectionIndex foldSource,
        ref long displacedBytes,
        ref int displacedPages)
    {
        NavigationPagedSequence<NavigationConnectionOwnerKey> prior =
            _explicitConnections.GetEndpointOwnerRow(address);
        NavigationPagedSequence<NavigationConnectionOwnerKey> published =
            _workPublishedExplicitConnections.GetEndpointOwnerRow(address);
        NavigationPagedSequence<NavigationConnectionOwnerKey> source =
            foldSource.GetEndpointOwnerRow(address);
        ReplaceCurrentPayloadOwnership(
            prior,
            next,
            published,
            ref _workOwnedExplicitPayloadBytes,
            ref _workOwnedExplicitPayloadPages);
        ReplaceDisplacedPayloadOwnership(
            prior,
            next,
            published,
            source,
            ref displacedBytes,
            ref displacedPages);
    }

    private static void ReplaceCurrentPayloadOwnership<T>(
        T? prior,
        T? next,
        T? published,
        ref long bytes,
        ref int pages)
        where T : class
    {
        GetPayloadOwnership(prior, published, out long priorBytes, out int priorPages);
        GetPayloadOwnership(next, published, out long nextBytes, out int nextPages);
        bytes = checked(bytes - priorBytes + nextBytes);
        pages = checked(pages - priorPages + nextPages);
    }

    private static void ReplaceDisplacedPayloadOwnership<T>(
        T? prior,
        T? next,
        T? published,
        T? source,
        ref long bytes,
        ref int pages)
        where T : class
    {
        if (source == null || ReferenceEquals(source, published))
            return;
        if (ReferenceEquals(prior, source) && !ReferenceEquals(next, source))
        {
            GetPayloadSize(source, out long sourceBytes, out int sourcePages);
            bytes = checked(bytes + sourceBytes);
            pages = checked(pages + sourcePages);
        }
        else if (!ReferenceEquals(prior, source) && ReferenceEquals(next, source))
        {
            GetPayloadSize(source, out long sourceBytes, out int sourcePages);
            bytes = checked(bytes - sourceBytes);
            pages = checked(pages - sourcePages);
        }
    }

    private static void GetPayloadOwnership<T>(
        T? value,
        T? published,
        out long bytes,
        out int pages)
        where T : class
    {
        if (value == null || ReferenceEquals(value, published))
        {
            bytes = 0;
            pages = 0;
            return;
        }
        GetPayloadSize(value, out bytes, out pages);
    }

    private static void GetPayloadSize<T>(T value, out long bytes, out int pages)
        where T : class
    {
        if (value is NavigationExplicitConnectionRecord record)
        {
            bytes = record.RetainedBytes;
            pages = record.PersistentPageCount;
            return;
        }
        var row = (NavigationPagedSequence<NavigationConnectionOwnerKey>)(object)value;
        bytes = row.RetainedBytes;
        pages = row.PersistentPageCount;
    }

    internal void RecordMapStateOwnership(
        string mapId,
        MapState? next,
        NavigationOperationCandidate foldSource,
        ref long displacedBytes,
        ref int displacedPages)
    {
        _maps.TryGetValue(mapId, out MapState prior);
        _workPublishedMaps.TryGetValue(mapId, out MapState published);
        foldSource._maps.TryGetValue(mapId, out MapState source);
        GetAdditionalMapStatePayload(prior, published, out long priorBytes, out int priorPages);
        GetAdditionalMapStatePayload(next, published, out long nextBytes, out int nextPages);
        _workOwnedMapStatePayloadBytes = checked(
            _workOwnedMapStatePayloadBytes - priorBytes + nextBytes);
        _workOwnedMapStatePayloadPages = checked(
            _workOwnedMapStatePayloadPages - priorPages + nextPages);
        ReplaceDisplacedMapStateOwnership(
            prior,
            next,
            published,
            source,
            ref displacedBytes,
            ref displacedPages);
    }

    internal static void GetAdditionalMapStatePayload(
        MapState? value,
        MapState? shared,
        out long bytes,
        out int pages)
    {
        bytes = 0;
        pages = 0;
        if (value == null)
            return;
        if (shared == null || !ReferenceEquals(value.Map, shared.Map))
        {
            bytes = checked(
                bytes + value.PreparedMapRetainedBytes - value.BakedCellLookup.RetainedBytes);
        }
        if (shared == null || !ReferenceEquals(value.BakedCellLookup, shared.BakedCellLookup))
            bytes = checked(bytes + value.BakedCellLookup.RetainedBytes);
        if (shared == null || !ReferenceEquals(value.Overlay, shared.Overlay))
        {
            bytes = checked(bytes + value.Overlay.RetainedBytes);
            pages = checked(pages + value.Overlay.PersistentNodeCount);
        }
        if (shared == null || !ReferenceEquals(value.DynamicAddresses, shared.DynamicAddresses))
        {
            bytes = checked(bytes + value.DynamicAddresses.RetainedBytes);
            pages = checked(pages + value.DynamicAddresses.PersistentNodeCount);
        }
    }

    private static void ReplaceDisplacedMapStateOwnership(
        MapState? prior,
        MapState? next,
        MapState? published,
        MapState? source,
        ref long bytes,
        ref int pages)
    {
        if (source == null)
            return;
        ReplaceDisplacedMapStateComponent(
            prior?.Map,
            next?.Map,
            published?.Map,
            source.Map,
            source.PreparedMapRetainedBytes - source.BakedCellLookup.RetainedBytes,
            0,
            ref bytes,
            ref pages);
        ReplaceDisplacedMapStateComponent(
            prior?.BakedCellLookup,
            next?.BakedCellLookup,
            published?.BakedCellLookup,
            source.BakedCellLookup,
            source.BakedCellLookup.RetainedBytes,
            0,
            ref bytes,
            ref pages);
        ReplaceDisplacedMapStateComponent(
            prior?.Overlay,
            next?.Overlay,
            published?.Overlay,
            source.Overlay,
            source.Overlay.RetainedBytes,
            source.Overlay.PersistentNodeCount,
            ref bytes,
            ref pages);
        ReplaceDisplacedMapStateComponent(
            prior?.DynamicAddresses,
            next?.DynamicAddresses,
            published?.DynamicAddresses,
            source.DynamicAddresses,
            source.DynamicAddresses.RetainedBytes,
            source.DynamicAddresses.PersistentNodeCount,
            ref bytes,
            ref pages);
    }

    private static void ReplaceDisplacedMapStateComponent(
        object? prior,
        object? next,
        object? published,
        object source,
        long sourceBytes,
        int sourcePages,
        ref long bytes,
        ref int pages)
    {
        if (ReferenceEquals(source, published))
            return;
        if (ReferenceEquals(prior, source) && !ReferenceEquals(next, source))
        {
            bytes = checked(bytes + sourceBytes);
            pages = checked(pages + sourcePages);
        }
        else if (!ReferenceEquals(prior, source) && ReferenceEquals(next, source))
        {
            bytes = checked(bytes - sourceBytes);
            pages = checked(pages - sourcePages);
        }
    }

    internal NavigationOperationRejection ReplaceOverlayState(
        MapState current,
        MapState next,
        NavigationOperationLimits limits)
    {
        if (next.Overlay.CellCount > limits.MaxOverlayCellsPerMap
            || next.Overlay.ConnectionCount > limits.MaxOverlayConnectionsPerMap
            || next.Overlay.TransitionCount > limits.MaxOverlayTransitionsPerMap)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }
        long candidateCells = _overlaySlotCount
            - current.Overlay.CellCount + next.Overlay.CellCount;
        long candidateConnections = _overlayConnectionCount
            - current.Overlay.ConnectionCount + next.Overlay.ConnectionCount;
        long candidateTransitions = _overlayTransitionCount
            - current.Overlay.TransitionCount + next.Overlay.TransitionCount;
        if (candidateCells > limits.MaxOverlayCells
            || candidateConnections > limits.MaxOverlayConnections
            || candidateTransitions > limits.MaxOverlayTransitions)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }
        ReplaceTotals(current, next);
        SetMapState(current, next);
        return NavigationOperationRejection.None;
    }

    internal NavigationOperationCandidate Clone()
    {
        return new NavigationOperationCandidate(_navigationAreaCount)
        {
            _maps = _maps,
            _bakeVersionHighWater = _bakeVersionHighWater,
            _incomingSources = _incomingSources,
            _gridBindings = _gridBindings,
            _explicitConnections = _explicitConnections,
            _explicitChangedSources = _explicitChangedSources,
            _explicitChangedOwners = _explicitChangedOwners,
            _overlaySlotCount = _overlaySlotCount,
            _overlayConnectionCount = _overlayConnectionCount,
            _overlayTransitionCount = _overlayTransitionCount,
            _transitionRuleCount = _transitionRuleCount,
            _seamCandidateCount = _seamCandidateCount,
            _explicitEdgeCount = _explicitEdgeCount,
            _dynamicCellCount = _dynamicCellCount,
            _mapStateRetainedBytes = _mapStateRetainedBytes,
            _mapStatePersistentPages = _mapStatePersistentPages,
            _incomingSetRetainedBytes = _incomingSetRetainedBytes,
            _incomingSetPersistentPages = _incomingSetPersistentPages,
            _workCopiedPersistentBytes = _workCopiedPersistentBytes,
            _workCopiedPersistentPages = _workCopiedPersistentPages,
            _workPublishedExplicitConnections = _workPublishedExplicitConnections,
            _workOwnedExplicitPayloadBytes = _workOwnedExplicitPayloadBytes,
            _workOwnedExplicitPayloadPages = _workOwnedExplicitPayloadPages,
            _workPublishedMaps = _workPublishedMaps,
            _workOwnedMapStatePayloadBytes = _workOwnedMapStatePayloadBytes,
            _workOwnedMapStatePayloadPages = _workOwnedMapStatePayloadPages
        };
    }

    internal int GetIncomingSourceCount(string mapId) =>
        _incomingSources.TryGetValue(mapId, out PersistentStringMap<bool> sources)
            ? sources.Count
            : 0;

    internal string GetIncomingSource(string mapId, int ordinal)
    {
        _incomingSources.TryGetValue(mapId, out PersistentStringMap<bool> sources);
        return sources.GetKeyAt(ordinal);
    }

    internal void UpdateIncomingSourceForWork(
        string destinationMapId,
        string sourceMapId,
        bool remove) => UpdateIncomingSource(destinationMapId, sourceMapId, remove);

    internal bool ValidateTransitionForWork(
        MapState state,
        bool overlay,
        int index,
        string[] changedMapIds,
        MapState[] changedStates,
        bool allowDormantEndpoints)
    {
        if (!overlay)
        {
            TraversalTransitionDefinition transition = state.Map.TransitionSpan[index];
            return state.Overlay.TryGetTransition(transition.Id, out _)
                || ValidateTransition(
                    state,
                    transition,
                    changedMapIds,
                    changedStates,
                    allowDormantEndpoints);
        }
        TraversalTransitionOverlayOperation operation = state.Overlay.GetTransitionAt(index);
        return operation.Kind != TraversalTransitionOverlayOperationKind.Upsert
            || ValidateTransition(
                state,
                operation.Transition,
                changedMapIds,
                changedStates,
                allowDormantEndpoints);
    }

    internal int GetTotalDynamicCellCandidateCount()
    {
        return _dynamicCellCount;
    }

    internal bool TryGetMap(string mapId, out NavigationMap map)
    {
        if (_maps.TryGetValue(mapId, out MapState? state))
        {
            map = state.Map;
            return true;
        }

        map = null!;
        return false;
    }

    internal bool TryGetState(string mapId, out MapState? state) =>
        _maps.TryGetValue(mapId, out state);

    internal bool TryGetSemanticState(
        NavigationCellAddress address,
        out NavigationCellSemanticSource source,
        out bool hasCell,
        out NavigationCell cell)
    {
        if (!_maps.TryGetValue(address.MapId, out MapState? state) || state == null)
        {
            source = default;
            hasCell = false;
            cell = default;
            return false;
        }
        int bakedSlot = state.BakedCellLookup.Find(address.Index);
        bool dynamic = bakedSlot < 0
            && state.DynamicAddresses.TryGetValue(address.Index, out _);
        if (bakedSlot < 0 && !dynamic)
        {
            source = default;
            hasCell = false;
            cell = default;
            return false;
        }
        if (state.Overlay.TryGetCell(
                address.Index,
                out NavigationCellOverlayOperation operation))
        {
            if (operation.Kind == NavigationCellOverlayOperationKind.Set)
            {
                source = dynamic
                    ? NavigationCellSemanticSource.DynamicOverlaySet
                    : NavigationCellSemanticSource.OverlaySet;
                hasCell = true;
                cell = operation.Cell;
                return true;
            }
            source = NavigationCellSemanticSource.OverlaySuppressed;
            hasCell = false;
            cell = default;
            return true;
        }
        if (dynamic)
        {
            NavigationCell? defaultCell = state.Map.DefaultCell;
            source = defaultCell.HasValue
                ? NavigationCellSemanticSource.Baked
                : NavigationCellSemanticSource.DynamicInactive;
            hasCell = defaultCell.HasValue;
            cell = defaultCell.GetValueOrDefault();
            return true;
        }
        source = NavigationCellSemanticSource.Baked;
        hasCell = true;
        cell = state.Map.CellSpan[bakedSlot].Cell;
        return true;
    }

    internal bool TryGetOverlay(string mapId, out NavigationMapOverlayState overlay)
    {
        if (_maps.TryGetValue(mapId, out MapState? state))
        {
            overlay = state.Overlay;
            return true;
        }

        overlay = NavigationMapOverlayState.Empty;
        return false;
    }

    private void SetMapState(MapState? previous, MapState next)
    {
        if (previous != null)
            RemoveMapStateTotals(previous);
        AddMapStateTotals(next);
        _maps = _maps.Set(next.Map.MapId, next, out int copiedNodes);
        RecordPersistentCopies(copiedNodes);
    }

    private void AddMapStateTotals(MapState state)
    {
        _mapStateRetainedBytes = checked(
            _mapStateRetainedBytes
            + state.PreparedMapRetainedBytes
            + state.Overlay.RetainedBytes
            + state.DynamicAddresses.RetainedBytes);
        _mapStatePersistentPages = checked(
            _mapStatePersistentPages
            + state.Overlay.PersistentNodeCount
            + state.DynamicAddresses.PersistentNodeCount);
    }

    private void RemoveMapStateTotals(MapState state)
    {
        _mapStateRetainedBytes = checked(
            _mapStateRetainedBytes
            - state.PreparedMapRetainedBytes
            - state.Overlay.RetainedBytes
            - state.DynamicAddresses.RetainedBytes);
        _mapStatePersistentPages = checked(
            _mapStatePersistentPages
            - state.Overlay.PersistentNodeCount
            - state.DynamicAddresses.PersistentNodeCount);
    }

    private void ReplaceTotals(MapState? previous, MapState? next)
    {
        if (previous != null)
        {
            _overlaySlotCount -= previous.Overlay.CellCount;
            _overlayConnectionCount -= previous.Overlay.ConnectionCount;
            _overlayTransitionCount -= previous.Overlay.TransitionCount;
            _transitionRuleCount = checked(
                _transitionRuleCount - previous.Map.TransitionRuleSpan.Length);
            long previousConnections = previous.Map.ConnectionSpan.Length
                + previous.Overlay.ConnectionCount;
            _seamCandidateCount -= previousConnections;
            _explicitEdgeCount -= previousConnections
                + previous.Map.TransitionSpan.Length
                + previous.Overlay.TransitionCount;
            _dynamicCellCount = checked(_dynamicCellCount - previous.DynamicAddresses.Count);
        }

        if (next != null)
        {
            _overlaySlotCount += next.Overlay.CellCount;
            _overlayConnectionCount += next.Overlay.ConnectionCount;
            _overlayTransitionCount += next.Overlay.TransitionCount;
            _transitionRuleCount = checked(
                _transitionRuleCount + next.Map.TransitionRuleSpan.Length);
            long nextConnections = next.Map.ConnectionSpan.Length
                + next.Overlay.ConnectionCount;
            _seamCandidateCount += nextConnections;
            _explicitEdgeCount += nextConnections
                + next.Map.TransitionSpan.Length
                + next.Overlay.TransitionCount;
            _dynamicCellCount = checked(_dynamicCellCount + next.DynamicAddresses.Count);
        }
    }

    private void UpdateIncomingSource(string destinationMapId, string sourceMapId, bool remove)
    {
        bool hadSources = _incomingSources.TryGetValue(
            destinationMapId,
            out PersistentStringMap<bool> sources);
        sources ??= PersistentStringMap<bool>.Empty;
        long previousBytes = hadSources ? sources.RetainedBytes : 0;
        int previousPages = hadSources ? sources.PersistentNodeCount : 0;
        if (remove)
        {
            sources = sources.Remove(sourceMapId, out bool removed, out int copiedNodes);
            if (!removed)
                return;
            RecordPersistentCopies(copiedNodes);
            if (sources.Count == 0)
            {
                _incomingSources = _incomingSources.Remove(
                    destinationMapId,
                    out _,
                    out copiedNodes);
            }
            else
            {
                _incomingSources = _incomingSources.Set(
                    destinationMapId,
                    sources,
                    out copiedNodes);
            }
            RecordPersistentCopies(copiedNodes);
        }
        else
        {
            if (sources.ContainsKey(sourceMapId))
                return;
            sources = sources.Set(sourceMapId, true, out int copiedNodes);
            RecordPersistentCopies(copiedNodes);
            _incomingSources = _incomingSources.Set(
                destinationMapId,
                sources,
                out copiedNodes);
            RecordPersistentCopies(copiedNodes);
        }
        long nextBytes = sources.Count == 0 ? 0 : sources.RetainedBytes;
        int nextPages = sources.Count == 0 ? 0 : sources.PersistentNodeCount;
        _incomingSetRetainedBytes = checked(
            _incomingSetRetainedBytes - previousBytes + nextBytes);
        _incomingSetPersistentPages = checked(
            _incomingSetPersistentPages - previousPages + nextPages);
    }

    private bool ValidateTransition(
        MapState source,
        TraversalTransitionDefinition transition,
        string[] changedMapIds,
        MapState[] changedStates,
        bool allowDormantEndpoints)
    {
        if (!source.Map.GridBinding.IsValidIndex(transition.SourceIndex)
            || (!HasDefinitionMedium(
                    source,
                    transition.SourceIndex,
                    transition.SourceMedium)
                && !(allowDormantEndpoints
                    && _bakeVersionHighWater.ContainsKey(source.Map.MapId))))
        {
            return false;
        }

        if (transition.HasSourcePointOverride
            && (!source.Map.GridBinding.TryGetCellPrism(transition.SourceIndex, out GridForge.Grids.Topology.GridCellPrism sourcePrism)
                || !sourcePrism.Contains(transition.SourcePointOverride)))
        {
            return false;
        }

        MapState? destination = FindChangedState(transition.Destination.MapId, changedMapIds, changedStates);
        if (destination == null && !_maps.TryGetValue(transition.Destination.MapId, out destination))
            return true;
        if (!destination.Map.GridBinding.IsValidIndex(transition.Destination.Index)
            || (!HasDefinitionMedium(
                    destination,
                    transition.Destination.Index,
                    transition.DestinationMedium)
                && !(allowDormantEndpoints
                    && _bakeVersionHighWater.ContainsKey(destination.Map.MapId))))
        {
            return false;
        }

        return !transition.HasDestinationPointOverride
            || (destination.Map.GridBinding.TryGetCellPrism(transition.Destination.Index, out GridForge.Grids.Topology.GridCellPrism destinationPrism)
                && destinationPrism.Contains(transition.DestinationPointOverride));
    }

    private static bool TryGetEffectiveCell(
        MapState state,
        GridForge.Spatial.VoxelIndex index,
        out NavigationCell cell)
    {
        if (TryFindCellOverlay(state.Overlay, index, out NavigationCellOverlayOperation operation))
        {
            cell = operation.Cell;
            return operation.Kind == NavigationCellOverlayOperationKind.Set;
        }
        int baked = state.BakedCellLookup.Find(index);
        if (baked >= 0)
        {
            cell = state.Map.CellSpan[baked].Cell;
            return true;
        }
        NavigationCell? defaultCell = state.Map.GridBinding.IsValidIndex(index)
            ? state.Map.DefaultCell
            : null;
        cell = defaultCell.GetValueOrDefault();
        return defaultCell.HasValue;
    }

    private static bool HasDefinitionMedium(
        MapState state,
        GridForge.Spatial.VoxelIndex index,
        TraversalMedium medium)
    {
        if (TryFindCellOverlay(
                state.Overlay,
                index,
                out NavigationCellOverlayOperation operation)
            && operation.Kind == NavigationCellOverlayOperationKind.Set
            && SupportsMedium(operation.Cell, medium))
        {
            return true;
        }
        int baked = state.BakedCellLookup.Find(index);
        if (baked >= 0)
            return SupportsMedium(state.Map.CellSpan[baked].Cell, medium);
        return state.Map.DefaultCell.HasValue
            && state.Map.GridBinding.IsValidIndex(index)
            && SupportsMedium(state.Map.DefaultCell.Value, medium);
    }

    private static bool SupportsMedium(NavigationCell cell, TraversalMedium medium) => medium switch
    {
        TraversalMedium.Solid => (cell.Media & TraversalMedia.Solid) != 0,
        TraversalMedium.Gas => (cell.Media & TraversalMedia.Gas) != 0,
        TraversalMedium.Liquid => (cell.Media & TraversalMedia.Liquid) != 0,
        _ => false
    };

    private static MapState? FindChangedState(
        string mapId,
        string[] changedMapIds,
        MapState[] changedStates)
    {
        int low = 0;
        int high = changedMapIds.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(changedMapIds[middle], mapId);
            if (comparison == 0)
                return changedStates[middle];
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return null;
    }

    private static bool TryFindCellOverlay(
        NavigationMapOverlayState overlay,
        GridForge.Spatial.VoxelIndex index,
        out NavigationCellOverlayOperation operation)
        => overlay.TryGetCell(index, out operation);

    internal sealed class MapState
    {
        internal MapState(
            NavigationMap map,
            long bakeVersion,
            long preparedMapRetainedBytes,
            NavigationMapOverlayState overlay,
            long dynamicSlotGeneration,
            PersistentVoxelIndexMap<byte>? dynamicAddresses = null,
            NavigationBakedCellLookup? bakedCellLookup = null)
        {
            Map = map;
            BakeVersion = bakeVersion;
            PreparedMapRetainedBytes = preparedMapRetainedBytes;
            Overlay = overlay;
            DynamicSlotGeneration = dynamicSlotGeneration;
            DynamicAddresses = dynamicAddresses ?? PersistentVoxelIndexMap<byte>.Empty;
            BakedCellLookup = bakedCellLookup ?? NavigationBakedCellLookup.Create(map);
        }

        internal NavigationMap Map { get; }

        internal long BakeVersion { get; }

        internal long PreparedMapRetainedBytes { get; }

        internal NavigationMapOverlayState Overlay { get; }

        internal long DynamicSlotGeneration { get; }

        internal PersistentVoxelIndexMap<byte> DynamicAddresses { get; }

        internal NavigationBakedCellLookup BakedCellLookup { get; }
    }
}
