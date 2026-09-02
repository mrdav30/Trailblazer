//=======================================================================
// NavigationBaselineRebuild.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.InteropServices;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Accumulates one immutable requested-address baseline under a finite frame budget.</summary>
internal sealed class NavigationBaselineRebuild
{
    private readonly NavigationMap _map;
    private readonly NavigationMapInstance _source;
    private readonly long _bakeVersion;
    private readonly long _overlayHighWater;
    private readonly long _dynamicSlotGeneration;
    private readonly int _dynamicSlotCount;
    private readonly int _addressCount;
    private readonly long _pageVersion;
    private readonly bool _discoversDefault;
    private GridCoveredAddressCursor? _coveredCursor;
    private NavigationMapInstance? _defaultCandidate;
    private PersistentVoxelIndexMap<NavigationDynamicCellSlot> _defaultSeedSlots =
        PersistentVoxelIndexMap<NavigationDynamicCellSlot>.Empty;
    private PersistentIntMap<VoxelIndex> _defaultSeedIndexes =
        PersistentIntMap<VoxelIndex>.Empty;
    private NavigationSurfaceComponentKeySet _structuralChangedStates =
        NavigationSurfaceComponentKeySet.Empty;
    private GridCoveredAddressGeneration _coveredGeneration;
    private bool _coveredGenerationBound;
    private PersistentIntMap<NavigationPhysicalPage> _pages =
        PersistentIntMap<NavigationPhysicalPage>.Empty;
    private int _cursor;
    private int _bakedCursor;
    private int _dynamicCursor;
    private int _defaultSeedCursor;
    private int _omittedDefaultSeedSlotCount;
    private int _omittedPhysicalDefaultSlotCount;
    private ulong _capturedChangeSequence;
    private long _worldSpawnToken;
    private ushort _gridIndex = ushort.MaxValue;
    private long _gridSpawnToken;
    private ulong _gridLastChangeSequence;
    private bool _hasIdentity;
    private bool _completed;
    private bool _capacityBlocked;
    private bool _hasNewDefaultSlot;
    private bool _defaultPhysicalAddressSetChanged;
    private NavigationGridBaselineCapture _completedCapture;

    internal NavigationBaselineRebuild(NavigationMapInstance instance)
    {
        _source = instance;
        _map = instance.Map;
        _bakeVersion = instance.BakeVersion;
        _overlayHighWater = instance.Overlay.HighWaterSequence;
        _dynamicSlotGeneration = instance.DynamicSlotGeneration;
        _dynamicSlotCount = instance.DynamicSlotCount;
        _addressCount = instance.AddressCount;
        _pageVersion = instance.InstanceVersion;
        _discoversDefault = instance.Map.DefaultCell.HasValue;
        if (_discoversDefault)
            _coveredCursor = new GridCoveredAddressCursor(generationCapacity: 1);
    }

    internal string MapId => _map.MapId;

    internal long RetainedBytes => _discoversDefault
        ? checked(320L
            + _coveredCursor!.RetainedBytes
            + (_defaultCandidate?.DefaultBaselineRetainedBytes
                ?? _defaultSeedSlots.RetainedBytes + _defaultSeedIndexes.RetainedBytes)
            + _structuralChangedStates.RetainedBytes)
        : checked(192L + _pages.RetainedBytes + ((long)_pages.Count * 320L));

    internal int PersistentPageCount => _discoversDefault
        ? checked(1
            + (_defaultCandidate?.DefaultBaselinePersistentPages
                ?? 2
                    + _defaultSeedSlots.PersistentNodeCount
                    + _defaultSeedIndexes.PersistentNodeCount)
            + _structuralChangedStates.PersistentPageCount)
        : 1 + (_pages.PersistentNodeCount * 2);

    internal bool IsComplete => _completed;

    internal bool IsCapacityBlocked => _capacityBlocked;

    internal bool RequiresCoveredDiscovery => _discoversDefault;

    internal bool TryGetCompletedCapture(out NavigationGridBaselineCapture capture)
    {
        capture = _completedCapture;
        return _completed;
    }

