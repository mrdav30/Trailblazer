//=======================================================================
// NavigationMapInstance.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable composed map instance without retaining GridForge voxels.</summary>
internal sealed partial class NavigationMapInstance
{
    private const int DynamicSlotBase = 1_000_000_000;
    private readonly PersistentVoxelIndexMap<NavigationDynamicCellSlot> _dynamicSlots;
    private readonly PersistentVoxelIndexMap<byte> _dynamicAddresses;
    private readonly PersistentIntMap<VoxelIndex> _dynamicSlotIndexes;
    private readonly int _nextDynamicSlot;
    private readonly PersistentIntMap<NavigationSemanticPage> _semanticPages;
    private readonly PersistentIntMap<NavigationPhysicalPage> _physicalPages;
    private readonly PersistentIntMap<ulong> _dynamicBaselineHighWater;
    private readonly NavigationBakedCellLookup _bakedLookup;
    private readonly long _preparedMapRetainedBytes;

    private NavigationMapInstance(
        NavigationMap map,
        long bakeVersion,
        NavigationMapOverlayState overlay,
        long dynamicSlotGeneration,
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots,
        PersistentIntMap<VoxelIndex> dynamicSlotIndexes,
        int nextDynamicSlot,
        PersistentIntMap<NavigationSemanticPage> semanticPages,
        PersistentIntMap<NavigationPhysicalPage> physicalPages,
        PersistentIntMap<ulong> dynamicBaselineHighWater,
        NavigationBakedCellLookup bakedLookup,
        long preparedMapRetainedBytes,
        NavigationGridGenerationIdentity gridIdentity,
        ulong baselineHighWater,
        long instanceVersion,
        long semanticVersion,
        long physicalVersion,
        int lastBaselineAddressCount,
        int lastCopiedSemanticPages,
        int lastCopiedPhysicalPages,
        PersistentVoxelIndexMap<byte>? dynamicAddresses = null)
    {
        Map = map;
        BakeVersion = bakeVersion;
        Overlay = overlay;
        DynamicSlotGeneration = dynamicSlotGeneration;
        _dynamicSlots = dynamicSlots;
        _dynamicAddresses = dynamicAddresses ?? PersistentVoxelIndexMap<byte>.Empty;
        _dynamicSlotIndexes = dynamicSlotIndexes;
        _nextDynamicSlot = nextDynamicSlot;
        _semanticPages = semanticPages;
        _physicalPages = physicalPages;
        _dynamicBaselineHighWater = dynamicBaselineHighWater;
        _bakedLookup = bakedLookup;
        _preparedMapRetainedBytes = preparedMapRetainedBytes;
        GridIdentity = gridIdentity;
        BaselineHighWater = baselineHighWater;
        InstanceVersion = instanceVersion;
        SemanticVersion = semanticVersion;
        PhysicalVersion = physicalVersion;
        LastBaselineAddressCount = lastBaselineAddressCount;
        LastCopiedSemanticPages = lastCopiedSemanticPages;
        LastCopiedPhysicalPages = lastCopiedPhysicalPages;
        RetainedBytes = EstimateRetainedBytes();
    }

    internal NavigationMap Map { get; }

    internal string MapId => Map.MapId;

    internal long BakeVersion { get; }

    internal NavigationMapOverlayState Overlay { get; }

    internal long DynamicSlotGeneration { get; }

    internal NavigationGridGenerationIdentity GridIdentity { get; }

    internal ulong BaselineHighWater { get; }

    internal long InstanceVersion { get; }

    internal long SemanticVersion { get; }

    internal long PhysicalVersion { get; }

    internal int LastBaselineAddressCount { get; }

    internal int LastCopiedSemanticPages { get; }

    internal int LastCopiedPhysicalPages { get; }

    internal bool IsMaterialized => GridIdentity.IsValid;

    internal int BakedSlotCount => Map.CellSpan.Length;

    internal int DynamicSlotCount => _dynamicSlots.Count;

    internal PersistentVoxelIndexMap<byte> DynamicAddresses => _dynamicAddresses;

    internal int AddressCount => BakedSlotCount + DynamicSlotCount;

    internal long RetainedBytes { get; }

    internal long PreparedMapRetainedBytes => _preparedMapRetainedBytes;

    internal NavigationBakedCellLookup BakedCellLookup => _bakedLookup;

    internal NavigationCellLookupKind LookupKind => _bakedLookup.Kind;

    internal int PersistentPageCount => 6
        + Overlay.PersistentNodeCount
        + _dynamicSlots.PersistentNodeCount
        + _dynamicAddresses.PersistentNodeCount
        + _dynamicSlotIndexes.PersistentNodeCount
        + (_semanticPages.PersistentNodeCount * 2)
        + (_physicalPages.PersistentNodeCount * 2)
        + _dynamicBaselineHighWater.PersistentNodeCount;

    internal static NavigationMapInstance Compose(
        GridWorld world,
        NavigationOperationCandidate.MapState state,
        NavigationMapInstance? previous,
        long instanceVersion)
    {
        NavigationMapInstance composed = ComposeDetached(state, previous, instanceVersion);
        if (ReferenceEquals(composed, previous))
            return composed;
        return composed.TryCaptureBaseline(world, out NavigationMapInstance? materialized)
            ? materialized!
            : composed;
    }

