//=======================================================================
// NavigationMapInstance.ComposeWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
        private readonly long _version;
        private PersistentVoxelIndexMap<NavigationDynamicCellSlot> _dynamicSlots;
        private PersistentIntMap<VoxelIndex> _dynamicSlotIndexes;
        private PersistentIntMap<ulong> _dynamicHighWater;
        private int _nextDynamicSlot;
        private PersistentIntMap<NavigationSemanticPage> _semanticPages;
        private PersistentIntMap<NavigationPhysicalPage> _physicalPages;
        private NavigationGridGenerationIdentity _gridIdentity;
        private ulong _baselineHighWater;
        private int _copiedSemanticPages;
        private int _retainedCopiedSemanticPages;
        private int _newAddressCount;
        private int _cellIndex;
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
            _version = version;
            _dynamicSlots = previous._dynamicSlots;
            _dynamicSlotIndexes = previous._dynamicSlotIndexes;
            _dynamicHighWater = previous._dynamicBaselineHighWater;
            _nextDynamicSlot = previous._nextDynamicSlot;
            _semanticPages = previous._semanticPages;
            _physicalPages = previous._physicalPages;
            _gridIdentity = previous.GridIdentity;
            _baselineHighWater = previous.BaselineHighWater;
        }

        internal NavigationMapInstance Result { get; private set; } = null!;

        internal long RetainedBytes => checked(
            96L
            + _state.PreparedMapRetainedBytes
            + _state.Overlay.RetainedBytes
            + _state.DynamicAddresses.RetainedBytes
            + _dynamicSlots.RetainedBytes
            + _dynamicSlotIndexes.RetainedBytes
            + _dynamicHighWater.RetainedBytes
            + _semanticPages.RetainedBytes
            + ((long)_semanticPages.Count * 4_400L)
            + _physicalPages.RetainedBytes
            + ((long)_physicalPages.Count * 320L)
            + _copiedPersistentBytes
            + ((long)_retainedCopiedSemanticPages * 4_400L));

        internal int PersistentPageCount => checked(
            1
            + _state.Overlay.PersistentNodeCount
            + _state.DynamicAddresses.PersistentNodeCount
            + _dynamicSlots.PersistentNodeCount
            + _dynamicSlotIndexes.PersistentNodeCount
            + _dynamicHighWater.PersistentNodeCount
            + (_semanticPages.PersistentNodeCount * 2)
            + (_physicalPages.PersistentNodeCount * 2)
            + _copiedPersistentPages
            + _retainedCopiedSemanticPages);

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            if (Result != null)
                return true;
            int cellCount = _delta?.Cells.Count ?? _state.Overlay.CellCount;
            while (_cellIndex < cellCount)
            {
                if (!meter.TryConsumeOverlaySlots(1))
                    return false;
                NavigationCellOverlayOperation operation = _delta == null
                    ? _state.Overlay.GetCellAt(_cellIndex++)
                    : _delta.Cells[_cellIndex++];
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
                if (slot >= 0)
                {
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
            }
            if (_delta != null && _newAddressCount > 0)
            {
                _gridIdentity = default;
                _baselineHighWater = 0;
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
                _version,
                semanticVersion: _version,
                physicalVersion: _delta != null && _newAddressCount == 0
                    ? _previous!.PhysicalVersion
                    : _version,
                lastBaselineAddressCount: _newAddressCount,
                lastCopiedSemanticPages: _delta == null
                    ? _semanticPages.Count
                    : _copiedSemanticPages,
                lastCopiedPhysicalPages: 0,
                dynamicAddresses: _state.DynamicAddresses);
            return true;
        }

        private void RecordPersistentCopies(int copiedNodes, long bytesPerNode)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
            _copiedPersistentBytes = checked(
                _copiedPersistentBytes + (copiedNodes * bytesPerNode));
        }
    }
}
