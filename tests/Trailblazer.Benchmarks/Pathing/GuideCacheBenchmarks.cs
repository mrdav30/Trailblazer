using BenchmarkDotNet.Attributes;
using FixedMathSharp;
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
    // Flow-field fixture for flow-field cache hit
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _ffFixture;
    private FlowFieldPathRequest _ffHitRequest;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        SetupAStarCacheFixture();
        SetupFlowFieldCacheFixture();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
        _ffFixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupAStarCacheFixture()
    {
        // Use a 32x32 open plane — sufficient for up to 32*32-1 = 1023 unique (start, dest) pairs.
        const int size = 32;

        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForSquare(size));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("CacheTestOpenPlane", size);

        BenchmarkPreflight.AssertAStarRouteExists(origin, destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        // Single warm-hit request.
        _hitRequest = AStarPathRequest.Create(origin, destination, Fixed64.One);

        // Unique start positions for cache-miss and eviction scenarios.
        // We need 129+ positions so: use spread-out positions across the plane.
        Vector3d[] uniqueStarts = BenchmarkChartFactory.GenerateUniqueStartPositions(
            size, CacheCapacity + 1, out Vector3d cacheDest);

        _belowCapacityRequests = new AStarPathRequest[CacheCapacity];
        _overCapacityRequests = new AStarPathRequest[CacheCapacity + 1];

        for (int i = 0; i < CacheCapacity + 1; i++)
        {
            AStarPathRequest req = AStarPathRequest.Create(uniqueStarts[i], cacheDest, Fixed64.One);
            if (req == null)
                throw new System.InvalidOperationException(
                    $"Preflight: Could not create A* request from cache-pressure start {uniqueStarts[i]}.");
            _overCapacityRequests[i] = req;

            if (i < CacheCapacity)
                _belowCapacityRequests[i] = req;
        }
    }

    private void SetupFlowFieldCacheFixture()
    {
        const int size = 32;

        _ffFixture = new BenchmarkPathFixture();
        _ffFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(size));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("CacheTestFF32", size);

        BenchmarkPreflight.AssertFlowFieldRouteExists(origin, destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _ffHitRequest = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
    }

    // -------------------------------------------------------------------------
    // Iteration setup
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(AStarCacheMiss_BelowCapacity),
        nameof(AStarCacheMiss_OverCapacity_Eviction),
        nameof(FlowFieldCacheMiss_BelowCapacity)
    })]
    public void FlushForColdRun()
    {
        BenchmarkPathFixture.FlushGuideCache();
        _iterationIndex = 0;
    }

    [IterationSetup(Targets = new[]
    {
        nameof(CullExpiredGuides_ManyStale),
        nameof(CullExpiredGuides_NoStale)
    })]
    public void SeedCacheForCull()
    {
        BenchmarkPathFixture.FlushGuideCache();

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
        BenchmarkPathFixture.FlushGuideCache();

        // Put one warm entry in the cache.
        if (PathGuideFactory.RequestGuide(_hitRequest, out AStarGuide guide))
            PathGuideFactory.ReturnGuide(guide);
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
    /// Each benchmark method call fills the cache to capacity then adds one more entry.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Cache", "AStar", "Miss", "Eviction")]
    public int AStarCacheMiss_OverCapacity_Eviction()
    {
        int resolved = 0;

        // Fill to capacity.
        for (int i = 0; i < CacheCapacity; i++)
        {
            if (PathGuideFactory.RequestGuide(_overCapacityRequests[i], out AStarGuide guide))
            {
                PathGuideFactory.ReturnGuide(guide);
                resolved++;
            }
        }

        // This 129th entry triggers LRU eviction of the oldest returned entry.
        if (PathGuideFactory.RequestGuide(_overCapacityRequests[CacheCapacity], out AStarGuide evictionGuide))
        {
            PathGuideFactory.ReturnGuide(evictionGuide);
            resolved++;
        }

        return resolved;
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
}
