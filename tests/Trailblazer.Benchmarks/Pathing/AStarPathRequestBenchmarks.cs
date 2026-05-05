using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
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
    // -------------------------------------------------------------------------
    // Small open-plane scenario (32x32)
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _openPlane32Fixture;
    private AStarPathRequest _openPlane32Request;
    private Vector3d _openPlane32Origin;
    private Vector3d _openPlane32Destination;

    // -------------------------------------------------------------------------
    // Corridor scenarios
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _corridor64Fixture;
    private AStarPathRequest _corridor64Request;

    private BenchmarkPathFixture _corridor256Fixture;
    private AStarPathRequest _corridor256Request;

    private BenchmarkPathFixture _corridor1024Fixture;
    private AStarPathRequest _corridor1024Request;

    // -------------------------------------------------------------------------
    // Sparse blocker field (64x64)
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _blockerFixture;
    private AStarPathRequest _blockerRequest;
    private Vector3d _blockerOrigin;
    private Vector3d _blockerDestination;

    // -------------------------------------------------------------------------
    // Heuristic comparison (64x64 open plane)
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _heuristicFixture;
    private AStarPathRequest _manhattanRequest;
    private AStarPathRequest _octileRequest;
    private AStarPathRequest _euclideanRequest;

    // -------------------------------------------------------------------------
    // Choke-point failure scenario
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _chokeFixture;
    private Vector3d _chokeOrigin;
    private Vector3d _chokeDestination;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        SetupOpenPlane32();
        SetupCorridor64();
        SetupCorridor256();
        SetupCorridor1024();
        SetupBlockerField64();
        SetupHeuristics64();
        SetupChokePoint();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _openPlane32Fixture?.Teardown();
        _corridor64Fixture?.Teardown();
        _corridor256Fixture?.Teardown();
        _corridor1024Fixture?.Teardown();
        _blockerFixture?.Teardown();
        _heuristicFixture?.Teardown();
        _chokeFixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupOpenPlane32()
    {
        _openPlane32Fixture = new BenchmarkPathFixture();
        _openPlane32Fixture.Setup(BenchmarkChartFactory.GridConfigForSquare(32));

        (_openPlane32Origin, _openPlane32Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("AStarOpenPlane32", 32);

        BenchmarkPreflight.AssertAStarRouteExists(
            _openPlane32Origin, _openPlane32Destination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _openPlane32Request = AStarPathRequest.Create(
            _openPlane32Origin, _openPlane32Destination, Fixed64.One);
    }

    private void SetupCorridor64()
    {
        _corridor64Fixture = new BenchmarkPathFixture();
        _corridor64Fixture.Setup(BenchmarkChartFactory.GridConfigForCorridor(64));

        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor("AStarCorridor64", 64);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor64Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupCorridor256()
    {
        _corridor256Fixture = new BenchmarkPathFixture();
        _corridor256Fixture.Setup(BenchmarkChartFactory.GridConfigForCorridor(256));

        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor("AStarCorridor256", 256);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor256Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupCorridor1024()
    {
        _corridor1024Fixture = new BenchmarkPathFixture();
        _corridor1024Fixture.Setup(BenchmarkChartFactory.GridConfigForCorridor(1024));

        var (start, end) = BenchmarkChartFactory.RegisterLongCorridor("AStarCorridor1024", 1024);

        BenchmarkPreflight.AssertAStarRouteExists(start, end, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _corridor1024Request = AStarPathRequest.Create(start, end, Fixed64.One);
    }

    private void SetupBlockerField64()
    {
        _blockerFixture = new BenchmarkPathFixture();
        _blockerFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(64));

        (_blockerOrigin, _blockerDestination) =
            BenchmarkChartFactory.RegisterSparseBlockerField("AStarBlocker64", 64);

        BenchmarkPreflight.AssertAStarRouteExists(
            _blockerOrigin, _blockerDestination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _blockerRequest = AStarPathRequest.Create(_blockerOrigin, _blockerDestination, Fixed64.One);
    }

    private void SetupHeuristics64()
    {
        _heuristicFixture = new BenchmarkPathFixture();
        _heuristicFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(64));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("AStarHeuristic64", 64);

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
        _chokeFixture = new BenchmarkPathFixture();
        _chokeFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(16));

        (_chokeOrigin, _chokeDestination) =
            BenchmarkChartFactory.RegisterChokePoint("AStarChoke");

        // A size-1 agent can pass; this preflight confirms the route exists.
        BenchmarkPreflight.AssertAStarRouteExists(_chokeOrigin, _chokeDestination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();
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
        nameof(ColdGuide_Heuristic_Euclidean),
        nameof(FailedRoute_ChokeUnitSize2)
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
        AStarPathRequest request = AStarPathRequest.Create(
            _chokeOrigin, _chokeDestination, Fixed64.Two);

        if (request == null)
            return false;

        bool ok = PathGuideFactory.RequestGuide(request, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }
}