    internal static NavigationMapInstance ComposeDetached(
        NavigationOperationCandidate.MapState state,
        NavigationMapInstance? previous,
        long instanceVersion)
    {
        if (previous != null
            && ReferenceEquals(previous.Map, state.Map)
            && ReferenceEquals(previous.Overlay, state.Overlay)
            && previous.BakeVersion == state.BakeVersion
            && previous.DynamicSlotGeneration == state.DynamicSlotGeneration)
        {
            return previous;
        }

        if (previous != null
            && ReferenceEquals(previous.Map, state.Map)
            && previous.BakeVersion == state.BakeVersion
            && previous.DynamicSlotGeneration == state.DynamicSlotGeneration
            && previous.Overlay.HasSameCellRoot(state.Overlay))
        {
            return new NavigationMapInstance(
                state.Map,
                state.BakeVersion,
                state.Overlay,
                state.DynamicSlotGeneration,
                previous._dynamicSlots,
                previous._dynamicSlotIndexes,
                previous._nextDynamicSlot,
                previous._semanticPages,
                previous._physicalPages,
                previous._dynamicBaselineHighWater,
                state.BakedCellLookup,
                state.PreparedMapRetainedBytes,
                previous.GridIdentity,
                previous.BaselineHighWater,
                instanceVersion,
                previous.SemanticVersion,
                previous.PhysicalVersion,
                lastBaselineAddressCount: 0,
                lastCopiedSemanticPages: 0,
                lastCopiedPhysicalPages: 0,
                dynamicAddresses: state.DynamicAddresses);
        }

        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots = BuildDynamicSlots(
            state,
            previous,
            out int nextDynamicSlot);
        PersistentIntMap<VoxelIndex> dynamicSlotIndexes = BuildDynamicSlotIndexes(dynamicSlots);
        PersistentIntMap<ulong> dynamicBaselineHighWater = BuildDynamicBaselineHighWater(dynamicSlots, previous);
        PersistentIntMap<NavigationSemanticPage> semanticPages = BuildSemanticPages(
            state,
            dynamicSlots,
            instanceVersion);
        NavigationBakedCellLookup bakedLookup = state.BakedCellLookup;
        var dormant = new NavigationMapInstance(
            state.Map,
            state.BakeVersion,
            state.Overlay,
            state.DynamicSlotGeneration,
            dynamicSlots,
            dynamicSlotIndexes,
            nextDynamicSlot,
            semanticPages,
            PersistentIntMap<NavigationPhysicalPage>.Empty,
            dynamicBaselineHighWater,
            bakedLookup,
            state.PreparedMapRetainedBytes,
            default,
            baselineHighWater: 0,
            instanceVersion,
            semanticVersion: instanceVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: semanticPages.Count,
            lastCopiedPhysicalPages: 0,
            dynamicAddresses: state.DynamicAddresses);

        return dormant;
    }

    internal bool TryGetSlot(VoxelIndex index, out int slot)
    {
        int baked = _bakedLookup.Find(index);
        if (baked >= 0)
        {
            slot = baked;
            return true;
        }

        if (_dynamicSlots.TryGetValue(index, out NavigationDynamicCellSlot dynamicSlot))
        {
            slot = dynamicSlot.Slot;
            return true;
        }

        slot = -1;
        return false;
    }

    internal VoxelIndex GetSlotIndex(int slot)
    {
        if ((uint)slot < (uint)Map.CellSpan.Length)
            return Map.CellSpan[slot].Index;

        return _dynamicSlotIndexes.TryGetValue(slot, out VoxelIndex index) ? index : default;
    }

    internal bool TryGetEffectiveCell(int slot, out NavigationCell cell)
    {
        NavigationSemanticPage? page = FindSemanticPage(slot / NavigationSemanticPage.SlotCount);
        int offset = slot % NavigationSemanticPage.SlotCount;
        if (page != null && page.IsSuppressed[offset])
        {
            cell = default;
            return false;
        }
        if (page != null && page.HasOverride[offset])
        {
            cell = page.Cells[offset];
            return true;
        }
        if ((uint)slot < (uint)Map.CellSpan.Length)
        {
            cell = Map.CellSpan[slot].Cell;
            return true;
        }

        cell = default;
        return false;
    }

    internal bool TryGetPhysicalState(int slot, out bool isPresent, out byte obstacleCount)
    {
        NavigationPhysicalPage? page = FindPhysicalPage(slot / NavigationPhysicalPage.SlotCount);
        if (page == null)
        {
            isPresent = false;
            obstacleCount = 0;
            return false;
        }

        int offset = slot % NavigationPhysicalPage.SlotCount;
        isPresent = page.IsPresent[offset];
        obstacleCount = page.ObstacleCounts[offset];
        return true;
    }

    internal GraphPageDependency GetPageDependency(int pageIndex)
    {
        _semanticPages.TryGetValue(pageIndex, out NavigationSemanticPage? semantic);
        _physicalPages.TryGetValue(pageIndex, out NavigationPhysicalPage? physical);
        return new GraphPageDependency(
            MapId,
            BakeVersion,
            DynamicSlotGeneration,
            pageIndex,
            semantic?.Version ?? 0,
            physical?.Version ?? 0);
    }

    internal NavigationMapInstance Apply(GridWorld world, in GridEventInfo eventInfo, long instanceVersion)
        => Apply(world.SpawnToken, eventInfo, instanceVersion);

