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
    private readonly PersistentIntMap<ulong> _dynamicBaselineCapturedChangeSequences;
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
        PersistentIntMap<ulong> dynamicBaselineCapturedChangeSequences,
        NavigationBakedCellLookup bakedLookup,
        long preparedMapRetainedBytes,
        NavigationGridGenerationIdentity gridIdentity,
        ulong baselineCapturedChangeSequence,
        ulong gridLastChangeSequence,
        long instanceVersion,
        long semanticVersion,
        long physicalVersion,
        int lastBaselineAddressCount,
        int lastCopiedSemanticPages,
        int lastCopiedPhysicalPages,
        PersistentVoxelIndexMap<byte> dynamicAddresses)
    {
        Map = map;
        BakeVersion = bakeVersion;
        Overlay = overlay;
        DynamicSlotGeneration = dynamicSlotGeneration;
        _dynamicSlots = dynamicSlots;
        _dynamicAddresses = dynamicAddresses;
        _dynamicSlotIndexes = dynamicSlotIndexes;
        _nextDynamicSlot = nextDynamicSlot;
        _semanticPages = semanticPages;
        _physicalPages = physicalPages;
        _dynamicBaselineCapturedChangeSequences = dynamicBaselineCapturedChangeSequences;
        _bakedLookup = bakedLookup;
        _preparedMapRetainedBytes = preparedMapRetainedBytes;
        GridIdentity = gridIdentity;
        BaselineCapturedChangeSequence = baselineCapturedChangeSequence;
        GridLastChangeSequence = gridLastChangeSequence;
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

    internal ulong BaselineCapturedChangeSequence { get; }

    internal ulong GridLastChangeSequence { get; }

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
        + _dynamicBaselineCapturedChangeSequences.PersistentNodeCount;

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
        bool found = TryGetSlotIndex(slot, out VoxelIndex index);
        System.Diagnostics.Debug.Assert(found, "Diagnostics enumerate only addressed slots.");
        return index;
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

        if (Map.DefaultCell.HasValue)
        {
            cell = Map.DefaultCell.Value;
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

    internal TraversalMedia GetEffectiveMedia(VoxelIndex index) =>
        TryGetSlot(index, out int slot)
        && TryGetEffectiveCell(slot, out NavigationCell cell)
            ? cell.Media
            : TraversalMedia.None;

    internal bool IsPhysicallyPresent(VoxelIndex index) =>
        TryGetSlot(index, out int slot)
        && TryGetPhysicalState(slot, out bool isPresent, out _)
        && isPresent;

    internal GraphPageDependency GetPageDependency(int pageIndex, long transitionVersion = 0)
    {
        _semanticPages.TryGetValue(pageIndex, out NavigationSemanticPage? semantic);
        _physicalPages.TryGetValue(pageIndex, out NavigationPhysicalPage? physical);
        return new GraphPageDependency(
            MapId,
            BakeVersion,
            DynamicSlotGeneration,
            pageIndex,
            semantic?.Version ?? 0,
            physical?.Version ?? 0,
            transitionVersion);
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
        if (!eventInfo.HasVoxelState
            || !TryGetSlot(eventInfo.VoxelIndex, out int slot)
            || eventInfo.ChangeSequence <= GetBaselineCapturedChangeSequence(slot))
        {
            return WithGridLastChangeSequence(eventInfo.ChangeSequence, instanceVersion);
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
        if (!capture.IsRequested
            || !capture.HasBaseline)
        {
            return MakeDormant(instanceVersion);
        }

        NavigationMapInstance materializing = capture.PreparedInstance ?? this;
        PersistentIntMap<NavigationPhysicalPage> pages = capture.PreparedPages
            ?? (baseline == null
                ? materializing._physicalPages
                : materializing.BuildPhysicalPages(baseline.VoxelStates));
        return materializing.CreateMaterialized(
            capture,
            pages,
            materializing.BakedSlotCount + materializing.DynamicSlotCount,
            instanceVersion);
    }

    internal NavigationMapInstance MaterializeDelta(
        NavigationMapInstance previous,
        in NavigationGridBaselineCapture capture,
        long instanceVersion)
    {
        GridNavigationBaseline? baseline = capture.Baseline;
        if (!capture.IsRequested
            || !capture.IsDelta)
        {
            return Materialize(capture, instanceVersion);
        }
        if (baseline == null)
        {
            return MakeDormant(instanceVersion);
        }

        PersistentIntMap<NavigationPhysicalPage> pages = previous._physicalPages;
        PersistentIntMap<ulong> dynamicCapturedChangeSequences =
            _dynamicBaselineCapturedChangeSequences;
        var capturedAddresses = new VoxelIndex[capture.AddressCount];
        for (int i = 0; i < capture.AddressCount; i++)
        {
            VoxelIndex address = baseline.VoxelStates[i].VoxelIndex;
            System.Diagnostics.Debug.Assert(!previous.TryGetSlot(address, out _));
            bool found = TryGetSlot(address, out int slot);
            System.Diagnostics.Debug.Assert(found);
            System.Diagnostics.Debug.Assert(slot >= DynamicSlotBase);
            NavigationBaselineVoxelState physical = baseline.VoxelStates[i];
            pages = ApplyPhysicalState(
                pages,
                slot,
                physical.IsPresent,
                physical.ObstacleCount,
                instanceVersion);
            dynamicCapturedChangeSequences = dynamicCapturedChangeSequences.Set(
                slot,
                baseline.CapturedChangeSequence);
            capturedAddresses[i] = address;
        }
        System.Diagnostics.Debug.Assert(capture.AddressCount > 0);

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
            dynamicCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            new NavigationGridGenerationIdentity(
                baseline.WorldSpawnToken,
                baseline.GridIndex,
                baseline.GridSpawnToken,
                baseline.ConfigurationKey),
            previous.BaselineCapturedChangeSequence,
            baseline.GridLastChangeSequence,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: capture.AddressCount,
            lastCopiedSemanticPages: LastCopiedSemanticPages,
            lastCopiedPhysicalPages: CountTouchedPages(
                capturedAddresses,
                capture.AddressCount,
                _dynamicSlots),
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
            FillDynamicBaselineCapturedChangeSequences(capture.CapturedChangeSequence),
            _bakedLookup,
            _preparedMapRetainedBytes,
            new NavigationGridGenerationIdentity(
                capture.WorldSpawnToken,
                capture.GridIndex,
                capture.GridSpawnToken,
                capture.ConfigurationKey),
            capture.CapturedChangeSequence,
            capture.GridLastChangeSequence,
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
            bool found = TryGetSlot(states[i].VoxelIndex, out int slot);
            System.Diagnostics.Debug.Assert(found);
            int pageIndex = slot / NavigationPhysicalPage.SlotCount;
            if (!pages.TryGetValue(pageIndex, out NavigationPhysicalPage? page))
            {
                page = new NavigationPhysicalPage(pageIndex, pageVersion);
                pages = pages.Set(pageIndex, page);
            }
            int offset = slot % NavigationPhysicalPage.SlotCount;
            page!.IsPresent[offset] = states[i].IsPresent;
            page.ObstacleCounts[offset] = states[i].ObstacleCount;
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

    internal bool TryCopyNewCanonicalAddresses(
        NavigationMapInstance previous,
        Span<VoxelIndex> destination,
        out int count)
    {
        int bakedCursor = 0;
        int dynamicCursor = 0;
        count = 0;
        Span<VoxelIndex> slot = stackalloc VoxelIndex[1];
        for (int i = 0; i < AddressCount; i++)
        {
            CopyCanonicalAddressChunk(ref bakedCursor, ref dynamicCursor, slot);
            if (previous.TryGetSlot(slot[0], out _))
                continue;
            if (count == destination.Length)
                return false;
            destination[count++] = slot[0];
        }
        return true;
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
            _dynamicBaselineCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            default,
            baselineCapturedChangeSequence: 0,
            gridLastChangeSequence: 0,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: _physicalPages.Count,
            dynamicAddresses: _dynamicAddresses);
    }

    internal bool TryGetDefaultBaselineSeedSlot(
        int ordinal,
        out NavigationDynamicCellSlot slot,
        out bool retain)
    {
        if ((uint)ordinal >= (uint)_dynamicSlots.Count)
        {
            slot = default;
            retain = false;
            return false;
        }
        VoxelIndex index = _dynamicSlots.GetKeyAt(ordinal);
        slot = _dynamicSlots.GetValueAt(ordinal);
        retain = _dynamicAddresses.TryGetValue(index, out _);
        return true;
    }

    internal bool TryGetDynamicSlot(
        VoxelIndex index,
        out NavigationDynamicCellSlot slot) =>
        _dynamicSlots.TryGetValue(index, out slot);

    internal NavigationMapInstance CreateDefaultBaselineSeed(
        long instanceVersion,
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots,
        PersistentIntMap<VoxelIndex> dynamicSlotIndexes)
    {
        if (!Map.DefaultCell.HasValue)
            return this;

        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            dynamicSlots,
            dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            PersistentIntMap<NavigationPhysicalPage>.Empty,
            PersistentIntMap<ulong>.Empty,
            _bakedLookup,
            _preparedMapRetainedBytes,
            default,
            baselineCapturedChangeSequence: 0,
            gridLastChangeSequence: 0,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: LastCopiedSemanticPages,
            lastCopiedPhysicalPages: 0,
            dynamicAddresses: _dynamicAddresses);
    }

    internal NavigationMapInstance AppendDefaultBaselineStates(
        NavigationMapInstance source,
        ReadOnlySpan<NavigationBaselineVoxelState> states,
        ulong capturedChangeSequence,
        long instanceVersion)
    {
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> dynamicSlots = _dynamicSlots;
        PersistentIntMap<VoxelIndex> dynamicSlotIndexes = _dynamicSlotIndexes;
        PersistentIntMap<NavigationPhysicalPage> physicalPages = _physicalPages;
        PersistentIntMap<ulong> dynamicCapturedChangeSequences =
            _dynamicBaselineCapturedChangeSequences;
        int nextDynamicSlot = _nextDynamicSlot;
        for (int i = 0; i < states.Length; i++)
        {
            NavigationBaselineVoxelState state = states[i];
            int slot = _bakedLookup.Find(state.VoxelIndex);
            if (slot < 0
                && dynamicSlots.TryGetValue(
                    state.VoxelIndex,
                    out NavigationDynamicCellSlot existing))
            {
                slot = existing.Slot;
            }
            if (slot < 0 && state.IsPresent)
            {
                if (source._dynamicSlots.TryGetValue(
                        state.VoxelIndex,
                        out NavigationDynamicCellSlot prior))
                {
                    slot = prior.Slot;
                }
                else
                {
                    slot = nextDynamicSlot++;
                }
                var added = new NavigationDynamicCellSlot(state.VoxelIndex, slot);
                dynamicSlots = dynamicSlots.Set(state.VoxelIndex, added);
                dynamicSlotIndexes = dynamicSlotIndexes.Set(slot, state.VoxelIndex);
            }
            if (slot < 0)
                continue;
            physicalPages = ApplyPhysicalState(
                physicalPages,
                slot,
                state.IsPresent,
                state.ObstacleCount,
                instanceVersion);
            if (slot >= DynamicSlotBase)
                dynamicCapturedChangeSequences = dynamicCapturedChangeSequences.Set(
                    slot,
                    capturedChangeSequence);
        }

        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            DynamicSlotGeneration,
            dynamicSlots,
            dynamicSlotIndexes,
            nextDynamicSlot,
            _semanticPages,
            physicalPages,
            dynamicCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            default,
            baselineCapturedChangeSequence: 0,
            gridLastChangeSequence: 0,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: LastCopiedSemanticPages,
            lastCopiedPhysicalPages: physicalPages.Count,
            dynamicAddresses: _dynamicAddresses);
    }

    internal NavigationMapInstance CompleteDefaultBaseline(
        long instanceVersion,
        bool hasSameDynamicSlots)
    {
        long dynamicSlotGeneration = hasSameDynamicSlots
            ? DynamicSlotGeneration
            : instanceVersion;
        return new NavigationMapInstance(
            Map,
            BakeVersion,
            Overlay,
            dynamicSlotGeneration,
            _dynamicSlots,
            _dynamicSlotIndexes,
            _nextDynamicSlot,
            _semanticPages,
            _physicalPages,
            _dynamicBaselineCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            default,
            baselineCapturedChangeSequence: 0,
            gridLastChangeSequence: 0,
            instanceVersion,
            SemanticVersion,
            physicalVersion: instanceVersion,
            lastBaselineAddressCount: AddressCount,
            lastCopiedSemanticPages: LastCopiedSemanticPages,
            lastCopiedPhysicalPages: _physicalPages.Count,
            dynamicAddresses: _dynamicAddresses);
    }

    internal long DefaultBaselineRetainedBytes => checked(
        200L
        + _dynamicSlots.RetainedBytes
        + _dynamicSlotIndexes.RetainedBytes
        + _physicalPages.RetainedBytes
        + ((long)_physicalPages.Count * 320L)
        + _dynamicBaselineCapturedChangeSequences.RetainedBytes);

    internal int DefaultBaselinePersistentPages => checked(
        4
        + _dynamicSlots.PersistentNodeCount
        + _dynamicSlotIndexes.PersistentNodeCount
        + (_physicalPages.PersistentNodeCount * 2)
        + _dynamicBaselineCapturedChangeSequences.PersistentNodeCount);

    private NavigationMapInstance WithPhysicalState(
        int slot,
        bool isPresent,
        byte obstacleCount,
        long instanceVersion,
        ulong gridLastChangeSequence)
    {
        PersistentIntMap<NavigationPhysicalPage> pages = ApplyPhysicalState(
            _physicalPages,
            slot,
            isPresent,
            obstacleCount,
            instanceVersion);
        if (ReferenceEquals(pages, _physicalPages))
            return WithGridLastChangeSequence(gridLastChangeSequence, instanceVersion);

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
            _dynamicBaselineCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            GridIdentity,
            BaselineCapturedChangeSequence,
            gridLastChangeSequence,
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
        ulong gridLastChangeSequence = GridLastChangeSequence;
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
            if (eventInfo.ChangeSequence > gridLastChangeSequence)
                gridLastChangeSequence = eventInfo.ChangeSequence;
            if (!eventInfo.HasVoxelState
                || !TryGetSlot(eventInfo.VoxelIndex, out int slot)
                || eventInfo.ChangeSequence <= GetBaselineCapturedChangeSequence(slot))
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

        return pages == null && gridLastChangeSequence == GridLastChangeSequence
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
                _dynamicBaselineCapturedChangeSequences,
                _bakedLookup,
                _preparedMapRetainedBytes,
                GridIdentity,
                BaselineCapturedChangeSequence,
                gridLastChangeSequence,
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
            bool found = slots.TryGetValue(addresses[i], out NavigationDynamicCellSlot slot);
            System.Diagnostics.Debug.Assert(found);
            int page = slot.Slot / NavigationPhysicalPage.SlotCount;
            if (page != priorPage)
            {
                pages++;
                priorPage = page;
            }
        }
        return pages;
    }

    private PersistentIntMap<ulong> FillDynamicBaselineCapturedChangeSequences(
        ulong capturedChangeSequence)
    {
        PersistentIntMap<ulong> result = PersistentIntMap<ulong>.Empty;
        for (int i = 0; i < _dynamicSlots.Count; i++)
            result = result.Set(
                _dynamicSlots.GetValueAt(i).Slot,
                capturedChangeSequence);
        return result;
    }

    private ulong GetBaselineCapturedChangeSequence(int slot)
    {
        if (slot < BakedSlotCount)
            return BaselineCapturedChangeSequence;
        bool found = _dynamicBaselineCapturedChangeSequences.TryGetValue(
            slot,
            out ulong capturedChangeSequence);
        System.Diagnostics.Debug.Assert(found);
        return capturedChangeSequence;
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
        if (slot >= BakedSlotCount && Map.DefaultCell.HasValue)
            return NavigationCellSemanticSource.Baked;
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
        + _dynamicBaselineCapturedChangeSequences.RetainedBytes
        + _preparedMapRetainedBytes);

    private NavigationMapInstance WithGridLastChangeSequence(
        ulong gridLastChangeSequence,
        long instanceVersion)
    {
        if (gridLastChangeSequence <= GridLastChangeSequence)
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
            _dynamicBaselineCapturedChangeSequences,
            _bakedLookup,
            _preparedMapRetainedBytes,
            GridIdentity,
            BaselineCapturedChangeSequence,
            gridLastChangeSequence,
            instanceVersion,
            SemanticVersion,
            PhysicalVersion,
            lastBaselineAddressCount: 0,
            lastCopiedSemanticPages: 0,
            lastCopiedPhysicalPages: 0,
            dynamicAddresses: _dynamicAddresses);
    }
}