    internal static void GetRetainedTotals(
        PersistentStringMap<NavigationBaselineRebuild> rebuilds,
        out long retainedBytes,
        out int persistentPageCount)
    {
        if (rebuilds.Count == 0)
        {
            retainedBytes = 0;
            persistentPageCount = 0;
            return;
        }

        retainedBytes = rebuilds.RetainedBytes;
        persistentPageCount = 1 + rebuilds.PersistentNodeCount;
        for (int i = 0; i < rebuilds.Count; i++)
        {
            NavigationBaselineRebuild rebuild = rebuilds.GetValueAt(i);
            retainedBytes = checked(retainedBytes + rebuild.RetainedBytes);
            persistentPageCount = checked(persistentPageCount + rebuild.PersistentPageCount);
        }
    }

    internal static int CountCapacityBlocked(
        PersistentStringMap<NavigationBaselineRebuild> rebuilds)
    {
        int count = 0;
        for (int i = 0; i < rebuilds.Count; i++)
        {
            if (rebuilds.GetValueAt(i).IsCapacityBlocked)
                count++;
        }
        return count;
    }

    internal bool Matches(NavigationMapInstance instance) =>
        ReferenceEquals(_map, instance.Map)
        && _bakeVersion == instance.BakeVersion
        && _overlayHighWater == instance.Overlay.HighWaterSequence
        && _dynamicSlotGeneration == instance.DynamicSlotGeneration
        && _dynamicSlotCount == instance.DynamicSlotCount;

    internal static bool MatchesCapturedGridIdentity(
        long capturedWorldSpawnToken,
        ushort capturedGridIndex,
        long capturedGridSpawnToken,
        ulong capturedGridLastChangeSequence,
        long expectedWorldSpawnToken,
        ushort expectedGridIndex,
        long expectedGridSpawnToken,
        ulong expectedGridLastChangeSequence) =>
        capturedWorldSpawnToken == expectedWorldSpawnToken
        && capturedGridIndex == expectedGridIndex
        && capturedGridSpawnToken == expectedGridSpawnToken
        && capturedGridLastChangeSequence == expectedGridLastChangeSequence;

    internal static bool IsCoveredBaselineCurrent(
        bool captured,
        GridNavigationBaseline? baseline,
        ulong expectedCapturedChangeSequence,
        long expectedWorldSpawnToken,
        ushort expectedGridIndex,
        long expectedGridSpawnToken,
        ulong expectedGridLastChangeSequence) =>
        captured
        && baseline != null
        && baseline.CapturedChangeSequence == expectedCapturedChangeSequence
        && MatchesCapturedGridIdentity(
            baseline.WorldSpawnToken,
            baseline.GridIndex,
            baseline.GridSpawnToken,
            baseline.GridLastChangeSequence,
            expectedWorldSpawnToken,
            expectedGridIndex,
            expectedGridSpawnToken,
            expectedGridLastChangeSequence);