    internal NavigationMapInstance Apply(
        long worldSpawnToken,
        in GridEventInfo eventInfo,
        long instanceVersion)
    {
        if (eventInfo.WorldSpawnToken != worldSpawnToken)
            return this;

        if (eventInfo.ChangeKind == GridEventKind.WorldReset)
            return MakeDormant(instanceVersion);

        if (eventInfo.ChangeKind == GridEventKind.GridAdded
            || eventInfo.ChangeKind == GridEventKind.GridChanged)
        {
            if (!eventInfo.Configuration.ToGridKey().Equals(Map.GridBinding.Key))
                return this;
            return MakeDormant(instanceVersion);
        }

        if (!GridIdentity.Matches(
                eventInfo.WorldSpawnToken,
                eventInfo.GridIndex,
                eventInfo.GridSpawnToken))
        {
            return this;
        }

        if (eventInfo.ChangeKind == GridEventKind.GridRemoved)
            return MakeDormant(instanceVersion);
        if (!IsMaterialized
            || !eventInfo.HasVoxelState
            || !TryGetSlot(eventInfo.VoxelIndex, out int slot)
            || eventInfo.ChangeSequence <= GetBaselineHighWater(slot))
        {
            return this;
        }

        return WithPhysicalState(
            slot,
            eventInfo.IsVoxelPresent,
            eventInfo.ObstacleCount,
            instanceVersion);
    }

    internal NavigationMapInstance ApplyBatch(
        GridWorld world,
        ReadOnlySpan<GridEventInfo> events,
        bool resnapshotAll,
        long instanceVersion)
        => ApplyBatch(world.SpawnToken, events, resnapshotAll, instanceVersion);

    internal NavigationMapInstance ApplyBatch(
        long worldSpawnToken,
        ReadOnlySpan<GridEventInfo> events,
        bool resnapshotAll,
        long instanceVersion)
    {
        NavigationMapInstance next = resnapshotAll ? MakeDormant(instanceVersion) : this;
        int segmentStart = 0;
        for (int i = 0; i < events.Length; i++)
        {
            if (!IsBroad(events[i].ChangeKind))
                continue;
            next = next.ApplyPhysicalSegment(events.Slice(segmentStart, i - segmentStart), instanceVersion);
            next = next.Apply(worldSpawnToken, events[i], instanceVersion);
            segmentStart = i + 1;
        }
        return next.ApplyPhysicalSegment(events.Slice(segmentStart), instanceVersion);
    }

    internal NavigationMapInstance Resnapshot(GridWorld world, long instanceVersion) =>
        TryCaptureBaseline(world, out NavigationMapInstance? materialized)
            ? materialized!
            : MakeDormant(instanceVersion);

    internal NavigationMapInstance Materialize(
        in NavigationGridBaselineCapture capture,
        long instanceVersion)
    {
        GridNavigationBaseline? baseline = capture.Baseline;
        VoxelIndex[]? addresses = capture.Addresses;
        if (!capture.IsRequested
            || !capture.HasBaseline
            || !capture.ConfigurationKey.Equals(Map.GridBinding.Key)
            || (baseline != null && baseline.VoxelStates.Length != capture.AddressCount))
        {
            return MakeDormant(instanceVersion);
        }

        PersistentIntMap<NavigationPhysicalPage> pages = capture.PreparedPages
            ?? BuildPhysicalPages(baseline!.VoxelStates);
        return CreateMaterialized(capture, pages, capture.AddressCount, instanceVersion);
    }

