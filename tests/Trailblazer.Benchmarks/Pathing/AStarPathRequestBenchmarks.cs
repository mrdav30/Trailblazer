using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the A* request and guide hot paths: raw survey cost, cold and warm guide
/// resolution, long-corridor route reconstruction, heuristic comparison, and failed routes.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "AStar")]
public class AStarPathRequestBenchmarks
{
    private static readonly Vector3d OpenPlane32Offset = Vector3d.Zero;
    private static readonly Vector3d Corridor64Offset = new(0, 0, 80);
    private static readonly Vector3d Corridor256Offset = new(0, 0, 88);
    private static readonly Vector3d Corridor1024Offset = new(0, 0, 96);
    private static readonly Vector3d Blocker64Offset = new(128, 0, 0);
    private static readonly Vector3d Heuristic64Offset = new(48, 0, 0);
    private static readonly Vector3d ChokeOffset = new(1040, 0, 0);

    private BenchmarkPathFixture _fixture;

    // -------------------------------------------------------------------------
    // Small open-plane scenario (32x32)
    // -------------------------------------------------------------------------

    private AStarPathRequest _openPlane32Request;
    private Vector3d _openPlane32Origin;
    private Vector3d _openPlane32Destination;

    // -------------------------------------------------------------------------
    // Corridor scenarios
    // -------------------------------------------------------------------------

    private AStarPathRequest _corridor64Request;

    private AStarPathRequest _corridor256Request;

    private AStarPathRequest _corridor1024Request;

    // -------------------------------------------------------------------------
    // Sparse blocker field (64x64)
    // -------------------------------------------------------------------------

    private AStarPathRequest _blockerRequest;
    private Vector3d _blockerOrigin;
    private Vector3d _blockerDestination;

    // -------------------------------------------------------------------------
    // Heuristic comparison (64x64 open plane)
    // -------------------------------------------------------------------------

    private AStarPathRequest _manhattanRequest;
    private AStarPathRequest _octileRequest;
    private AStarPathRequest _euclideanRequest;

    // -------------------------------------------------------------------------
    // Choke-point failure scenario
    // -------------------------------------------------------------------------

