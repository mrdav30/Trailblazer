//=======================================================================
// NavigationBaselineRebuild.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Accumulates one immutable requested-address baseline under a finite frame budget.</summary>
internal sealed class NavigationBaselineRebuild
{
    private readonly NavigationMap _map;
    private readonly long _bakeVersion;
    private readonly long _overlayHighWater;
    private readonly long _dynamicSlotGeneration;
    private readonly int _dynamicSlotCount;
    private readonly int _addressCount;
    private readonly long _pageVersion;
    private PersistentIntMap<NavigationPhysicalPage> _pages =
        PersistentIntMap<NavigationPhysicalPage>.Empty;
    private int _cursor;
    private int _bakedCursor;
    private int _dynamicCursor;
    private ulong _highWaterSequence;
    private long _worldSpawnToken;
    private ushort _gridIndex = ushort.MaxValue;
    private long _gridSpawnToken;
    private ulong _gridHighWaterSequence;
    private bool _hasIdentity;
    private bool _completed;
    private bool _capacityBlocked;

    internal NavigationBaselineRebuild(NavigationMapInstance instance)
    {
        _map = instance.Map;
        _bakeVersion = instance.BakeVersion;
        _overlayHighWater = instance.Overlay.HighWaterSequence;
        _dynamicSlotGeneration = instance.DynamicSlotGeneration;
        _dynamicSlotCount = instance.DynamicSlotCount;
        _addressCount = instance.AddressCount;
        _pageVersion = instance.InstanceVersion;
    }

    internal string MapId => _map.MapId;

    internal long RetainedBytes => checked(
        192L
        + _pages.RetainedBytes
        + ((long)_pages.Count * 320L));

    internal int PersistentPageCount => 1 + (_pages.PersistentNodeCount * 2);

    internal bool IsComplete => _completed;

    internal bool IsCapacityBlocked => _capacityBlocked;

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

    internal bool Matches(NavigationMapInstance instance) =>
        ReferenceEquals(_map, instance.Map)
        && _bakeVersion == instance.BakeVersion
        && _overlayHighWater == instance.Overlay.HighWaterSequence
        && _dynamicSlotGeneration == instance.DynamicSlotGeneration
        && _dynamicSlotCount == instance.DynamicSlotCount;

    internal int Advance(
        GridWorld world,
        NavigationMapInstance instance,
        int maximumAddresses,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        Span<VoxelIndex> addressScratch,
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
            capture = new NavigationGridBaselineCapture(Array.Empty<VoxelIndex>(), baseline: null);
            return count;
        }

        if (_hasIdentity
            && (baseline.WorldSpawnToken != _worldSpawnToken
                || baseline.GridIndex != _gridIndex
                || baseline.GridSpawnToken != _gridSpawnToken
                || baseline.GridHighWaterSequence != _gridHighWaterSequence
                || !baseline.ConfigurationKey.Equals(_map.GridBinding.Key)))
        {
            ResetProgress();
            return count;
        }

        if (!_hasIdentity)
        {
            _highWaterSequence = baseline.HighWaterSequence;
            _worldSpawnToken = baseline.WorldSpawnToken;
            _gridIndex = baseline.GridIndex;
            _gridSpawnToken = baseline.GridSpawnToken;
            _gridHighWaterSequence = baseline.GridHighWaterSequence;
            _hasIdentity = true;
        }
        _highWaterSequence = baseline.HighWaterSequence;

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
            _highWaterSequence,
            _worldSpawnToken,
            _gridIndex,
            _gridSpawnToken,
            _gridHighWaterSequence,
            _map.GridBinding.Key);
        return count;
    }

    private void ResetProgress()
    {
        _pages = PersistentIntMap<NavigationPhysicalPage>.Empty;
        _cursor = 0;
        _bakedCursor = 0;
        _dynamicCursor = 0;
        _highWaterSequence = 0;
        _worldSpawnToken = 0;
        _gridIndex = ushort.MaxValue;
        _gridSpawnToken = 0;
        _gridHighWaterSequence = 0;
        _hasIdentity = false;
        _completed = false;
        _capacityBlocked = false;
    }

    private static long EstimateRetainedBytes(
        PersistentIntMap<NavigationPhysicalPage> pages) => checked(
            192L
            + pages.RetainedBytes
            + ((long)pages.Count * 320L));

    private static int EstimatePersistentPageCount(
        PersistentIntMap<NavigationPhysicalPage> pages) =>
            1 + (pages.PersistentNodeCount * 2);
}
