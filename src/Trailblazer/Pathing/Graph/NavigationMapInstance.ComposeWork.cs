//=======================================================================
// NavigationMapInstance.ComposeWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationMapInstance
{
    /// <summary>Rebuilds replacement-map semantic pages under the shared maintenance meter.</summary>
    internal sealed class ComposeWork
    {
        private readonly NavigationOperationCandidate.MapState _state;
        private readonly NavigationMapInstance? _previous;
        private readonly NavigationMapOverlayDelta? _delta;
        private readonly NavigationOperationFrameChange[]? _changes;
        private readonly int _changeCount;
        private readonly string? _mapId;
        private readonly bool _isOverlayCompose;
        private readonly long _version;
        private PersistentVoxelIndexMap<NavigationDynamicCellSlot> _dynamicSlots;
        private PersistentIntMap<VoxelIndex> _dynamicSlotIndexes;
        private PersistentIntMap<ulong> _dynamicHighWater;
        private int _nextDynamicSlot;
        private PersistentIntMap<NavigationSemanticPage> _semanticPages;
        private PersistentIntMap<NavigationPhysicalPage> _physicalPages;
        private NavigationGridGenerationIdentity _gridIdentity;
        private ulong _baselineHighWater;
        private ulong _gridHighWaterSequence;
        private int _copiedSemanticPages;
        private int _retainedCopiedSemanticPages;
        private int _newAddressCount;
        private int _cellIndex;
        private int _changeIndex;
        private bool _changeDebited;
        private bool _lookupDebited;
        private NavigationMapOverlayDelta? _activeDelta;
        private int _lastCopiedSemanticPageIndex = -1;
        private long _copiedPersistentBytes;
        private int _copiedPersistentPages;

        internal ComposeWork(
            NavigationOperationCandidate.MapState state,
            NavigationMapInstance? previous,
            long version)
        {
            _state = state;
            _previous = previous;
            _version = version;
            _semanticPages = PersistentIntMap<NavigationSemanticPage>.Empty;
            _physicalPages = PersistentIntMap<NavigationPhysicalPage>.Empty;
            if (previous != null
                && previous.DynamicSlotGeneration == state.DynamicSlotGeneration)
            {
                // PreserveAndRevalidate keeps the exact logical dynamic-address set. Reuse its
                // reverse/high-water roots; only baked ordinals can force semantic-page rebuilds.
                _dynamicSlots = previous._dynamicSlots;
                _dynamicSlotIndexes = previous._dynamicSlotIndexes;
                _dynamicHighWater = previous._dynamicBaselineHighWater;
                _nextDynamicSlot = previous._nextDynamicSlot;
            }
            else
            {
                _dynamicSlots = PersistentVoxelIndexMap<NavigationDynamicCellSlot>.Empty;
                _dynamicSlotIndexes = PersistentIntMap<VoxelIndex>.Empty;
                _dynamicHighWater = PersistentIntMap<ulong>.Empty;
                _nextDynamicSlot = DynamicSlotBase;
            }
        }

        internal ComposeWork(
            NavigationOperationCandidate.MapState state,
            NavigationMapInstance previous,
            NavigationMapOverlayDelta delta,
            long version)
        {
            _state = state;
            _previous = previous;
            _delta = delta;
            _isOverlayCompose = true;
            _version = version;
            _dynamicSlots = previous._dynamicSlots;
            _dynamicSlotIndexes = previous._dynamicSlotIndexes;
            _dynamicHighWater = previous._dynamicBaselineHighWater;
            _nextDynamicSlot = previous._nextDynamicSlot;
            _semanticPages = previous._semanticPages;
            _physicalPages = previous._physicalPages;
            _gridIdentity = previous.GridIdentity;
            _baselineHighWater = previous.BaselineHighWater;
            _gridHighWaterSequence = previous.GridHighWaterSequence;
        }

        internal ComposeWork(
            NavigationOperationCandidate.MapState state,
            NavigationMapInstance previous,
            NavigationOperationFrameChange[] changes,
            int changeCount,
            string mapId,
            long version)
        {
            _state = state;
            _previous = previous;
            _changes = changes;
            _changeCount = changeCount;
            _mapId = mapId;
            _isOverlayCompose = true;
            _version = version;
            _dynamicSlots = previous._dynamicSlots;
            _dynamicSlotIndexes = previous._dynamicSlotIndexes;
            _dynamicHighWater = previous._dynamicBaselineHighWater;
            _nextDynamicSlot = previous._nextDynamicSlot;
            _semanticPages = previous._semanticPages;
            _physicalPages = previous._physicalPages;
            _gridIdentity = previous.GridIdentity;
            _baselineHighWater = previous.BaselineHighWater;
            _gridHighWaterSequence = previous.GridHighWaterSequence;
        }

        internal NavigationMapInstance Result { get; private set; } = null!;

        internal long RetainedBytes => checked(96L + AdditionalExclusiveRetainedBytes);

        internal int PersistentPageCount => checked(1 + AdditionalExclusivePersistentPages);

        internal long AdditionalExclusiveRetainedBytes => checked(
            200L
            + GetAdditionalRootBytes(
                _dynamicSlots,
                _previous?._dynamicSlots,
                _dynamicSlots.RetainedBytes,
                _previous?._dynamicSlots.RetainedBytes ?? 0L)
            + GetAdditionalRootBytes(
                _dynamicSlotIndexes,
                _previous?._dynamicSlotIndexes,
                _dynamicSlotIndexes.RetainedBytes,
                _previous?._dynamicSlotIndexes.RetainedBytes ?? 0L)
            + GetAdditionalRootBytes(
                _dynamicHighWater,
                _previous?._dynamicBaselineHighWater,
                _dynamicHighWater.RetainedBytes,
                _previous?._dynamicBaselineHighWater.RetainedBytes ?? 0L)
            + GetAdditionalRootBytes(
                _semanticPages,
                _previous?._semanticPages,
                checked(_semanticPages.RetainedBytes + ((long)_semanticPages.Count * 4_400L)),
                checked((_previous?._semanticPages.RetainedBytes ?? 0L)
                    + ((long)(_previous?._semanticPages.Count ?? 0) * 4_400L)))
            + GetAdditionalRootBytes(
                _physicalPages,
                _previous?._physicalPages,
                checked(_physicalPages.RetainedBytes + ((long)_physicalPages.Count * 320L)),
                checked((_previous?._physicalPages.RetainedBytes ?? 0L)
                    + ((long)(_previous?._physicalPages.Count ?? 0) * 320L)))
            + _copiedPersistentBytes
            + ((long)_retainedCopiedSemanticPages * 4_400L));

        internal int AdditionalExclusivePersistentPages => checked(
            6
            + GetAdditionalRootPages(
                _dynamicSlots,
                _previous?._dynamicSlots,
                _dynamicSlots.PersistentNodeCount,
                _previous?._dynamicSlots.PersistentNodeCount ?? 0)
            + GetAdditionalRootPages(
                _dynamicSlotIndexes,
                _previous?._dynamicSlotIndexes,
                _dynamicSlotIndexes.PersistentNodeCount,
                _previous?._dynamicSlotIndexes.PersistentNodeCount ?? 0)
            + GetAdditionalRootPages(
                _dynamicHighWater,
                _previous?._dynamicBaselineHighWater,
                _dynamicHighWater.PersistentNodeCount,
                _previous?._dynamicBaselineHighWater.PersistentNodeCount ?? 0)
            + GetAdditionalRootPages(
                _semanticPages,
                _previous?._semanticPages,
                checked(_semanticPages.PersistentNodeCount * 2),
                checked((_previous?._semanticPages.PersistentNodeCount ?? 0) * 2))
            + GetAdditionalRootPages(
                _physicalPages,
                _previous?._physicalPages,
                checked(_physicalPages.PersistentNodeCount * 2),
                checked((_previous?._physicalPages.PersistentNodeCount ?? 0) * 2))
            + _copiedPersistentPages
            + _retainedCopiedSemanticPages);

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            if (Result != null)
                return true;
            if (_changes != null)
            {
                if (!AdvanceOverlaySequence(meter))
                    return false;
            }
            else
            {
                int cellCount = _delta?.Cells.Count ?? _state.Overlay.CellCount;
                while (_cellIndex < cellCount)
                {
                    if (!meter.TryConsumeOverlaySlots(1))
                        return false;
                    NavigationCellOverlayOperation operation = _delta == null
                        ? _state.Overlay.GetCellAt(_cellIndex++)
                        : _delta.Cells[_cellIndex++];
                    ApplyCellOperation(operation);
                }
            }
            if (_isOverlayCompose && _newAddressCount > 0)
            {
                _gridIdentity = default;
                _baselineHighWater = 0;
                _gridHighWaterSequence = 0;
                _physicalPages = PersistentIntMap<NavigationPhysicalPage>.Empty;
            }
            Result = new NavigationMapInstance(
                _state.Map,
                _state.BakeVersion,
                _state.Overlay,
                _state.DynamicSlotGeneration,
                _dynamicSlots,
                _dynamicSlotIndexes,
                _nextDynamicSlot,
                _semanticPages,
                _physicalPages,
                _dynamicHighWater,
                _state.BakedCellLookup,
                _state.PreparedMapRetainedBytes,
                _gridIdentity,
                _baselineHighWater,
                _gridHighWaterSequence,
                _version,
                semanticVersion: _version,
                physicalVersion: _isOverlayCompose && _newAddressCount == 0
                    ? _previous!.PhysicalVersion
                    : _version,
                lastBaselineAddressCount: _newAddressCount,
                lastCopiedSemanticPages: !_isOverlayCompose
                    ? _semanticPages.Count
                    : _copiedSemanticPages,
                lastCopiedPhysicalPages: 0,
                dynamicAddresses: _state.DynamicAddresses);
            return true;
        }

        private bool AdvanceOverlaySequence(MaintenanceWorkMeter meter)
        {
            while (_changeIndex < _changeCount)
            {
                if (!_changeDebited)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    _changeDebited = true;
                }
                NavigationOperationFrameChange change = _changes![_changeIndex];
                if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                {
                    CompleteSequenceChange();
                    continue;
                }
                if (!_lookupDebited)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _lookupDebited = true;
                    _activeDelta = FindDelta(
                        change.PreparedOverlay!.Transaction.MapSpan,
                        _mapId!);
                }
                if (_activeDelta == null)
                {
                    CompleteSequenceChange();
                    continue;
                }
                while (_cellIndex < _activeDelta.Cells.Count)
                {
                    if (!meter.TryConsumeOverlaySlots(1))
                        return false;
                    ApplyCellOperation(_activeDelta.Cells[_cellIndex++]);
                }
                CompleteSequenceChange();
            }
            return true;
        }

        private void CompleteSequenceChange()
        {
            _changeIndex++;
            _cellIndex = 0;
            _changeDebited = false;
            _lookupDebited = false;
            _activeDelta = null;
        }

        private static NavigationMapOverlayDelta? FindDelta(
            ReadOnlySpan<NavigationMapOverlayDelta> deltas,
            string mapId)
        {
            int lower = 0;
            int upper = deltas.Length - 1;
            while (lower <= upper)
            {
                int middle = lower + ((upper - lower) >> 1);
                NavigationMapOverlayDelta delta = deltas[middle];
                int comparison = string.CompareOrdinal(delta.MapId, mapId);
                if (comparison == 0)
                    return delta;
                if (comparison < 0)
                    lower = middle + 1;
                else
                    upper = middle - 1;
            }
            return null;
        }

        private void ApplyCellOperation(NavigationCellOverlayOperation operation)
        {
            int slot = _state.BakedCellLookup.Find(operation.Index);
            if (slot < 0
                && _dynamicSlots.TryGetValue(
                    operation.Index,
                    out NavigationDynamicCellSlot dynamicSlot))
            {
                slot = dynamicSlot.Slot;
            }
            if (slot < 0 && operation.Kind == NavigationCellOverlayOperationKind.Set)
            {
                slot = _nextDynamicSlot++;
                var addedDynamicSlot = new NavigationDynamicCellSlot(operation.Index, slot);
                _dynamicSlots = _dynamicSlots.Set(
                    operation.Index,
                    addedDynamicSlot,
                    out int slotCopies);
                RecordPersistentCopies(slotCopies, 64L);
                _dynamicSlotIndexes = _dynamicSlotIndexes.Set(
                    slot,
                    operation.Index,
                    out int indexCopies);
                RecordPersistentCopies(indexCopies, 72L);
                _newAddressCount++;
            }
            if (slot < 0)
                return;
            if (HasSameSemanticState(slot, operation))
                return;
            PersistentIntMap<NavigationSemanticPage> updated = ApplySemanticOperation(
                _semanticPages,
                slot,
                operation,
                _version,
                out int semanticCopies);
            if (!ReferenceEquals(updated, _semanticPages))
            {
                RecordPersistentCopies(semanticCopies, 72L);
                int pageIndex = slot / NavigationSemanticPage.SlotCount;
                if (pageIndex != _lastCopiedSemanticPageIndex)
                {
                    _copiedSemanticPages++;
                    if (_previous != null
                        && _previous._semanticPages.TryGetValue(pageIndex, out _))
                    {
                        _retainedCopiedSemanticPages++;
                    }
                    _lastCopiedSemanticPageIndex = pageIndex;
                }
            }
            _semanticPages = updated;
        }

        private bool HasSameSemanticState(
            int slot,
            NavigationCellOverlayOperation operation)
        {
            bool dynamic = slot >= _state.Map.CellSpan.Length;
            _semanticPages.TryGetValue(
                slot / NavigationSemanticPage.SlotCount,
                out NavigationSemanticPage? page);
            int offset = slot % NavigationSemanticPage.SlotCount;
            NavigationCellSemanticSource currentSource;
            bool currentHasCell;
            NavigationCell currentCell;
            if (page != null && page.IsSuppressed[offset])
            {
                currentSource = NavigationCellSemanticSource.OverlaySuppressed;
                currentHasCell = false;
                currentCell = default;
            }
            else if (page != null && page.HasOverride[offset])
            {
                currentSource = dynamic
                    ? NavigationCellSemanticSource.DynamicOverlaySet
                    : NavigationCellSemanticSource.OverlaySet;
                currentHasCell = true;
                currentCell = page.Cells[offset];
            }
            else if (dynamic)
            {
                currentSource = NavigationCellSemanticSource.DynamicInactive;
                currentHasCell = false;
                currentCell = default;
            }
            else
            {
                currentSource = NavigationCellSemanticSource.Baked;
                currentHasCell = true;
                currentCell = _state.Map.CellSpan[slot].Cell;
            }

            NavigationCellSemanticSource nextSource;
            bool nextHasCell;
            NavigationCell nextCell;
            if (operation.Kind == NavigationCellOverlayOperationKind.Set)
            {
                nextSource = dynamic
                    ? NavigationCellSemanticSource.DynamicOverlaySet
                    : NavigationCellSemanticSource.OverlaySet;
                nextHasCell = true;
                nextCell = operation.Cell;
            }
            else if (operation.Kind == NavigationCellOverlayOperationKind.Suppress)
            {
                nextSource = NavigationCellSemanticSource.OverlaySuppressed;
                nextHasCell = false;
                nextCell = default;
            }
            else if (dynamic)
            {
                nextSource = NavigationCellSemanticSource.DynamicInactive;
                nextHasCell = false;
                nextCell = default;
            }
            else
            {
                nextSource = NavigationCellSemanticSource.Baked;
                nextHasCell = true;
                nextCell = _state.Map.CellSpan[slot].Cell;
            }
            return currentSource == nextSource
                && currentHasCell == nextHasCell
                && (!currentHasCell || currentCell.Equals(nextCell));
        }

        private void RecordPersistentCopies(int copiedNodes, long bytesPerNode)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
            _copiedPersistentBytes = checked(
                _copiedPersistentBytes + (copiedNodes * bytesPerNode));
        }

        private long GetAdditionalRootBytes(
            object current,
            object? previous,
            long currentBytes,
            long previousBytes)
        {
            if (ReferenceEquals(current, previous))
                return 0;
            return !_isOverlayCompose
                ? currentBytes
                : checked(32L + System.Math.Max(0L, currentBytes - previousBytes));
        }

        private int GetAdditionalRootPages(
            object current,
            object? previous,
            int currentPages,
            int previousPages)
        {
            if (ReferenceEquals(current, previous))
                return 0;
            return !_isOverlayCompose
                ? currentPages
                : System.Math.Max(0, currentPages - previousPages);
        }
    }
}