    internal int Advance(
        GridWorld world,
        NavigationMapInstance instance,
        int maximumAddresses,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        Span<VoxelIndex> addressScratch,
        Span<GridCoveredAddress> coveredAddressScratch,
        out NavigationGridBaselineCapture capture,
        out bool completed)
    {
        capture = default;
        completed = false;
        _completed = false;
        if (_capacityBlocked)
            return 0;
        if (maximumAddresses <= 0)
            return 0;
        if (_discoversDefault)
        {
            return AdvanceDefaultDiscovery(
                world,
                maximumAddresses,
                maximumRetainedBytes,
                maximumPersistentPages,
                addressScratch,
                coveredAddressScratch,
                out capture,
                out completed);
        }

        int count = Math.Min(maximumAddresses, _addressCount - _cursor);
        Span<VoxelIndex> requestedScratch = addressScratch.Slice(0, count);
        int bakedCursor = _bakedCursor;
        int dynamicCursor = _dynamicCursor;
        instance.CopyCanonicalAddressChunk(
            ref bakedCursor,
            ref dynamicCursor,
            requestedScratch);
        ReadOnlySpan<VoxelIndex> requested = requestedScratch;
        if (!world.TryCaptureNavigationBaseline(
                _map.GridBinding.Key,
                requested,
                out GridNavigationBaseline? baseline)
            || baseline == null)
        {
            completed = true;
            _completed = true;
            capture = new NavigationGridBaselineCapture(0, baseline: null, isDelta: false);
            _completedCapture = capture;
            return count;
        }

        if (_hasIdentity
            && !MatchesCapturedGridIdentity(
                baseline.WorldSpawnToken,
                baseline.GridIndex,
                baseline.GridSpawnToken,
                baseline.GridLastChangeSequence,
                _worldSpawnToken,
                _gridIndex,
                _gridSpawnToken,
                _gridLastChangeSequence))
        {
            ResetProgress();
            return count;
        }

        if (!_hasIdentity)
        {
            _capturedChangeSequence = baseline.CapturedChangeSequence;
            _worldSpawnToken = baseline.WorldSpawnToken;
            _gridIndex = baseline.GridIndex;
            _gridSpawnToken = baseline.GridSpawnToken;
            _gridLastChangeSequence = baseline.GridLastChangeSequence;
            _hasIdentity = true;
        }
        _capturedChangeSequence = baseline.CapturedChangeSequence;

        PersistentIntMap<NavigationPhysicalPage> pages = instance.AppendPhysicalBaselinePages(
            _pages,
            baseline.VoxelStates,
            _pageVersion);
        long retainedBytes = EstimateRetainedBytes(pages);
        int persistentPageCount = EstimatePersistentPageCount(pages);
        if (retainedBytes > maximumRetainedBytes
            || persistentPageCount > maximumPersistentPages)
        {
            _capacityBlocked = true;
            return count;
        }

        _pages = pages;
        _bakedCursor = bakedCursor;
        _dynamicCursor = dynamicCursor;
        _cursor += count;
        if (_cursor != _addressCount)
            return count;

        completed = true;
        _completed = true;
        capture = new NavigationGridBaselineCapture(
            _addressCount,
            _pages,
            _capturedChangeSequence,
            _worldSpawnToken,
            _gridIndex,
            _gridSpawnToken,
            _gridLastChangeSequence,
            _map.GridBinding.Key);
        _completedCapture = capture;
        return count;
    }

    private void ResetProgress()
    {
        _pages = PersistentIntMap<NavigationPhysicalPage>.Empty;
        _cursor = 0;
        _bakedCursor = 0;
        _dynamicCursor = 0;
        _capturedChangeSequence = 0;
        _worldSpawnToken = 0;
        _gridIndex = ushort.MaxValue;
        _gridSpawnToken = 0;
        _gridLastChangeSequence = 0;
        _hasIdentity = false;
        _completed = false;
        _capacityBlocked = false;
        _completedCapture = default;
        if (_discoversDefault)
        {
            _defaultCandidate = null;
            _defaultSeedSlots = PersistentVoxelIndexMap<NavigationDynamicCellSlot>.Empty;
            _defaultSeedIndexes = PersistentIntMap<VoxelIndex>.Empty;
            _defaultSeedCursor = 0;
            _omittedDefaultSeedSlotCount = 0;
            _omittedPhysicalDefaultSlotCount = 0;
            _hasNewDefaultSlot = false;
            _defaultPhysicalAddressSetChanged = false;
            _coveredGeneration = default;
            _coveredGenerationBound = false;
            _structuralChangedStates = NavigationSurfaceComponentKeySet.Empty;
        }
    }

