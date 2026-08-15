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
        ulong gridHighWaterSequence,
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
        GridHighWaterSequence = gridHighWaterSequence;
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

    internal ulong GridHighWaterSequence { get; }

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

    internal bool TryGetSemanticState(
        VoxelIndex index,
        out NavigationCellSemanticSource source,
        out bool hasCell,
        out NavigationCell cell)
    {
        if (!TryGetSlot(index, out int slot))
        {
            source = default;
            hasCell = false;
            cell = default;
            return false;
        }
        source = GetSemanticSource(slot);
        hasCell = TryGetEffectiveCell(slot, out cell);
        return true;
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
        if (!IsMaterialized)
            return this;
        if (!eventInfo.HasVoxelState
            || !TryGetSlot(eventInfo.VoxelIndex, out int slot)
            || eventInfo.ChangeSequence <= GetBaselineHighWater(slot))
        {
            return WithGridHighWater(eventInfo.ChangeSequence, instanceVersion);
        }

        return WithPhysicalState(
            slot,
            eventInfo.IsVoxelPresent,
            eventInfo.ObstacleCount,
            instanceVersion,
            eventInfo.ChangeSequence);
    }

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
                capture.GridHighWaterSequence,
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
            baseline.GridHighWaterSequence,
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
            capture.GridHighWaterSequence,
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
            gridHighWaterSequence: 0,
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
        long instanceVersion,
        ulong gridHighWaterSequence)
    {
        PersistentIntMap<NavigationPhysicalPage> pages = ApplyPhysicalState(
            _physicalPages,
            slot,
            isPresent,
            obstacleCount,
            instanceVersion);
        if (ReferenceEquals(pages, _physicalPages))
            return WithGridHighWater(gridHighWaterSequence, instanceVersion);

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
            gridHighWaterSequence,
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
        ulong gridHighWaterSequence = GridHighWaterSequence;
        int copiedPhysicalPages = 0;
        for (int i = 0; i < events.Length; i++)
        {
            GridEventInfo eventInfo = events[i];
            if (!GridIdentity.Matches(
                    eventInfo.WorldSpawnToken,
                    eventInfo.GridIndex,
                    eventInfo.GridSpawnToken))
            {
                continue;
            }
            if (eventInfo.ChangeSequence > gridHighWaterSequence)
                gridHighWaterSequence = eventInfo.ChangeSequence;
            if (!eventInfo.HasVoxelState
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

        return pages == null && gridHighWaterSequence == GridHighWaterSequence
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
                pages ?? _physicalPages,
                _dynamicBaselineHighWater,
                _bakedLookup,
                _preparedMapRetainedBytes,
                GridIdentity,
                BaselineHighWater,
                gridHighWaterSequence,
                instanceVersion,
                SemanticVersion,
                physicalVersion: pages == null ? PhysicalVersion : instanceVersion,
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
        200L
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

    private NavigationMapInstance WithGridHighWater(
        ulong gridHighWaterSequence,
        long instanceVersion)
    {
        if (gridHighWaterSequence <= GridHighWaterSequence)
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
            _physicalPages,
            _dynamicBaselineHighWater,
            _bakedLookup,
            _preparedMapRetainedBytes,
            GridIdentity,
            BaselineHighWater,
            gridHighWaterSequence,
            instanceVersion,
            SemanticVersion,
            PhysicalVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: 0,
            dynamicAddresses: _dynamicAddresses);
    }
}