    internal NavigationMapInstance MaterializeDelta(
        NavigationMapInstance previous,
        in NavigationGridBaselineCapture capture,
        long instanceVersion)
    {
        GridNavigationBaseline? baseline = capture.Baseline;
        if (!capture.IsRequested
            || !previous.IsMaterialized
            || !capture.IsDelta)
        {
            return Materialize(capture, instanceVersion);
        }
        if (capture.AddressCount == 0)
        {
            return new NavigationMapInstance(
                Map,
                BakeVersion,
                Overlay,
                DynamicSlotGeneration,
                _dynamicSlots,
                _dynamicSlotIndexes,
                _nextDynamicSlot,
                _semanticPages,
                previous._physicalPages,
                _dynamicBaselineHighWater,
                _bakedLookup,
                _preparedMapRetainedBytes,
                previous.GridIdentity,
                previous.BaselineHighWater,
                instanceVersion,
                SemanticVersion,
                previous.PhysicalVersion,
                lastBaselineAddressCount: 0,
                lastCopiedSemanticPages: LastCopiedSemanticPages,
                lastCopiedPhysicalPages: 0,
                dynamicAddresses: _dynamicAddresses);
        }
        if (baseline == null
            || !baseline.ConfigurationKey.Equals(Map.GridBinding.Key)
            || baseline.VoxelStates.Length != capture.AddressCount)
        {
            return MakeDormant(instanceVersion);
        }

        PersistentIntMap<NavigationPhysicalPage> pages = previous._physicalPages;
        PersistentIntMap<ulong> dynamicHighWater = _dynamicBaselineHighWater;
        var capturedAddresses = new VoxelIndex[capture.AddressCount];
        int capturedCount = 0;
        for (int i = 0; i < capture.AddressCount; i++)
        {
            VoxelIndex address = baseline.VoxelStates[i].VoxelIndex;
            if (previous.TryGetSlot(address, out _))
                continue;
            if (!TryGetSlot(address, out int slot))
                continue;
            NavigationBaselineVoxelState physical = baseline.VoxelStates[i];
            pages = ApplyPhysicalState(
                pages,
                slot,
                physical.IsPresent,
                physical.ObstacleCount,
                instanceVersion);
            if (slot >= DynamicSlotBase)
                dynamicHighWater = dynamicHighWater.Set(slot, baseline.HighWaterSequence);
            capturedAddresses[capturedCount] = address;
            capturedCount++;
        }

        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            _dynamicSlots,
            _dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            pages,
            dynamicHighWater,
            _bakedLookup,
            _preparedMapRetainedBytes,
            new NavigationGridGenerationIdentity(
                baseline.WorldSpawnToken,
                baseline.GridIndex,
                baseline.GridSpawnToken,
                baseline.ConfigurationKey),
            previous.BaselineHighWater,
            instanceVersion,
            SemanticVersion,
            physicalVersion: capturedCount > 0 ? instanceVersion : PhysicalVersion,
            lastBaselineAddressCount: capturedCount,
            lastCopiedSemanticPages: LastCopiedSemanticPages,
            lastCopiedPhysicalPages: capturedCount > 0
                ? CountTouchedPages(capturedAddresses, capturedCount, _dynamicSlots)
                : 0,
            dynamicAddresses: _dynamicAddresses);
    }

    internal NavigationMapInstance FailClosed(long instanceVersion) =>
        MakeDormant(instanceVersion);

    internal NavigationMapInstance ApplyCellOverlay(
        GridWorld world,
        NavigationOperationCandidate.MapState state,
        ReadOnlySpan<NavigationCellOverlayOperation> operations,
        long instanceVersion)
        => ApplyCellOverlay(world, state, operations, instanceVersion, captureNewAddresses: true);

    internal NavigationMapInstance ApplyCellOverlayDetached(
        NavigationOperationCandidate.MapState state,
        ReadOnlySpan<NavigationCellOverlayOperation> operations,
        long instanceVersion) =>
        ApplyCellOverlay(null, state, operations, instanceVersion, captureNewAddresses: false);

    private NavigationMapInstance ApplyCellOverlay(
        GridWorld? world,
        NavigationOperationCandidate.MapState state,
        ReadOnlySpan<NavigationCellOverlayOperation> operations,
        long instanceVersion,
        bool captureNewAddresses)
    {
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots = _dynamicSlots;
        PersistentIntMap<VoxelIndex> dynamicSlotIndexes = _dynamicSlotIndexes;
        PersistentIntMap<ulong> dynamicHighWater = _dynamicBaselineHighWater;
        PersistentIntMap<NavigationPhysicalPage> physicalPages = _physicalPages;
        PersistentIntMap<NavigationSemanticPage> pages = _semanticPages;
        int nextDynamicSlot = _nextDynamicSlot;
        int copiedSemanticPages = 0;
        var newAddresses = new VoxelIndex[operations.Length];
        int newAddressCount = 0;
        for (int i = 0; i < operations.Length; i++)
        {
            NavigationCellOverlayOperation operation = operations[i];
            int slot = _bakedLookup.Find(operation.Index);
            if (slot < 0 && dynamicSlots.TryGetValue(operation.Index, out NavigationDynamicCellSlot dynamicSlot))
                slot = dynamicSlot.Slot;
            if (slot < 0 && operation.Kind == NavigationCellOverlayOperationKind.Set)
            {
                slot = nextDynamicSlot++;
                dynamicSlot = new NavigationDynamicCellSlot(operation.Index, slot);
                dynamicSlots = dynamicSlots.Set(operation.Index, dynamicSlot);
                dynamicSlotIndexes = dynamicSlotIndexes.Set(slot, operation.Index);
                newAddresses[newAddressCount++] = operation.Index;
            }
            if (slot < 0)
                continue;
            PersistentIntMap<NavigationSemanticPage> updated = ApplySemanticOperation(
                pages,
                slot,
                operation,
                instanceVersion);
            if (!ReferenceEquals(updated, pages))
                copiedSemanticPages++;
            pages = updated;
        }

        NavigationGridGenerationIdentity gridIdentity = GridIdentity;
        ulong baselineHighWater = BaselineHighWater;
        if (captureNewAddresses && newAddressCount > 0 && IsMaterialized)
        {
            ReadOnlySpan<VoxelIndex> requested = newAddresses.AsSpan(0, newAddressCount);
            if (!world!.TryCaptureNavigationBaseline(Map.GridBinding.Key, requested, out GridNavigationBaseline? baseline)
                || baseline == null
                || !GridIdentity.Matches(
                    baseline.WorldSpawnToken,
                    baseline.GridIndex,
                    baseline.GridSpawnToken))
            {
                gridIdentity = default;
                baselineHighWater = 0;
                physicalPages = PersistentIntMap<NavigationPhysicalPage>.Empty;
            }
            else
            {
                for (int i = 0; i < newAddressCount; i++)
                {
                    dynamicSlots.TryGetValue(newAddresses[i], out NavigationDynamicCellSlot slot);
                    NavigationBaselineVoxelState physical = baseline.VoxelStates[i];
                    physicalPages = ApplyPhysicalState(
                    physicalPages,
                    slot.Slot,
                    physical.IsPresent,
                    physical.ObstacleCount,
                    instanceVersion);
                    dynamicHighWater = dynamicHighWater.Set(slot.Slot, baseline.HighWaterSequence);
                }
            }
        }
        else if (!captureNewAddresses && newAddressCount > 0)
        {
            gridIdentity = default;
            baselineHighWater = 0;
            physicalPages = PersistentIntMap<NavigationPhysicalPage>.Empty;
        }
        if (ReferenceEquals(pages, _semanticPages)
            && ReferenceEquals(dynamicSlots, _dynamicSlots)
            && ReferenceEquals(state.Overlay, Overlay))
        {
            return this;
        }
        return new NavigationMapInstance(
            Map,
            BakeVersion,
            state.Overlay,
            DynamicSlotGeneration,
            dynamicSlots,
            dynamicSlotIndexes,
            nextDynamicSlot,
            pages,
            physicalPages,
            dynamicHighWater,
            _bakedLookup,
            _preparedMapRetainedBytes,
            gridIdentity,
            baselineHighWater,
            instanceVersion,
            semanticVersion: instanceVersion,
            physicalVersion: newAddressCount > 0 ? instanceVersion : PhysicalVersion,
            lastBaselineAddressCount: newAddressCount,
            lastCopiedSemanticPages: copiedSemanticPages,
            lastCopiedPhysicalPages: newAddressCount > 0 ? CountTouchedPages(newAddresses, newAddressCount, dynamicSlots) : 0,
            dynamicAddresses: state.DynamicAddresses);
    }

    internal NavigationGraphMapDiagnostic CreateDiagnostic(
        int maximumCells,
        int componentId,
        long componentVersion,
        int incidentEdgeCount,
        out bool truncated)
    {
        int addressedCount = BakedSlotCount + DynamicSlotCount;
        int count = Math.Min(addressedCount, maximumCells);
        truncated = count < addressedCount;
        var cells = new NavigationGraphCellDiagnostic[count];
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            int slot = ordinal < BakedSlotCount
                ? ordinal
                : _dynamicSlots.GetValueAt(ordinal - BakedSlotCount).Slot;
            bool hasCell = TryGetEffectiveCell(slot, out NavigationCell cell);
            TryGetPhysicalState(slot, out bool isPresent, out byte obstacleCount);
            cells[ordinal] = new NavigationGraphCellDiagnostic(
                GetSlotIndex(slot),
                slot,
                GetSemanticSource(slot),
                hasCell,
                cell,
                isPresent,
                obstacleCount);
        }

        return new NavigationGraphMapDiagnostic(
            MapId,
            BakeVersion,
            InstanceVersion,
            Overlay.HighWaterSequence,
            PhysicalVersion,
            componentId,
            componentVersion,
            incidentEdgeCount,
            IsMaterialized,
            GridIdentity.WorldSpawnToken,
            GridIdentity.GridSpawnToken,
            Map.GridBinding.Key,
            Map.GridBinding.Configuration.TopologyKind,
            Map.GridBinding.Configuration.StorageKind,
            LookupKind,
            BakedSlotCount,
            DynamicSlotCount,
            RetainedBytes,
            LastBaselineAddressCount,
            LastCopiedSemanticPages,
            LastCopiedPhysicalPages,
            cells);
    }

    private bool TryCaptureBaseline(GridWorld world, out NavigationMapInstance? materialized)
    {
        VoxelIndex[] addresses = GetAddressSnapshot();
        if (!world.TryCaptureNavigationBaseline(Map.GridBinding.Key, addresses, out GridNavigationBaseline? baseline)
            || baseline == null
            || baseline.WorldSpawnToken != world.SpawnToken
            || !baseline.ConfigurationKey.Equals(Map.GridBinding.Key))
        {
            materialized = null;
            return false;
        }

        PersistentIntMap<NavigationPhysicalPage> pages = BuildPhysicalPages(baseline.VoxelStates);
        materialized = CreateMaterialized(
            new NavigationGridBaselineCapture(addresses, baseline),
            pages,
            addresses.Length,
            InstanceVersion);
        return true;
    }

    private NavigationMapInstance CreateMaterialized(
        in NavigationGridBaselineCapture capture,
        PersistentIntMap<NavigationPhysicalPage> pages,
        int addressCount,
        long instanceVersion) =>
        new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            _dynamicSlots,
            _dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            pages,
            FillDynamicBaselineHighWater(capture.HighWaterSequence),
            _bakedLookup,
            _preparedMapRetainedBytes,
            new NavigationGridGenerationIdentity(
                capture.WorldSpawnToken,
                capture.GridIndex,
                capture.GridSpawnToken,
                capture.ConfigurationKey),
            capture.HighWaterSequence,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: addressCount,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: pages.Count,
            dynamicAddresses: _dynamicAddresses);

    internal PersistentIntMap<NavigationPhysicalPage> AppendPhysicalBaselinePages(
        PersistentIntMap<NavigationPhysicalPage> pages,
        ReadOnlySpan<NavigationBaselineVoxelState> states,
        long pageVersion)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (!states[i].IsPresent && states[i].ObstacleCount == 0)
                continue;
            if (!TryGetSlot(states[i].VoxelIndex, out int slot))
                continue;
            int pageIndex = slot / NavigationPhysicalPage.SlotCount;
            if (!pages.TryGetValue(pageIndex, out NavigationPhysicalPage? page))
            {
                page = new NavigationPhysicalPage(pageIndex, pageVersion);
                pages = pages.Set(pageIndex, page);
            }
            int offset = slot % NavigationPhysicalPage.SlotCount;
            page!.IsPresent[offset] = states[i].IsPresent;
            page.ObstacleCounts[offset] = states[i].IsPresent ? states[i].ObstacleCount : (byte)0;
        }
        return pages;
    }

    internal void CopyCanonicalAddressChunk(
        ref int bakedCursor,
        ref int dynamicCursor,
        Span<VoxelIndex> destination)
    {
        ReadOnlySpan<NavigationCellEntry> baked = Map.CellSpan;
        for (int i = 0; i < destination.Length; i++)
        {
            bool hasBaked = bakedCursor < baked.Length;
            bool hasDynamic = dynamicCursor < _dynamicSlots.Count;
            if (!hasDynamic
                || (hasBaked
                    && baked[bakedCursor].Index.CompareTo(
                        _dynamicSlots.GetKeyAt(dynamicCursor)) < 0))
            {
                destination[i] = baked[bakedCursor++].Index;
            }
            else
            {
                destination[i] = _dynamicSlots.GetKeyAt(dynamicCursor++);
            }
        }
    }

    internal int CopyCanonicalAddresses(Span<VoxelIndex> destination)
    {
        int count = AddressCount;
        if (count > destination.Length)
            return 0;
        int bakedCursor = 0;
        int dynamicCursor = 0;
        CopyCanonicalAddressChunk(
            ref bakedCursor,
            ref dynamicCursor,
            destination.Slice(0, count));
        return count;
    }

    internal int CopyNewCanonicalAddresses(
        NavigationMapInstance previous,
        Span<VoxelIndex> destination)
    {
        if (AddressCount > destination.Length)
            return 0;
        int bakedCursor = 0;
        int dynamicCursor = 0;
        int count = 0;
        for (int i = 0; i < AddressCount; i++)
        {
            Span<VoxelIndex> slot = destination.Slice(count, 1);
            CopyCanonicalAddressChunk(ref bakedCursor, ref dynamicCursor, slot);
            if (!previous.TryGetSlot(slot[0], out _))
                count++;
        }
        return count;
    }

    internal VoxelIndex[] GetAddressSnapshot()
    {
        var addresses = new VoxelIndex[Map.CellSpan.Length + _dynamicSlots.Count];
        for (int i = 0; i < Map.CellSpan.Length; i++)
            addresses[i] = Map.CellSpan[i].Index;
        for (int i = 0; i < _dynamicSlots.Count; i++)
            addresses[Map.CellSpan.Length + i] = _dynamicSlots.GetValueAt(i).Index;
        Array.Sort(addresses, static (left, right) => left.CompareTo(right));
        int unique = 0;
        for (int i = 0; i < addresses.Length; i++)
        {
            if (unique == 0 || addresses[i].CompareTo(addresses[unique - 1]) != 0)
                addresses[unique++] = addresses[i];
        }
        if (unique != addresses.Length)
            Array.Resize(ref addresses, unique);
        return addresses;
    }

    internal VoxelIndex[] GetNewAddressSnapshot(NavigationMapInstance previous)
    {
        VoxelIndex[] addresses = GetAddressSnapshot();
        int count = 0;
        for (int i = 0; i < addresses.Length; i++)
        {
            if (!previous.TryGetSlot(addresses[i], out _))
                addresses[count++] = addresses[i];
        }
        if (count != addresses.Length)
            Array.Resize(ref addresses, count);
        return addresses;
    }

    private NavigationMapInstance MakeDormant(long instanceVersion)
    {
        if (!IsMaterialized && _physicalPages.Count == 0)
            return this;
        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            _dynamicSlots,
            _dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            PersistentIntMap<NavigationPhysicalPage>.Empty,
            _dynamicBaselineHighWater,
            _bakedLookup,
            _preparedMapRetainedBytes,
            default,
            baselineHighWater: 0,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: _physicalPages.Count,
            dynamicAddresses: _dynamicAddresses);
    }

    private NavigationMapInstance WithPhysicalState(
        int slot,
        bool isPresent,
        byte obstacleCount,
        long instanceVersion)
    {
        int pageIndex = slot / NavigationPhysicalPage.SlotCount;
        int offset = slot % NavigationPhysicalPage.SlotCount;
        _physicalPages.TryGetValue(pageIndex, out NavigationPhysicalPage? current);
        if (current != null
            && current.IsPresent[offset] == isPresent
            && current.ObstacleCounts[offset] == obstacleCount)
        {
            return this;
        }
        NavigationPhysicalPage page = current?.Clone(instanceVersion)
            ?? new NavigationPhysicalPage(pageIndex, instanceVersion);
        PersistentIntMap<NavigationPhysicalPage> pages = _physicalPages.Set(pageIndex, page);

        page.IsPresent[offset] = isPresent;
        page.ObstacleCounts[offset] = isPresent ? obstacleCount : (byte)0;
        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            _dynamicSlots,
            _dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            pages,
            _dynamicBaselineHighWater,
            _bakedLookup,
            _preparedMapRetainedBytes,
            GridIdentity,
            BaselineHighWater,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: 1,
            dynamicAddresses: _dynamicAddresses);
    }

    private static PersistentIntMap<NavigationPhysicalPage> ApplyPhysicalState(
        PersistentIntMap<NavigationPhysicalPage> pages,
        int slot,
        bool isPresent,
        byte obstacleCount,
        long version)
    {
        int pageIndex = slot / NavigationPhysicalPage.SlotCount;
        int offset = slot % NavigationPhysicalPage.SlotCount;
        pages.TryGetValue(pageIndex, out NavigationPhysicalPage? current);
        if (current != null
            && current.IsPresent[offset] == isPresent
            && current.ObstacleCounts[offset] == obstacleCount)
        {
            return pages;
        }

        NavigationPhysicalPage page = current?.Clone(version)
            ?? new NavigationPhysicalPage(pageIndex, version);
        page.IsPresent[offset] = isPresent;
        page.ObstacleCounts[offset] = isPresent ? obstacleCount : (byte)0;
        return pages.Set(pageIndex, page);
    }

    private NavigationMapInstance ApplyPhysicalSegment(
        ReadOnlySpan<GridEventInfo> events,
        long instanceVersion)
    {
        if (!IsMaterialized || events.IsEmpty)
            return this;

        PersistentIntMap<NavigationPhysicalPage>? pages = null;
        int copiedPhysicalPages = 0;
        for (int i = 0; i < events.Length; i++)
        {
            GridEventInfo eventInfo = events[i];
            if (!eventInfo.HasVoxelState
                || !GridIdentity.Matches(
                    eventInfo.WorldSpawnToken,
                    eventInfo.GridIndex,
                    eventInfo.GridSpawnToken)
                || !TryGetSlot(eventInfo.VoxelIndex, out int slot)
                || eventInfo.ChangeSequence <= GetBaselineHighWater(slot))
            {
                continue;
            }

            int pageIndex = slot / NavigationPhysicalPage.SlotCount;
            pages ??= _physicalPages;
            pages.TryGetValue(pageIndex, out NavigationPhysicalPage? page);
            if (page == null)
            {
                page = new NavigationPhysicalPage(pageIndex, instanceVersion);
                pages = pages.Set(pageIndex, page);
                copiedPhysicalPages++;
            }
            else if (_physicalPages.TryGetValue(pageIndex, out NavigationPhysicalPage? original)
                && ReferenceEquals(page, original))
            {
                page = page.Clone(instanceVersion);
                pages = pages.Set(pageIndex, page);
                copiedPhysicalPages++;
            }

            int offset = slot % NavigationPhysicalPage.SlotCount;
            page.IsPresent[offset] = eventInfo.IsVoxelPresent;
            page.ObstacleCounts[offset] = eventInfo.IsVoxelPresent
                ? eventInfo.ObstacleCount
                : (byte)0;
        }

        return pages == null
            ? this
            : new NavigationMapInstance(
                Map,
                BakeVersion,
                Overlay,
                DynamicSlotGeneration,
                _dynamicSlots,
                _dynamicSlotIndexes,
                _nextDynamicSlot,
                _semanticPages,
                pages,
                _dynamicBaselineHighWater,
                _bakedLookup,
                _preparedMapRetainedBytes,
                GridIdentity,
                BaselineHighWater,
                instanceVersion,
                SemanticVersion,
                physicalVersion: instanceVersion,
                lastBaselineAddressCount: 0,
                lastCopiedSemanticPages: 0,
                lastCopiedPhysicalPages: copiedPhysicalPages,
                dynamicAddresses: _dynamicAddresses);
    }

    private static int CountTouchedPages(
        VoxelIndex[] addresses,
        int count,
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> slots)
    {
        int pages = 0;
        int priorPage = -1;
        for (int i = 0; i < count; i++)
        {
            if (!slots.TryGetValue(addresses[i], out NavigationDynamicCellSlot slot))
                continue;
            int page = slot.Slot / NavigationPhysicalPage.SlotCount;
            if (page != priorPage)
            {
                pages++;
                priorPage = page;
            }
        }
        return pages;
    }

    private static PersistentVoxelIndexMap<NavigationDynamicCellSlot> BuildDynamicSlots(
        NavigationOperationCandidate.MapState state,
        NavigationMapInstance? previous,
        out int nextSlot)
    {
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> slots = previous != null
            && previous.DynamicSlotGeneration == state.DynamicSlotGeneration
            ? previous._dynamicSlots
            : PersistentVoxelIndexMap<NavigationDynamicCellSlot>.Empty;
        nextSlot = previous != null && previous.DynamicSlotGeneration == state.DynamicSlotGeneration
            ? previous._nextDynamicSlot
            : DynamicSlotBase;

        for (int i = 0; i < state.DynamicAddresses.Count; i++)
        {
            VoxelIndex index = state.DynamicAddresses.GetKeyAt(i);
            if (slots.TryGetValue(index, out _))
            {
                continue;
            }
            slots = slots.Set(
                index,
                new NavigationDynamicCellSlot(index, nextSlot++));
        }
        return slots;
    }

    private static PersistentIntMap<VoxelIndex> BuildDynamicSlotIndexes(
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> slots)
    {
        PersistentIntMap<VoxelIndex> indexes = PersistentIntMap<VoxelIndex>.Empty;
        for (int i = 0; i < slots.Count; i++)
        {
            NavigationDynamicCellSlot slot = slots.GetValueAt(i);
            indexes = indexes.Set(slot.Slot, slot.Index);
        }
        return indexes;
    }

    private static PersistentIntMap<ulong> BuildDynamicBaselineHighWater(
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> slots,
        NavigationMapInstance? previous)
    {
        PersistentIntMap<ulong> highWater = PersistentIntMap<ulong>.Empty;
        if (previous == null)
            return highWater;
        for (int i = 0; i < slots.Count; i++)
        {
            NavigationDynamicCellSlot slot = slots.GetValueAt(i);
            if (previous._dynamicBaselineHighWater.TryGetValue(slot.Slot, out ulong prior))
                highWater = highWater.Set(slot.Slot, prior);
        }
        return highWater;
    }

    private PersistentIntMap<ulong> FillDynamicBaselineHighWater(ulong highWater)
    {
        PersistentIntMap<ulong> result = PersistentIntMap<ulong>.Empty;
        for (int i = 0; i < _dynamicSlots.Count; i++)
            result = result.Set(_dynamicSlots.GetValueAt(i).Slot, highWater);
        return result;
    }

    private ulong GetBaselineHighWater(int slot)
    {
        if (slot < BakedSlotCount)
            return BaselineHighWater;
        return _dynamicBaselineHighWater.TryGetValue(slot, out ulong highWater)
            ? highWater
            : BaselineHighWater;
    }

    private static PersistentIntMap<NavigationSemanticPage> BuildSemanticPages(
        NavigationOperationCandidate.MapState state,
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots,
        long version)
    {
        if (state.Overlay.CellCount == 0)
            return PersistentIntMap<NavigationSemanticPage>.Empty;

        PersistentIntMap<NavigationSemanticPage> pages = PersistentIntMap<NavigationSemanticPage>.Empty;
        for (int i = 0; i < state.Overlay.CellCount; i++)
        {
            NavigationCellOverlayOperation operation = state.Overlay.GetCellAt(i);
            int slot = state.Map.FindCellIndex(operation.Index);
            if (slot < 0)
            {
                if (dynamicSlots.TryGetValue(operation.Index, out NavigationDynamicCellSlot dynamicSlot))
                    slot = dynamicSlot.Slot;
            }
            if (slot < 0)
                continue;

            int pageIndex = slot / NavigationSemanticPage.SlotCount;
            if (!pages.TryGetValue(pageIndex, out NavigationSemanticPage? page))
                page = new NavigationSemanticPage(pageIndex, version);
            int offset = slot % NavigationSemanticPage.SlotCount;
            page!.HasOverride[offset] = operation.Kind == NavigationCellOverlayOperationKind.Set;
            page.IsSuppressed[offset] = operation.Kind == NavigationCellOverlayOperationKind.Suppress;
            page.Cells[offset] = operation.Cell;
            pages = pages.Set(pageIndex, page);
        }
        return pages;
    }

    private static PersistentIntMap<NavigationSemanticPage> ApplySemanticOperation(
        PersistentIntMap<NavigationSemanticPage> pages,
        int slot,
        NavigationCellOverlayOperation operation,
        long version) => ApplySemanticOperation(
        pages,
        slot,
        operation,
        version,
        out _);

    private static PersistentIntMap<NavigationSemanticPage> ApplySemanticOperation(
        PersistentIntMap<NavigationSemanticPage> pages,
        int slot,
        NavigationCellOverlayOperation operation,
        long version,
        out int copiedNodeCount)
    {
        int pageIndex = slot / NavigationSemanticPage.SlotCount;
        NavigationSemanticPage page;
        if (pages.TryGetValue(pageIndex, out NavigationSemanticPage? existing))
        {
            page = existing!.Clone(version);
        }
        else
        {
            if (operation.Kind == NavigationCellOverlayOperationKind.RevertToBake)
            {
                copiedNodeCount = 0;
                return pages;
            }
            page = new NavigationSemanticPage(pageIndex, version);
        }

        int offset = slot % NavigationSemanticPage.SlotCount;
        page.HasOverride[offset] = operation.Kind == NavigationCellOverlayOperationKind.Set;
        page.IsSuppressed[offset] = operation.Kind == NavigationCellOverlayOperationKind.Suppress;
        page.Cells[offset] = operation.Cell;
        return page.IsEmpty()
            ? pages.Remove(pageIndex, out copiedNodeCount)
            : pages.Set(pageIndex, page, out copiedNodeCount);
    }

    private PersistentIntMap<NavigationPhysicalPage> BuildPhysicalPages(
        ReadOnlySpan<NavigationBaselineVoxelState> states) =>
        AppendPhysicalBaselinePages(
            PersistentIntMap<NavigationPhysicalPage>.Empty,
            states,
            InstanceVersion);

    private NavigationSemanticPage? FindSemanticPage(int pageIndex)
    {
        _semanticPages.TryGetValue(pageIndex, out NavigationSemanticPage? page);
        return page;
    }

    private NavigationCellSemanticSource GetSemanticSource(int slot)
    {
        NavigationSemanticPage? page = FindSemanticPage(slot / NavigationSemanticPage.SlotCount);
        int offset = slot % NavigationSemanticPage.SlotCount;
        if (page != null && page.IsSuppressed[offset])
            return NavigationCellSemanticSource.OverlaySuppressed;
        if (page != null && page.HasOverride[offset])
        {
            return slot >= BakedSlotCount
                ? NavigationCellSemanticSource.DynamicOverlaySet
                : NavigationCellSemanticSource.OverlaySet;
        }
        return slot >= BakedSlotCount
            ? NavigationCellSemanticSource.DynamicInactive
            : NavigationCellSemanticSource.Baked;
    }

    private NavigationPhysicalPage? FindPhysicalPage(int pageIndex)
    {
        _physicalPages.TryGetValue(pageIndex, out NavigationPhysicalPage? page);
        return page;
    }

    private static bool IsBroad(GridEventKind kind) =>
        kind == GridEventKind.WorldReset
        || kind == GridEventKind.GridAdded
        || kind == GridEventKind.GridRemoved
        || kind == GridEventKind.GridChanged;

    private long EstimateRetainedBytes() => checked(
        192L
        + Overlay.RetainedBytes
        + _dynamicSlots.RetainedBytes
        + _dynamicAddresses.RetainedBytes
        + _dynamicSlotIndexes.RetainedBytes
        + _semanticPages.RetainedBytes
        + ((long)_semanticPages.Count * 4_400L)
        + _physicalPages.RetainedBytes
        + ((long)_physicalPages.Count * 320L)
        + _dynamicBaselineHighWater.RetainedBytes
        + _preparedMapRetainedBytes);
}