    private int AdvanceDefaultDiscovery(
        GridWorld world,
        int maximumAddresses,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        Span<VoxelIndex> addressScratch,
        Span<GridCoveredAddress> coveredAddressScratch,
        out NavigationGridBaselineCapture capture,
        out bool completed)
    {
        capture = default;
        completed = false;
        int seedProbes = AdvanceDefaultSeed(
            maximumAddresses,
            maximumRetainedBytes,
            maximumPersistentPages);
        if (_capacityBlocked || seedProbes != 0 || _defaultCandidate == null)
            return seedProbes;
        GridCoveredAddressCursor cursor = _coveredCursor!;
        if (cursor.Status == GridCoveredAddressCursorStatus.Stale)
        {
            if (!world.TryCaptureNavigationBaseline(
                    _map.GridBinding.Key,
                    ReadOnlySpan<VoxelIndex>.Empty,
                    out GridNavigationBaseline? identity)
                || identity == null)
            {
                completed = true;
                _completed = true;
                capture = new NavigationGridBaselineCapture(
                    0,
                    baseline: null,
                    isDelta: false);
                _completedCapture = capture;
                return 0;
            }

            _capturedChangeSequence = identity.CapturedChangeSequence;
            _worldSpawnToken = identity.WorldSpawnToken;
            _gridIndex = identity.GridIndex;
            _gridSpawnToken = identity.GridSpawnToken;
            _gridLastChangeSequence = identity.GridLastChangeSequence;
            _hasIdentity = true;
            _coveredGeneration = new GridCoveredAddressGeneration(
                identity.ConfigurationKey,
                identity.GridIndex,
                identity.GridSpawnToken,
                identity.GridLastChangeSequence);
            bool began = world.TryBeginCoveredAddresses(
                cursor,
                identity.ConfigurationKey.BoundsMin,
                identity.ConfigurationKey.BoundsMax,
                eligibleGenerationCount: 1,
                identity.ConfigurationKey);
            System.Diagnostics.Debug.Assert(began,
                "The one-generation baseline cursor is constructed with exact capacity one.");
        }

        ReadOnlySpan<GridCoveredAddressGeneration> input = _coveredGenerationBound
            ? ReadOnlySpan<GridCoveredAddressGeneration>.Empty
            : MemoryMarshal.CreateReadOnlySpan(ref _coveredGeneration, 1);
        int outputLimit = Math.Min(
            maximumAddresses,
            Math.Min(addressScratch.Length, coveredAddressScratch.Length));
        GridCoveredAddressCursorStatus status = world.AdvanceCoveredAddresses(
            cursor,
            input,
            coveredAddressScratch,
            lookupProbeLimit: _coveredGenerationBound ? 0 : 1,
            addressProbeLimit: maximumAddresses,
            outputLimit,
            out _,
            out int addressProbes,
            out int inputsConsumed,
            out int outputCount);
        _coveredGenerationBound |= inputsConsumed != 0;
        bool invalidated = status == GridCoveredAddressCursorStatus.Stale;
        System.Diagnostics.Debug.Assert(!invalidated || outputCount == 0,
            "A stale covered-address cursor discards the run without emitting output.");

        for (int i = 0; i < outputCount; i++)
            addressScratch[i] = coveredAddressScratch[i].VoxelIndex;
        bool captured = false;
        GridNavigationBaseline? baseline = null;
        if (outputCount > 0)
        {
            captured = world.TryCaptureNavigationBaseline(
                    _map.GridBinding.Key,
                    addressScratch.Slice(0, outputCount),
                    out baseline);
        }
        return FinalizeCoveredAddressAdvance(
            status,
            addressProbes,
            outputCount,
            invalidated,
            captured,
            baseline,
            cursor.RunStamp.ChangeSequence,
            cursor.RunStamp.WorldSpawnToken,
            maximumRetainedBytes,
            maximumPersistentPages,
            out capture,
            out completed);
    }