    private Vector3d _chokeOrigin;
    private Vector3d _chokeDestination;
    private AStarPathRequest _chokeUnitSize2Request;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForArea(maxXExclusive: 1052, maxZExclusive: 112));

        SetupOpenPlane32();
        SetupCorridor64();
        SetupCorridor256();
        SetupCorridor1024();
        SetupBlockerField64();
        SetupHeuristics64();
        SetupChokePoint();
        ValidateConfiguredRequests();
        BenchmarkPathFixture.FlushGuideCache();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupOpenPlane32()
    {
        (_openPlane32Origin, _openPlane32Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("AStarOpenPlane32", 32, OpenPlane32Offset);

        BenchmarkPreflight.AssertAStarRouteExists(
            _openPlane32Origin, _openPlane32Destination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _openPlane32Request = AStarPathRequest.Create(
            _openPlane32Origin, _openPlane32Destination, Fixed64.One);
    }

    private void SetupCorridor64()
    {
        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor(
            "AStarCorridor64",
            64,
            Corridor64Offset);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor64Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupCorridor256()
    {
        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor(
            "AStarCorridor256",
            256,
            Corridor256Offset);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor256Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupCorridor1024()
    {
        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor(
            "AStarCorridor1024",
            1024,
            Corridor1024Offset);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor1024Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupBlockerField64()
    {
        (_blockerOrigin, _blockerDestination) =
            BenchmarkChartFactory.RegisterSparseBlockerField("AStarBlocker64", 64, Blocker64Offset);

        BenchmarkPreflight.AssertAStarRouteExists(
            _blockerOrigin, _blockerDestination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _blockerRequest = AStarPathRequest.Create(_blockerOrigin, _blockerDestination, Fixed64.One);
    }

    private void SetupHeuristics64()
    {
        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("AStarHeuristic64", 64, Heuristic64Offset);

        BenchmarkPreflight.AssertAStarRouteExists(origin, destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _manhattanRequest = AStarPathRequest.Create(
            origin, destination, Fixed64.One, HeuristicMethod.Manhattan);
        _octileRequest = AStarPathRequest.Create(
            origin, destination, Fixed64.One, HeuristicMethod.Octile);
        _euclideanRequest = AStarPathRequest.Create(
            origin, destination, Fixed64.One, HeuristicMethod.Euclidean);
    }

    private void SetupChokePoint()
    {
        (_chokeOrigin, _chokeDestination) =
            BenchmarkChartFactory.RegisterChokePoint("AStarChoke", ChokeOffset);

        // A size-1 agent can pass; this preflight confirms the route exists.
        BenchmarkPreflight.AssertAStarRouteExists(_chokeOrigin, _chokeDestination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _chokeUnitSize2Request = AStarPathRequest.Create(
            _chokeOrigin,
            _chokeDestination,
            Fixed64.Two);

        if (_chokeUnitSize2Request == null)
            throw new System.InvalidOperationException(
                "Preflight: choke-point unit-size-2 request could not be created.");
    }

    private void ValidateConfiguredRequests()
    {
        EnsureAStarSurveyResolves(_openPlane32Request, nameof(_openPlane32Request));
        EnsureAStarGuideResolves(_openPlane32Request, nameof(_openPlane32Request));
        EnsureAStarGuideResolves(_corridor64Request, nameof(_corridor64Request));
        EnsureAStarGuideResolves(_corridor256Request, nameof(_corridor256Request));
        EnsureAStarGuideResolves(_corridor1024Request, nameof(_corridor1024Request));
        EnsureAStarGuideResolves(_blockerRequest, nameof(_blockerRequest));
        EnsureAStarGuideResolves(_manhattanRequest, nameof(_manhattanRequest));
        EnsureAStarGuideResolves(_octileRequest, nameof(_octileRequest));
        EnsureAStarGuideResolves(_euclideanRequest, nameof(_euclideanRequest));

        if (PathGuideFactory.RequestGuide(_chokeUnitSize2Request, out AStarGuide blockedGuide))
        {
            PathGuideFactory.ReturnGuide(blockedGuide);
            throw new System.InvalidOperationException(
                "Preflight: unit-size-2 choke request unexpectedly resolved after all A* benchmark charts were configured.");
        }

        BenchmarkPreflight.AssertNoCacheLeak();
    }

    private static void EnsureAStarSurveyResolves(AStarPathRequest request, string requestName)
    {
        AStarSurveyResult result = AStarSurveyor.Shared.FindPath(request);
        if (!result.HasPath)
            throw new System.InvalidOperationException(
                $"Preflight: raw A* survey for {requestName} failed after all A* benchmark charts were configured.");
    }

    private static void EnsureAStarGuideResolves(AStarPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out AStarGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: guide request for {requestName} failed after all A* benchmark charts were configured.");

        if (guide.ActiveWaypoints.Length == 0)
            throw new System.InvalidOperationException(
                $"Preflight: guide request for {requestName} returned no waypoints.");

        PathGuideFactory.ReturnGuide(guide);
    }

    // -------------------------------------------------------------------------
    // Iteration setup: flush cache before cold-request benchmarks
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(ColdGuide_OpenPlane32),
        nameof(ColdGuide_Corridor64),
        nameof(ColdGuide_Corridor256),
        nameof(ColdGuide_Corridor1024),
        nameof(ColdGuide_BlockerField64),
        nameof(ColdGuide_Heuristic_Manhattan),
        nameof(ColdGuide_Heuristic_Octile),
        nameof(ColdGuide_Heuristic_Euclidean)
    })]
    public void FlushForColdRun()
    {
        BenchmarkPathFixture.FlushGuideCache();
    }

    // -------------------------------------------------------------------------
    // A* raw survey
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raw A* survey on a 32x32 open plane, bypassing guide caching.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Survey")]
    public AStarSurveyResult RawSurvey_OpenPlane32()
    {
        return AStarSurveyor.Shared.FindPath(_openPlane32Request);
    }

    // -------------------------------------------------------------------------
    // Cold guide requests (cache is flushed before each iteration)
    // -------------------------------------------------------------------------

    /// <summary>Cold guide request — 32x32 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold")]
    public bool ColdGuide_OpenPlane32()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane32Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide request — 64-cell corridor (route reconstruction).</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Corridor")]
    public bool ColdGuide_Corridor64()
    {
        bool ok = PathGuideFactory.RequestGuide(_corridor64Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide request — 256-cell corridor.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Corridor")]
    public bool ColdGuide_Corridor256()
    {
        bool ok = PathGuideFactory.RequestGuide(_corridor256Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide request — 1024-cell corridor (long route reconstruction).</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Corridor")]
    public bool ColdGuide_Corridor1024()
    {
        bool ok = PathGuideFactory.RequestGuide(_corridor1024Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide request — 64x64 sparse-blocker field.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Blockers")]
    public bool ColdGuide_BlockerField64()
    {
        bool ok = PathGuideFactory.RequestGuide(_blockerRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Warm guide requests (guide already cached from previous iteration)
    // -------------------------------------------------------------------------

    /// <summary>Warm guide request — 32x32 open plane (cache hit).</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pathing", "AStar", "Warm")]
    public bool WarmGuide_OpenPlane32()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane32Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Warm guide request — 1024-cell corridor (cache hit).</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Warm", "Corridor")]
    public bool WarmGuide_Corridor1024()
    {
        bool ok = PathGuideFactory.RequestGuide(_corridor1024Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Constructs an A* request for the 32x32 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Request")]
    public AStarPathRequest RequestConstruction_OpenPlane32()
    {
        return AStarPathRequest.Create(_openPlane32Origin, _openPlane32Destination, Fixed64.One);
    }

    /// <summary>Reads the cache key for a pre-created 32x32 open-plane A* request.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Request", "Key")]
    public int RequestCacheKey_OpenPlane32()
    {
        return _openPlane32Request.RequestCacheKey;
    }

    // -------------------------------------------------------------------------
    // Heuristic comparison
    // -------------------------------------------------------------------------

    /// <summary>Cold guide — Manhattan heuristic, 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Heuristic")]
    public bool ColdGuide_Heuristic_Manhattan()
    {
        bool ok = PathGuideFactory.RequestGuide(_manhattanRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide — Octile heuristic, 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Heuristic")]
    public bool ColdGuide_Heuristic_Octile()
    {
        bool ok = PathGuideFactory.RequestGuide(_octileRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide — Euclidean heuristic, 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Cold", "Heuristic")]
    public bool ColdGuide_Heuristic_Euclidean()
    {
        bool ok = PathGuideFactory.RequestGuide(_euclideanRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Failed route (choke with unit size 2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts an A* guide request through the choke with a size-2 agent.
    /// The choke has only a 1-voxel gap, so this should return no route.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "AStar", "Failed")]
    public bool FailedRoute_ChokeUnitSize2()
    {
        bool ok = PathGuideFactory.RequestGuide(_chokeUnitSize2Request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }
}
