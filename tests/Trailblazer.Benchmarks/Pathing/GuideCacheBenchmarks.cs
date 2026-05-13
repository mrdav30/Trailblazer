using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the guide cache lifecycle: hit, miss, eviction under the 128-entry capacity,
/// invalidation, stale-cull, and force/soft flush.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "Cache")]
public class GuideCacheBenchmarks
{
    // Capacity threshold from ReusableSurveyResultCache — 128 entries before LRU eviction.
    private const int CacheCapacity = 128;
    public const int MixedCacheEntriesPerFamily = CacheCapacity;
    public const int MixedActiveEntriesPerFamily = MixedCacheEntriesPerFamily / MixedActiveStride;

    private const int MixedActiveStride = 4;
    private const string MixedSolidChartKey = "MixedCacheSolid";
    private const string MixedVolumeChartKey = "MixedCacheVolume";
    private const string MixedHybridSourceChartKey = "MixedCacheHybridSource";
    private const string MixedHybridDestinationChartKey = "MixedCacheHybridDestination";
    private const string MixedNoMatchChartKey = "MixedCacheChartThatDoesNotExist";
    private const int MixedAStarCacheKeyBase = 100_000;
    private const int MixedFlowFieldCacheKeyBase = 200_000;
    private const int MixedVolumeCacheKeyBase = 300_000;
    private const int MixedHybridCacheKeyBase = 400_000;

    private static readonly string[] MixedSolidChartKeys = { MixedSolidChartKey };
    private static readonly string[] MixedVolumeChartKeys = { MixedVolumeChartKey };
    private static readonly string[] MixedHybridChartKeys =
    {
        MixedHybridSourceChartKey,
        MixedHybridDestinationChartKey
    };

    // -------------------------------------------------------------------------
    // Shared A* fixture for hit / miss / eviction / invalidation / cull
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _fixture;

    // Pre-created requests used for warm hit benchmarks.
    private AStarPathRequest _hitRequest;

    // Requests for the below-capacity miss scenario (one fresh key each iteration).
    private AStarPathRequest[] _belowCapacityRequests;

    // Requests for the over-capacity scenario (129 unique keys to force eviction).
    private AStarPathRequest[] _overCapacityRequests;

    // Tracking index for rotating through unique keys in iteration setup.
    private int _iterationIndex;

    // -------------------------------------------------------------------------
    // Cull scenario — stale entries
    // -------------------------------------------------------------------------

    private const int StaleFrameBase = 10_000; // Frame offset that makes entries immediately stale.

    // -------------------------------------------------------------------------
    // Flow-field request for flow-field cache hit
    // -------------------------------------------------------------------------

    private FlowFieldPathRequest _ffHitRequest;