    internal int FinalizeCoveredAddressAdvance(
        GridCoveredAddressCursorStatus status,
        int addressProbes,
        int outputCount,
        bool invalidated,
        bool captured,
        GridNavigationBaseline? baseline,
        ulong expectedCapturedChangeSequence,
        long expectedWorldSpawnToken,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        out NavigationGridBaselineCapture capture,
        out bool completed)
    {
        capture = default;
        completed = false;
        if (outputCount > 0)
        {
            invalidated |= !IsCoveredBaselineCurrent(
                captured,
                baseline,
                expectedCapturedChangeSequence,
                expectedWorldSpawnToken,
                _gridIndex,
                _gridSpawnToken,
                _gridLastChangeSequence);
            if (!invalidated)
            {
                NavigationMapInstance next = _defaultCandidate!.AppendDefaultBaselineStates(
                    _source,
                    baseline!.VoxelStates,
                    baseline.CapturedChangeSequence,
                    _pageVersion);
                for (int i = 0; i < baseline.VoxelStates.Length; i++)
                {
                    VoxelIndex index = baseline.VoxelStates[i].VoxelIndex;
                    bool sourcePresent = _source.IsPhysicallyPresent(index);
                    bool nextPresent = next.IsPhysicallyPresent(index);
                    _defaultPhysicalAddressSetChanged |= sourcePresent != nextPresent;
                    bool sourceHasDynamic = _source.TryGetDynamicSlot(
                        index,
                        out NavigationDynamicCellSlot sourceSlot);
                    bool nextHasDynamic = next.TryGetDynamicSlot(
                        index,
                        out NavigationDynamicCellSlot nextSlot);
                    bool rediscoveredOmittedSlot = sourceHasDynamic
                        && nextHasDynamic
                        && sourceSlot.Slot == nextSlot.Slot
                        && !_defaultSeedSlots.TryGetValue(index, out _);
                    if (rediscoveredOmittedSlot)
                    {
                        System.Diagnostics.Debug.Assert(_omittedDefaultSeedSlotCount > 0,
                            "Each unique rediscovered omitted seed owns one pending seed count.");
                        _omittedDefaultSeedSlotCount--;
                        UpdateStructuralStates(
                            index,
                            _source.GetEffectiveMedia(index),
                            add: false);
                    }
                    if (sourcePresent
                        && nextPresent
                        && rediscoveredOmittedSlot)
                    {
                        System.Diagnostics.Debug.Assert(_omittedPhysicalDefaultSlotCount > 0,
                            "Each physically present rediscovered omitted seed owns one pending physical count.");
                        _omittedPhysicalDefaultSlotCount--;
                    }
                    if (!sourceHasDynamic && nextHasDynamic)
                    {
                        _hasNewDefaultSlot = true;
                        UpdateStructuralStates(index, next.GetEffectiveMedia(index), add: true);
                    }
                }
                if (EstimateDefaultRetainedBytes(next) > maximumRetainedBytes
                    || EstimateDefaultPersistentPages(next) > maximumPersistentPages)
                {
                    _capacityBlocked = true;
                    return addressProbes;
                }
                _defaultCandidate = next;
                _cursor = checked(_cursor + outputCount);
                _capturedChangeSequence = baseline.CapturedChangeSequence;
            }
        }
        if (invalidated)
            return ResetAfterDefaultDiscoveryInvalidation(addressProbes);

        if (status != GridCoveredAddressCursorStatus.Complete)
            return addressProbes;

        NavigationMapInstance prepared = _defaultCandidate!.CompleteDefaultBaseline(
            _pageVersion,
            _omittedDefaultSeedSlotCount == 0 && !_hasNewDefaultSlot);
        completed = true;
        _completed = true;
        capture = new NavigationGridBaselineCapture(
            prepared,
            _structuralChangedStates,
            _defaultPhysicalAddressSetChanged || _omittedPhysicalDefaultSlotCount != 0,
            prepared.AddressCount,
            _capturedChangeSequence,
            _worldSpawnToken,
            _gridIndex,
            _gridSpawnToken,
            _gridLastChangeSequence,
            _map.GridBinding.Key);
        _completedCapture = capture;
        return addressProbes;
    }

    private int ResetAfterDefaultDiscoveryInvalidation(int addressProbes)
    {
        ResetProgress();
        return addressProbes;
    }

