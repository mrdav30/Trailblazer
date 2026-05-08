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
    public const int MixedCacheEntriesPerFamily = CacheCapacity / 4;
    public const int MixedActiveEntriesPerFamily = MixedCacheEntriesPerFamily / MixedActiveStride;

    private const int MixedActiveStride = 4;
    private const int MixedSolidSize = 8;
    private const int MixedVolumeSize = 8;
    private const int MixedHybridLength = MixedCacheEntriesPerFamily + 1;
    private const string MixedSolidChartKey = "MixedCacheSolid";
    private const string MixedVolumeChartKey = "MixedCacheVolume";
    private const string MixedHybridSourceChartKey = "MixedCacheHybridSource";
    private const string MixedHybridDestinationChartKey = "MixedCacheHybridDestination";
    private const string MixedNoMatchChartKey = "MixedCacheChartThatDoesNotExist";

    private static readonly Vector3d MixedSolidOffset = new(40, 0, 0);
    private static readonly Vector3d MixedVolumeOffset = new(40, 1, 40);
    private static readonly Vector3d MixedHybridSourceOffset = new(90, 0, 0);
    private static readonly Vector3d MixedHybridDestinationOffset = new(90, 0, 4);

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

    private AStarPathRequest[] _mixedAStarRequests;
    private FlowFieldPathRequest[] _mixedFlowFieldRequests;
    private VolumePathRequest[] _mixedVolumeRequests;
    private FlowFieldPathRequest[] _mixedHybridRequests;
    private AStarGuide[] _mixedActiveAStarGuides;
    private FlowFieldGuide[] _mixedActiveFlowFieldGuides;
    private VolumeGuide[] _mixedActiveVolumeGuides;

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

        BenchmarkPreflight.AssertAStarRouteExists(origin, destination, Fixed64.One);
        BenchmarkPreflight.AssertFlowFieldRouteExists(origin, destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        // Single warm-hit request.
        _hitRequest = AStarPathRequest.Create(origin, destination, Fixed64.One);

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
            AStarPathRequest req = AStarPathRequest.Create(
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

        _ffHitRequest = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
        if (_ffHitRequest == null)
            throw new System.InvalidOperationException(
                $"Preflight: Could not create flow-field request from {origin} -> {destination}.");

        SetupMixedCachePressureRequests();
    }

    private void SetupMixedCachePressureRequests()
    {
        SetupMixedSolidRequests();
        SetupMixedVolumeRequests();
        SetupMixedHybridRequests();

        _mixedActiveAStarGuides = new AStarGuide[MixedActiveEntriesPerFamily];
        _mixedActiveFlowFieldGuides = new FlowFieldGuide[MixedActiveEntriesPerFamily];
        _mixedActiveVolumeGuides = new VolumeGuide[MixedActiveEntriesPerFamily];
    }

    private void SetupMixedSolidRequests()
    {
        BenchmarkChartFactory.RegisterOpenPlane(MixedSolidChartKey, MixedSolidSize, MixedSolidOffset);

        int adjacentPairCount = MixedSolidSize * (MixedSolidSize - 1);
        Vector3d[] aStarStarts = new Vector3d[adjacentPairCount];
        Vector3d[] aStarDestinations = new Vector3d[adjacentPairCount];
        BenchmarkChartFactory.GenerateAdjacentRequestPairs(
            MixedSolidSize,
            adjacentPairCount,
            aStarStarts,
            aStarDestinations,
            MixedSolidOffset);

        _mixedAStarRequests = new AStarPathRequest[MixedCacheEntriesPerFamily];
        int aStarCount = 0;
        for (int i = 0; i < adjacentPairCount && aStarCount < MixedCacheEntriesPerFamily; i++)
        {
            AStarPathRequest request = AStarPathRequest.Create(aStarStarts[i], aStarDestinations[i], Fixed64.One)
                ?? throw new System.InvalidOperationException(
                    $"Preflight: Could not create mixed A* request {i}.");

            if (ContainsRequestKey(_mixedAStarRequests, aStarCount, request.RequestCacheKey))
                continue;

            _mixedAStarRequests[aStarCount++] = request;
        }

        if (aStarCount != MixedCacheEntriesPerFamily)
            throw new System.InvalidOperationException("Preflight: Could not create enough unique mixed A* request keys.");

        int flowCandidateCount = (MixedSolidSize * MixedSolidSize) - 1;
        Vector3d[] flowDestinations = BenchmarkChartFactory.GenerateUniqueStartPositions(
            MixedSolidSize,
            flowCandidateCount,
            out Vector3d flowStart,
            MixedSolidOffset);

        _mixedFlowFieldRequests = new FlowFieldPathRequest[MixedCacheEntriesPerFamily];
        int flowFieldCount = 0;
        for (int i = 0; i < flowCandidateCount && flowFieldCount < MixedCacheEntriesPerFamily; i++)
        {
            FlowFieldPathRequest request = FlowFieldPathRequest.Create(flowStart, flowDestinations[i], Fixed64.One)
                ?? throw new System.InvalidOperationException(
                    $"Preflight: Could not create mixed flow-field request {i}.");

            if (ContainsRequestKey(_mixedFlowFieldRequests, flowFieldCount, request.RequestCacheKey))
                continue;

            _mixedFlowFieldRequests[flowFieldCount++] = request;
        }

        if (flowFieldCount != MixedCacheEntriesPerFamily)
            throw new System.InvalidOperationException("Preflight: Could not create enough unique mixed flow-field request keys.");
    }

    private void SetupMixedVolumeRequests()
    {
        RegisterGasPlane(MixedVolumeChartKey, MixedVolumeOffset, MixedVolumeSize);

        int adjacentPairCount = MixedVolumeSize * (MixedVolumeSize - 1);
        Vector3d[] volumeStarts = new Vector3d[adjacentPairCount];
        Vector3d[] volumeDestinations = new Vector3d[adjacentPairCount];
        BenchmarkChartFactory.GenerateAdjacentRequestPairs(
            MixedVolumeSize,
            adjacentPairCount,
            volumeStarts,
            volumeDestinations,
            MixedVolumeOffset);

        _mixedVolumeRequests = new VolumePathRequest[MixedCacheEntriesPerFamily];
        int volumeCount = 0;
        for (int i = 0; i < adjacentPairCount && volumeCount < MixedCacheEntriesPerFamily; i++)
        {
            VolumePathRequest request = VolumePathRequest.Create(
                    volumeStarts[i],
                    volumeDestinations[i],
                    Fixed64.One,
                    medium: TraversalMedium.Gas)
                ?? throw new System.InvalidOperationException(
                    $"Preflight: Could not create mixed volume request {i}.");

            if (ContainsRequestKey(_mixedVolumeRequests, volumeCount, request.RequestCacheKey))
                continue;

            _mixedVolumeRequests[volumeCount++] = request;
        }

        if (volumeCount != MixedCacheEntriesPerFamily)
            throw new System.InvalidOperationException("Preflight: Could not create enough unique mixed volume request keys.");
    }

    private void SetupMixedHybridRequests()
    {
        RegisterSolidCorridor(MixedHybridSourceChartKey, MixedHybridSourceOffset, MixedHybridLength);
        RegisterSolidPlane(MixedHybridDestinationChartKey, MixedHybridDestinationOffset, MixedHybridLength, depth: 2);

        _mixedHybridRequests = new FlowFieldPathRequest[MixedCacheEntriesPerFamily];
        for (int i = 0; i < MixedCacheEntriesPerFamily; i++)
        {
            Vector3d source = MixedHybridSourceOffset + new Vector3d(i, 0, 0);
            Vector3d sourceAnchor = MixedHybridSourceOffset + new Vector3d(i + 1, 0, 0);
            Vector3d destinationAnchor = MixedHybridDestinationOffset + new Vector3d(i, 0, 0);
            Vector3d destination = MixedHybridDestinationOffset + new Vector3d(i, 0, 1);

            TraversalTransitionRegistry.Register(new TraversalTransition(
                id: $"mixed-cache-jump-{i}",
                type: TraversalTransitionType.Jump,
                source: TraversalTransitionAnchor.Solid(sourceAnchor),
                destination: TraversalTransitionAnchor.Solid(destinationAnchor),
                pathCostModifier: 1));

            _mixedHybridRequests[i] = FlowFieldPathRequest.Create(
                    source,
                    destination,
                    Fixed64.One,
                    allowTraversalTransitions: true)
                ?? throw new System.InvalidOperationException(
                    $"Preflight: Could not create mixed hybrid flow-field request {i}.");

            if (ContainsRequestKey(_mixedHybridRequests, i, _mixedHybridRequests[i].RequestCacheKey))
                throw new System.InvalidOperationException(
                    $"Preflight: Mixed hybrid request {i} produced a duplicate request key.");
        }
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
        BenchmarkPathFixture.FlushGuideCache();
        _iterationIndex = 0;
    }

    [IterationSetup(Targets = new[] { nameof(AStarCacheMiss_OverCapacity_Eviction) })]
    public void SeedCacheForEviction()
    {
        BenchmarkPathFixture.FlushGuideCache();

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
    public void ReturnMixedActiveGuides()
    {
        ReturnGuides(_mixedActiveAStarGuides);
        ReturnGuides(_mixedActiveFlowFieldGuides);
        ReturnGuides(_mixedActiveVolumeGuides);
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
        return InvalidateMixedCacheFor(MixedNoMatchChartKey, expectedIndexedEntries: 0);
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
        return InvalidateMixedCacheFor(
            MixedSolidChartKey,
            expectedIndexedEntries: MixedCacheEntriesPerFamily * 2);
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
        return InvalidateMixedCacheFor(
            MixedVolumeChartKey,
            expectedIndexedEntries: MixedCacheEntriesPerFamily);
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
        return InvalidateMixedCacheFor(
            MixedHybridDestinationChartKey,
            expectedIndexedEntries: MixedCacheEntriesPerFamily);
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
        ReturnMixedActiveGuides();
        BenchmarkPathFixture.FlushGuideCache();

        int activeAStarIndex = 0;
        int activeFlowFieldIndex = 0;
        int activeVolumeIndex = 0;

        for (int i = 0; i < MixedCacheEntriesPerFamily; i++)
        {
            SeedAStarMixedEntry(i, keepActiveRatio, ref activeAStarIndex);
            SeedFlowFieldMixedEntry(i, keepActiveRatio, ref activeFlowFieldIndex);
            SeedVolumeMixedEntry(i, keepActiveRatio, ref activeVolumeIndex);
            SeedHybridMixedEntry(i);
        }

        EnsureMixedCacheSeeded();
    }

    private void SeedAStarMixedEntry(int index, bool keepActiveRatio, ref int activeIndex)
    {
        if (!PathGuideFactory.RequestGuide(_mixedAStarRequests[index], out AStarGuide guide))
            throw new System.InvalidOperationException($"Preflight: mixed A* cache seed {index} failed.");

        if (keepActiveRatio && ShouldKeepActive(index))
            _mixedActiveAStarGuides[activeIndex++] = guide;
        else
            PathGuideFactory.ReturnGuide(guide);
    }

    private void SeedFlowFieldMixedEntry(int index, bool keepActiveRatio, ref int activeIndex)
    {
        if (!PathGuideFactory.RequestGuide(_mixedFlowFieldRequests[index], out FlowFieldGuide guide))
            throw new System.InvalidOperationException($"Preflight: mixed flow-field cache seed {index} failed.");

        if (keepActiveRatio && ShouldKeepActive(index))
            _mixedActiveFlowFieldGuides[activeIndex++] = guide;
        else
            PathGuideFactory.ReturnGuide(guide);
    }

    private void SeedVolumeMixedEntry(int index, bool keepActiveRatio, ref int activeIndex)
    {
        if (!PathGuideFactory.RequestGuide(_mixedVolumeRequests[index], out VolumeGuide guide))
            throw new System.InvalidOperationException($"Preflight: mixed volume cache seed {index} failed.");

        if (keepActiveRatio && ShouldKeepActive(index))
            _mixedActiveVolumeGuides[activeIndex++] = guide;
        else
            PathGuideFactory.ReturnGuide(guide);
    }

    private void SeedHybridMixedEntry(int index)
    {
        if (!PathGuideFactory.RequestGuide(_mixedHybridRequests[index], out FlowFieldGuide guide))
            throw new System.InvalidOperationException($"Preflight: mixed hybrid route-plan cache seed {index} failed.");

        PathGuideFactory.ReturnGuide(guide);
    }

    private static bool ShouldKeepActive(int index)
    {
        return index % MixedActiveStride == 0;
    }

    private static void EnsureMixedCacheSeeded()
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
    }

    private static bool ContainsRequestKey<T>(T[] requests, int count, int requestKey) where T : IPathRequest
    {
        for (int i = 0; i < count; i++)
        {
            if (requests[i].RequestCacheKey == requestKey)
                return true;
        }

        return false;
    }

    private static CacheInvalidationCardinality InvalidateMixedCacheFor(
        string chartKey,
        int expectedIndexedEntries)
    {
        CacheCounts before = CacheCounts.Capture();
        PathGuideFactory.InvalidateCacheFor(chartKey);
        CacheCounts after = CacheCounts.Capture();

        return new CacheInvalidationCardinality(
            entriesScanned: expectedIndexedEntries,
            entriesMatched: expectedIndexedEntries,
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

    private static void ReturnGuides<T>(T[] guides) where T : class, IGuide
    {
        if (guides == null)
            return;

        for (int i = 0; i < guides.Length; i++)
        {
            T guide = guides[i];
            if (guide == null)
                continue;

            PathGuideFactory.ReturnGuide(guide);
            guides[i] = null;
        }
    }

    private static void RegisterGasPlane(string chartName, Vector3d origin, int size)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                data[0, x, z] = new NavigationChartCell(TraversalMedia.Gas);

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);
    }

    private static void RegisterSolidCorridor(string chartName, Vector3d origin, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int x = 0; x < length; x++)
            data[0, x, 0] = true;

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);
    }

    private static void RegisterSolidPlane(string chartName, Vector3d origin, int length, int depth)
    {
        bool[,,] data = new bool[1, length, depth];
        for (int x = 0; x < length; x++)
            for (int z = 0; z < depth; z++)
                data[0, x, z] = true;

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);
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