    // -------------------------------------------------------------------------
    // Mixed pressure scenario: all cache families populated in one aggregate pressure set.
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        SetupCacheFixture();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupCacheFixture()
    {
        // Use a 32x32 open plane — sufficient for up to 32*32-1 = 1023 unique (start, dest) pairs.
        const int size = 32;

        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(224, 8, 84)));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("CacheTestOpenPlane", size);

        BenchmarkPreflight.AssertAStarRouteExists(_fixture.Context, origin, destination, Fixed64.One);
        BenchmarkPreflight.AssertFlowFieldRouteExists(_fixture.Context, origin, destination, Fixed64.One);
        _fixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);

        // Single warm-hit request.
        _hitRequest = AStarPathRequest.Create(_fixture.Context, origin, destination, Fixed64.One);

        // Unique adjacent pairs for cache-miss and eviction scenarios. Keeping each route to
        // roughly the same length isolates cache behavior from A* search-length differences.
        Vector3d[] cacheStarts = new Vector3d[CacheCapacity + 1];
        Vector3d[] cacheDestinations = new Vector3d[CacheCapacity + 1];
        BenchmarkChartFactory.GenerateAdjacentRequestPairs(
            size,
            CacheCapacity + 1,
            cacheStarts,
            cacheDestinations);

        _belowCapacityRequests = new AStarPathRequest[CacheCapacity];
        _overCapacityRequests = new AStarPathRequest[CacheCapacity + 1];

        for (int i = 0; i < CacheCapacity + 1; i++)
        {
            AStarPathRequest req = AStarPathRequest.Create(_fixture.Context,
                cacheStarts[i],
                cacheDestinations[i],
                Fixed64.One);
            if (req == null)
                throw new System.InvalidOperationException(
                    $"Preflight: Could not create A* request from cache-pressure pair {cacheStarts[i]} -> {cacheDestinations[i]}.");
            _overCapacityRequests[i] = req;

            if (i < CacheCapacity)
                _belowCapacityRequests[i] = req;
        }

        _ffHitRequest = FlowFieldPathRequest.Create(_fixture.Context, origin, destination, Fixed64.One);
        if (_ffHitRequest == null)
            throw new System.InvalidOperationException(
                $"Preflight: Could not create flow-field request from {origin} -> {destination}.");

        ValidateMixedCachePressureSeedPlan();
    }

    private static void ValidateMixedCachePressureSeedPlan()
    {
        if (MixedCacheEntriesPerFamily != CacheCapacity)
            throw new System.InvalidOperationException("Preflight: mixed cache pressure must exercise full per-family capacity.");
    }

    // -------------------------------------------------------------------------
    // Iteration setup
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(AStarCacheMiss_BelowCapacity),
        nameof(FlowFieldCacheMiss_BelowCapacity)
    })]
    public void FlushForColdRun()
    {
        _fixture.FlushGuideCache();
        _iterationIndex = 0;
    }

    [IterationSetup(Targets = new[] { nameof(AStarCacheMiss_OverCapacity_Eviction) })]
    public void SeedCacheForEviction()
    {
        _fixture.FlushGuideCache();

        for (int i = 0; i < CacheCapacity; i++)
        {
            if (PathGuideFactory.RequestGuide(_overCapacityRequests[i], out AStarGuide guide))
                PathGuideFactory.ReturnGuide(guide);
        }
    }

    [IterationSetup(Targets = new[]
    {
        nameof(CullExpiredGuides_ManyStale),
        nameof(CullExpiredGuides_NoStale)
    })]
    public void SeedCacheForCull()
    {
        _fixture.FlushGuideCache();

        // Seed the cache with CacheCapacity entries so there is something to cull.
        for (int i = 0; i < CacheCapacity; i++)
        {
            if (PathGuideFactory.RequestGuide(_belowCapacityRequests[i], out AStarGuide guide))
                PathGuideFactory.ReturnGuide(guide);
        }

        _iterationIndex = 0;
    }

    [IterationSetup(Targets = new[]
    {
        nameof(InvalidateCacheFor_MatchingChart),
        nameof(InvalidateCacheFor_NoMatchingChart)
    })]
    public void SeedCacheForInvalidation()
    {
        _fixture.FlushGuideCache();

        // Seed to capacity so no-match invalidation measures the reverse index rather than a one-entry happy path.
        for (int i = 0; i < CacheCapacity; i++)
        {
            if (PathGuideFactory.RequestGuide(_belowCapacityRequests[i], out AStarGuide guide))
                PathGuideFactory.ReturnGuide(guide);
        }
    }

    [IterationSetup(Targets = new[]
    {
        nameof(InvalidateMixedCacheFor_NoMatchingChart),
        nameof(InvalidateMixedCacheFor_MatchingSolidChart),
        nameof(InvalidateMixedCacheFor_MatchingVolumeChart),
        nameof(InvalidateMixedCacheFor_MatchingHybridChart)
    })]
    public void SeedMixedCacheForInvalidation()
    {
        SeedMixedCachePressure(keepActiveRatio: false);
    }

    [IterationSetup(Targets = new[] { nameof(CullMixedCache_NoStale) })]
    public void SeedMixedCacheForCull()
    {
        SeedMixedCachePressure(keepActiveRatio: false);
    }

    [IterationSetup(Targets = new[] { nameof(CullMixedCache_StaleWithActiveQuarter) })]
    public void SeedMixedCacheForCullWithActiveQuarter()
    {
        SeedMixedCachePressure(keepActiveRatio: true);
    }

    [IterationCleanup(Targets = new[] { nameof(CullMixedCache_StaleWithActiveQuarter) })]
    public void ClearMixedCachePressure()
    {
        _fixture.FlushGuideCache();
    }

    // -------------------------------------------------------------------------
    // A* cache hit
    // -------------------------------------------------------------------------

    /// <summary>A* warm guide request — exact cache hit for the same request key.</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pathing", "Cache", "AStar", "Hit")]
    public bool AStarCacheHit()
    {
        bool ok = PathGuideFactory.RequestGuide(_hitRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // A* cache miss
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cache miss below the 128-entry capacity — each iteration rotates through unique keys
    /// against an empty cache.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "AStar", "Miss")]
    public bool AStarCacheMiss_BelowCapacity()
    {
        int i = _iterationIndex % CacheCapacity;
        _iterationIndex++;

        bool ok = PathGuideFactory.RequestGuide(_belowCapacityRequests[i], out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>
    /// Cache miss over the 128-entry capacity — 129 unique keys force LRU eviction.
    /// Iteration setup fills the cache; the measured method adds one entry to isolate eviction cost.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "AStar", "Miss", "Eviction")]
    public bool AStarCacheMiss_OverCapacity_Eviction()
    {
        bool ok = PathGuideFactory.RequestGuide(_overCapacityRequests[CacheCapacity], out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Flow-field cache hit / miss
    // -------------------------------------------------------------------------

    /// <summary>Flow-field warm guide request — cache hit.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "FlowField", "Hit")]
    public bool FlowFieldCacheHit()
    {
        bool ok = PathGuideFactory.RequestGuide(_ffHitRequest, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Flow-field cold guide request — cache miss (cache is empty before each iteration).</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "FlowField", "Miss")]
    public bool FlowFieldCacheMiss_BelowCapacity()
    {
        bool ok = PathGuideFactory.RequestGuide(_ffHitRequest, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // InvalidateCacheFor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Invalidate cache entries for a chart key that matches a cached result.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation")]
    public void InvalidateCacheFor_MatchingChart()
    {
        PathGuideFactory.InvalidateCacheFor("CacheTestOpenPlane");
    }

    /// <summary>
    /// Invalidate cache entries for a chart key that does not match any cached result.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation")]
    public void InvalidateCacheFor_NoMatchingChart()
    {
        PathGuideFactory.InvalidateCacheFor("ChartKeyThatDoesNotExist");
    }

    /// <summary>
    /// No-match invalidation against mixed A*, flow-field, volume, and hybrid caches.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation", "Mixed")]
    public int InvalidateMixedCacheFor_NoMatchingChart()
    {
        return MeasureInvalidateMixedCacheFor_NoMatchingChart().EntriesRemoved;
    }

    public CacheInvalidationCardinality MeasureInvalidateMixedCacheFor_NoMatchingChart()
    {
        return InvalidateMixedCacheFor(MixedNoMatchChartKey);
    }

    /// <summary>
    /// Invalidates the shared solid chart used by the mixed A* and flow-field cache entries.
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation", "Mixed")]
    public int InvalidateMixedCacheFor_MatchingSolidChart()
    {
        return MeasureInvalidateMixedCacheFor_MatchingSolidChart().EntriesRemoved;
    }

    public CacheInvalidationCardinality MeasureInvalidateMixedCacheFor_MatchingSolidChart()
    {
        return InvalidateMixedCacheFor(MixedSolidChartKey);
    }

    /// <summary>
    /// Invalidates the shared gas-volume chart used by the mixed volume cache entries.
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation", "Mixed")]
    public int InvalidateMixedCacheFor_MatchingVolumeChart()
    {
        return MeasureInvalidateMixedCacheFor_MatchingVolumeChart().EntriesRemoved;
    }

    public CacheInvalidationCardinality MeasureInvalidateMixedCacheFor_MatchingVolumeChart()
    {
        return InvalidateMixedCacheFor(MixedVolumeChartKey);
    }

    /// <summary>
    /// Invalidates the shared destination chart used by cached hybrid transition route plans.
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Cache", "Invalidation", "Mixed")]
    public int InvalidateMixedCacheFor_MatchingHybridChart()
    {
        return MeasureInvalidateMixedCacheFor_MatchingHybridChart().EntriesRemoved;
    }

    public CacheInvalidationCardinality MeasureInvalidateMixedCacheFor_MatchingHybridChart()
    {
        return InvalidateMixedCacheFor(MixedHybridDestinationChartKey);
    }

    // -------------------------------------------------------------------------
    // CullExpiredGuides
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cull expired guides when all entries are fresh (frame 0 — nothing stale).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Cull")]
    public void CullExpiredGuides_NoStale()
    {
        PathGuideFactory.CullExpiredGuides(currentFrame: 0);
    }

    /// <summary>
    /// Cull expired guides when all 128 entries are stale (far-future frame).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Cull")]
    public void CullExpiredGuides_ManyStale()
    {
        PathGuideFactory.CullExpiredGuides(currentFrame: StaleFrameBase);
    }

    /// <summary>
    /// Mixed-cache cull when all entries are fresh.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Cull", "Mixed")]
    public int CullMixedCache_NoStale()
    {
        return MeasureCullMixedCache_NoStale().EntriesRemoved;
    }

    public CacheCullCardinality MeasureCullMixedCache_NoStale()
    {
        return CullMixedCache(currentFrame: 0);
    }

    /// <summary>
    /// Mixed-cache cull with stale returned entries and one active guide per four A*/flow/volume entries.
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Cache", "Cull", "Mixed")]
    public int CullMixedCache_StaleWithActiveQuarter()
    {
        return MeasureCullMixedCache_StaleWithActiveQuarter().EntriesRemoved;
    }

    public CacheCullCardinality MeasureCullMixedCache_StaleWithActiveQuarter()
    {
        return CullMixedCache(currentFrame: StaleFrameBase);
    }

    // -------------------------------------------------------------------------
    // FlushCache
    // -------------------------------------------------------------------------

    /// <summary>Soft flush — leaves guides checked out in the cache.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Flush")]
    public void FlushCache_Soft()
    {
        PathGuideFactory.FlushCache(force: false);
    }

    /// <summary>Force flush — removes all entries regardless of checkout state.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "Flush")]
    public void FlushCache_Force()
    {
        PathGuideFactory.FlushCache(force: true);
    }

    private void SeedMixedCachePressure(bool keepActiveRatio)
    {
        ClearMixedCachePressure();
        _fixture.FlushGuideCache();

        for (int i = 0; i < MixedCacheEntriesPerFamily; i++)
        {
            SeedAStarMixedEntry(i, keepActiveRatio);
            SeedFlowFieldMixedEntry(i, keepActiveRatio);
            SeedVolumeMixedEntry(i, keepActiveRatio);
            SeedHybridMixedEntry(i);
        }

        EnsureMixedCacheSeeded(keepActiveRatio);
    }

    private static void SeedAStarMixedEntry(int index, bool keepActiveRatio)
    {
        bool checkout = keepActiveRatio && ShouldKeepActive(index);
        if (!PathGuideFactory.TrySeedAStarCacheForBenchmark(
            MixedAStarCacheKeyBase + index,
            MixedSolidChartKeys,
            checkout))
        {
            throw new System.InvalidOperationException($"Preflight: mixed A* cache seed {index} failed.");
        }
    }

    private static void SeedFlowFieldMixedEntry(int index, bool keepActiveRatio)
    {
        bool checkout = keepActiveRatio && ShouldKeepActive(index);
        if (!PathGuideFactory.TrySeedFlowFieldCacheForBenchmark(
            MixedFlowFieldCacheKeyBase + index,
            MixedSolidChartKeys,
            checkout))
        {
            throw new System.InvalidOperationException($"Preflight: mixed flow-field cache seed {index} failed.");
        }
    }

    private static void SeedVolumeMixedEntry(int index, bool keepActiveRatio)
    {
        bool checkout = keepActiveRatio && ShouldKeepActive(index);
        if (!PathGuideFactory.TrySeedVolumeCacheForBenchmark(
            MixedVolumeCacheKeyBase + index,
            MixedVolumeChartKeys,
            checkout))
        {
            throw new System.InvalidOperationException($"Preflight: mixed volume cache seed {index} failed.");
        }
    }

    private static void SeedHybridMixedEntry(int index)
    {
        if (!PathGuideFactory.TrySeedHybridRoutePlanCacheForBenchmark(
            MixedHybridCacheKeyBase + index,
            MixedHybridChartKeys,
            checkout: false))
        {
            throw new System.InvalidOperationException($"Preflight: mixed hybrid route-plan cache seed {index} failed.");
        }
    }

    private static bool ShouldKeepActive(int index)
    {
        return index % MixedActiveStride == 0;
    }

    private static void EnsureMixedCacheSeeded(bool keepActiveRatio)
    {
        if (PathGuideFactory.TotalAStarGuideCount != MixedCacheEntriesPerFamily
            || PathGuideFactory.TotalFlowGuideCount != MixedCacheEntriesPerFamily
            || PathGuideFactory.TotalVolumeGuideCount != MixedCacheEntriesPerFamily
            || PathGuideFactory.TotalHybridRoutePlanCount != MixedCacheEntriesPerFamily)
        {
            throw new System.InvalidOperationException(
                "Preflight: mixed cache pressure seed did not populate every cache family to target cardinality. " +
                $"A*={PathGuideFactory.TotalAStarGuideCount}, " +
                $"FlowField={PathGuideFactory.TotalFlowGuideCount}, " +
                $"Volume={PathGuideFactory.TotalVolumeGuideCount}, " +
                $"Hybrid={PathGuideFactory.TotalHybridRoutePlanCount}.");
        }

        int expectedActiveEntries = keepActiveRatio
            ? MixedActiveEntriesPerFamily * 3
            : 0;
        int activeEntries = PathGuideFactory.InUseAStarGuideCount
            + PathGuideFactory.InUseFlowGuideCount
            + PathGuideFactory.InUseVolumeGuideCount
            + PathGuideFactory.InUseHybridRoutePlanCount;

        if (activeEntries != expectedActiveEntries)
        {
            throw new System.InvalidOperationException(
                "Preflight: mixed cache pressure active count did not match target cardinality. " +
                $"Expected={expectedActiveEntries}, Actual={activeEntries}.");
        }
    }

    private static CacheInvalidationCardinality InvalidateMixedCacheFor(
        string chartKey)
    {
        int indexedEntries = PathGuideFactory.CountIndexedCacheEntriesForBenchmark(chartKey);
        CacheCounts before = CacheCounts.Capture();
        PathGuideFactory.InvalidateCacheFor(chartKey);
        CacheCounts after = CacheCounts.Capture();

        return new CacheInvalidationCardinality(
            entriesScanned: indexedEntries,
            entriesMatched: indexedEntries,
            entriesRemoved: before.TotalEntries - after.TotalEntries);
    }

    private static CacheCullCardinality CullMixedCache(int currentFrame)
    {
        CacheCounts before = CacheCounts.Capture();
        PathGuideFactory.CullExpiredGuides(currentFrame);
        CacheCounts after = CacheCounts.Capture();

        return new CacheCullCardinality(
            entriesBefore: before.TotalEntries,
            entriesAfter: after.TotalEntries,
            entriesRemoved: before.TotalEntries - after.TotalEntries,
            activeEntriesRemaining: after.ActiveEntries);
    }

}

public readonly struct CacheInvalidationCardinality
{
    public CacheInvalidationCardinality(int entriesScanned, int entriesMatched, int entriesRemoved)
    {
        EntriesScanned = entriesScanned;
        EntriesMatched = entriesMatched;
        EntriesRemoved = entriesRemoved;
    }

    public int EntriesScanned { get; }

    public int EntriesMatched { get; }

    public int EntriesRemoved { get; }
}

public readonly struct CacheCullCardinality
{
    public CacheCullCardinality(
        int entriesBefore,
        int entriesAfter,
        int entriesRemoved,
        int activeEntriesRemaining)
    {
        EntriesBefore = entriesBefore;
        EntriesAfter = entriesAfter;
        EntriesRemoved = entriesRemoved;
        ActiveEntriesRemaining = activeEntriesRemaining;
    }

    public int EntriesBefore { get; }

    public int EntriesAfter { get; }

    public int EntriesRemoved { get; }

    public int ActiveEntriesRemaining { get; }
}

internal readonly struct CacheCounts
{
    private CacheCounts(int totalEntries, int activeEntries)
    {
        TotalEntries = totalEntries;
        ActiveEntries = activeEntries;
    }

    public int TotalEntries { get; }

    public int ActiveEntries { get; }

    public static CacheCounts Capture()
    {
        return new CacheCounts(
            PathGuideFactory.TotalAStarGuideCount
                + PathGuideFactory.TotalFlowGuideCount
                + PathGuideFactory.TotalVolumeGuideCount
                + PathGuideFactory.TotalHybridRoutePlanCount,
            PathGuideFactory.InUseAStarGuideCount
                + PathGuideFactory.InUseFlowGuideCount
                + PathGuideFactory.InUseVolumeGuideCount
                + PathGuideFactory.InUseHybridRoutePlanCount);
    }
}