    private int AdvanceDefaultSeed(
        int maximumAddresses,
        long maximumRetainedBytes,
        int maximumPersistentPages)
    {
        if (_defaultCandidate != null)
            return 0;
        int remaining = _source.DynamicSlotCount - _defaultSeedCursor;
        int count = Math.Min(maximumAddresses, remaining);
        PersistentVoxelIndexMap<NavigationDynamicCellSlot> slots = _defaultSeedSlots;
        PersistentIntMap<VoxelIndex> indexes = _defaultSeedIndexes;
        int omittedSlotCount = _omittedDefaultSeedSlotCount;
        int omittedPhysicalSlotCount = _omittedPhysicalDefaultSlotCount;
        NavigationSurfaceComponentKeySet structuralChangedStates =
            _structuralChangedStates;
        for (int i = 0; i < count; i++)
        {
            _source.TryGetDefaultBaselineSeedSlot(
                _defaultSeedCursor + i,
                out NavigationDynamicCellSlot slot,
                out bool retain);
            if (retain)
            {
                slots = slots.Set(slot.Index, slot);
                indexes = indexes.Set(slot.Slot, slot.Index);
            }
            else
            {
                omittedSlotCount++;
                AddMediaStates(
                    _map.MapId,
                    slot.Index,
                    _source.GetEffectiveMedia(slot.Index),
                    ref structuralChangedStates);
                if (_source.IsPhysicallyPresent(slot.Index))
                    omittedPhysicalSlotCount++;
            }
        }
        bool seedComplete = _defaultSeedCursor + count == _source.DynamicSlotCount;
        NavigationMapInstance? candidate = seedComplete
            ? _source.CreateDefaultBaselineSeed(_pageVersion, slots, indexes)
            : null;
        long retainedBytes = candidate == null
            ? checked(
                320L
                + _coveredCursor!.RetainedBytes
                + slots.RetainedBytes
                + indexes.RetainedBytes
                + structuralChangedStates.RetainedBytes)
            : EstimateDefaultRetainedBytes(candidate);
        int retainedPages = candidate == null
            ? checked(
                3
                + slots.PersistentNodeCount
                + indexes.PersistentNodeCount
                + structuralChangedStates.PersistentPageCount)
            : EstimateDefaultPersistentPages(candidate);
        if (retainedBytes > maximumRetainedBytes
            || retainedPages > maximumPersistentPages)
        {
            _capacityBlocked = true;
            return count;
        }
        _defaultSeedSlots = slots;
        _defaultSeedIndexes = indexes;
        _defaultSeedCursor += count;
        _omittedDefaultSeedSlotCount = omittedSlotCount;
        _omittedPhysicalDefaultSlotCount = omittedPhysicalSlotCount;
        _structuralChangedStates = structuralChangedStates;
        _defaultCandidate = candidate;
        return count;
    }

    private void UpdateStructuralStates(VoxelIndex index, TraversalMedia media, bool add)
    {
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            TraversalMedia bit = (TraversalMedia)NavigationMediumSlots<byte>.GetBit(medium);
            if ((media & bit) == 0)
                continue;
            var key = new NavigationSurfaceComponentKey(
                new NavigationCellAddress(_map.MapId, index),
                medium);
            _structuralChangedStates = add
                ? _structuralChangedStates.Add(key)
                : _structuralChangedStates.Remove(key);
        }
    }

    private static void AddMediaStates(
        string mapId,
        VoxelIndex index,
        TraversalMedia media,
        ref NavigationSurfaceComponentKeySet states)
    {
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            TraversalMedia bit = (TraversalMedia)NavigationMediumSlots<byte>.GetBit(medium);
            if ((media & bit) != 0)
            {
                states = states.Add(new NavigationSurfaceComponentKey(
                    new NavigationCellAddress(mapId, index),
                    medium));
            }
        }
    }

    private long EstimateDefaultRetainedBytes(NavigationMapInstance candidate) => checked(
        320L
        + _coveredCursor!.RetainedBytes
        + candidate.DefaultBaselineRetainedBytes
        + _structuralChangedStates.RetainedBytes);

    private int EstimateDefaultPersistentPages(NavigationMapInstance candidate) => checked(
        1
        + candidate.DefaultBaselinePersistentPages
        + _structuralChangedStates.PersistentPageCount);

    private static long EstimateRetainedBytes(
        PersistentIntMap<NavigationPhysicalPage> pages) => checked(
            192L
            + pages.RetainedBytes
            + ((long)pages.Count * 320L));

    private static int EstimatePersistentPageCount(
        PersistentIntMap<NavigationPhysicalPage> pages) =>
            1 + (pages.PersistentNodeCount * 2);
}
