using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the flow-field request and reuse hot paths: raw field generation, cold and warm
/// guide resolution, many-start reuse, coverage miss, SampleFlowVector sampling, and
/// ExtraFloodRange scaling.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "FlowField")]
public class FlowFieldPathRequestBenchmarks
{
    // -------------------------------------------------------------------------
    // Open plane 64x64 — cold / warm guide and raw survey
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _openPlane64Fixture;
    private FlowFieldPathRequest _openPlane64Request;
    private Vector3d _openPlane64Origin;
    private Vector3d _openPlane64Destination;

    // -------------------------------------------------------------------------
    // Open plane 128x128 — ExtraFloodRange scaling and batch many-start reuse
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _openPlane128Fixture;
    private Vector3d _openPlane128Origin;
    private Vector3d _openPlane128Destination;
    private FlowFieldPathRequest _openPlane128Request;

    // -------------------------------------------------------------------------
    // Destination cluster — many starts, one destination
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _clusterFixture;
    private FlowFieldPathRequest[] _clusterRequests;
    private Vector3d _clusterDestination;

    // -------------------------------------------------------------------------
    // Blocker field 64x64 — with and without enlarged ExtraFloodRange
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _blockerFloodFixture;
    private FlowFieldPathRequest _blockerDefaultFloodRequest;
    private FlowFieldPathRequest _blockerLargeFloodRequest;

    // -------------------------------------------------------------------------
    // SampleFlowVector diagnostic — prebuilt field
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _sampleVectorFixture;
    private SwiftDictionary<WorldVoxelIndex, FlowField> _prebuiltFields;
    private Vector3d _sampleExactPosition;
    private Vector3d _sampleFractionalPosition;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        SetupOpenPlane64();
        SetupOpenPlane128();
        SetupDestinationCluster64();
        SetupBlockerFloodScaling64();
        SetupSampleFlowVector32();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _openPlane64Fixture?.Teardown();
        _openPlane128Fixture?.Teardown();
        _clusterFixture?.Teardown();
        _blockerFloodFixture?.Teardown();
        _sampleVectorFixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupOpenPlane64()
    {
        _openPlane64Fixture = new BenchmarkPathFixture();
        _openPlane64Fixture.Setup(BenchmarkChartFactory.GridConfigForSquare(64));

        (_openPlane64Origin, _openPlane64Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFOpenPlane64", 64);

        BenchmarkPreflight.AssertFlowFieldRouteExists(
            _openPlane64Origin, _openPlane64Destination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _openPlane64Request = FlowFieldPathRequest.Create(
            _openPlane64Origin, _openPlane64Destination, Fixed64.One);
    }

    private void SetupOpenPlane128()
    {
        _openPlane128Fixture = new BenchmarkPathFixture();
        _openPlane128Fixture.Setup(BenchmarkChartFactory.GridConfigForSquare(128));

        (_openPlane128Origin, _openPlane128Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFOpenPlane128", 128);

        BenchmarkPreflight.AssertFlowFieldRouteExists(
            _openPlane128Origin, _openPlane128Destination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _openPlane128Request = FlowFieldPathRequest.Create(
            _openPlane128Origin, _openPlane128Destination, Fixed64.One);
    }

    private void SetupDestinationCluster64()
    {
        const int size = 64;
        const int startCount = 32;

        _clusterFixture = new BenchmarkPathFixture();
        _clusterFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(size));

        var (starts, destination) =
            BenchmarkChartFactory.RegisterDestinationCluster("FFCluster64", size, startCount);

        _clusterDestination = destination;

        _clusterRequests = new FlowFieldPathRequest[startCount];
        for (int i = 0; i < startCount; i++)
        {
            _clusterRequests[i] = FlowFieldPathRequest.Create(starts[i], destination, Fixed64.One);
            if (_clusterRequests[i] == null)
                throw new System.InvalidOperationException(
                    $"Preflight: Could not create flow-field request from cluster start {starts[i]} -> {destination}.");
        }

        // Warm preflight for the first request to confirm reachability.
        BenchmarkPreflight.AssertFlowFieldRouteExists(starts[0], destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();
    }

    private void SetupBlockerFloodScaling64()
    {
        _blockerFloodFixture = new BenchmarkPathFixture();
        _blockerFloodFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(64));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterSparseBlockerField("FFBlocker64", 64);

        BenchmarkPreflight.AssertFlowFieldRouteExists(origin, destination, Fixed64.One);
        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _blockerDefaultFloodRequest = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
        _blockerDefaultFloodRequest.ExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

        _blockerLargeFloodRequest = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
        _blockerLargeFloodRequest.ExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange * 4;
    }

    private void SetupSampleFlowVector32()
    {
        const int size = 32;

        _sampleVectorFixture = new BenchmarkPathFixture();
        _sampleVectorFixture.Setup(BenchmarkChartFactory.GridConfigForSquare(size));

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFSampleVector32", size);

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        if (!result.HasPath || result.Fields == null)
            throw new System.InvalidOperationException(
                "Preflight: Could not build flow field for SampleFlowVector benchmark.");

        _prebuiltFields = result.Fields;
        _sampleExactPosition = new Vector3d(size / 2, 0, size / 2);
        _sampleFractionalPosition = new Vector3d((Fixed64)(size / 2) + Fixed64.Half, Fixed64.Zero, (Fixed64)(size / 2) + Fixed64.Half);
    }

    // -------------------------------------------------------------------------
    // Iteration setup: flush cache before cold-request benchmarks
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(ColdGuide_OpenPlane64),
        nameof(ColdGuide_OpenPlane128),
        nameof(RawSurvey_OpenPlane64),
        nameof(RawSurvey_Blocker_DefaultFloodRange),
        nameof(RawSurvey_Blocker_LargeFloodRange)
    })]
    public void FlushForColdRun()
    {
        BenchmarkPathFixture.FlushGuideCache();
    }

    // -------------------------------------------------------------------------
    // Raw flow-field survey (bypasses caching)
    // -------------------------------------------------------------------------

    /// <summary>Raw flow-field survey on a 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Survey")]
    public FlowFieldSurveyResult RawSurvey_OpenPlane64()
    {
        return FlowFieldSurveyor.Shared.FindPath(_openPlane64Request);
    }

    /// <summary>Raw survey on a 64x64 blocker field with default ExtraFloodRange.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Survey", "FloodRange")]
    public FlowFieldSurveyResult RawSurvey_Blocker_DefaultFloodRange()
    {
        return FlowFieldSurveyor.Shared.FindPath(_blockerDefaultFloodRequest);
    }

    /// <summary>Raw survey on a 64x64 blocker field with 4× ExtraFloodRange.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Survey", "FloodRange")]
    public FlowFieldSurveyResult RawSurvey_Blocker_LargeFloodRange()
    {
        return FlowFieldSurveyor.Shared.FindPath(_blockerLargeFloodRequest);
    }

    // -------------------------------------------------------------------------
    // Cold guide requests
    // -------------------------------------------------------------------------

    /// <summary>Cold guide request — 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Cold")]
    public bool ColdGuide_OpenPlane64()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane64Request, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Cold guide request — 128x128 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Cold")]
    public bool ColdGuide_OpenPlane128()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane128Request, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Warm guide requests (cache hit)
    // -------------------------------------------------------------------------

    /// <summary>Warm guide request — 64x64 open plane (cache hit, baseline).</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pathing", "FlowField", "Warm")]
    public bool WarmGuide_OpenPlane64()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane64Request, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>Warm guide request — 128x128 open plane (cache hit).</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Warm")]
    public bool WarmGuide_OpenPlane128()
    {
        bool ok = PathGuideFactory.RequestGuide(_openPlane128Request, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Many-start batch reuse (N starts sharing one destination field)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requests guides for 32 distinct starts that share one cached flow-field destination.
    /// The field is built once; subsequent starts reuse the cached field.
    /// Returns all guides after the iteration.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Warm", "Showcase")]
    public int ManyStartWarmReuse_32Starts()
    {
        FlowFieldGuide[] guides = new FlowFieldGuide[_clusterRequests.Length];
        int resolved = 0;

        for (int i = 0; i < _clusterRequests.Length; i++)
        {
            if (PathGuideFactory.RequestGuide(_clusterRequests[i], out FlowFieldGuide guide))
            {
                guides[resolved++] = guide;
            }
        }

        for (int i = 0; i < resolved; i++)
            PathGuideFactory.ReturnGuide(guides[i]);

        return resolved;
    }

    // -------------------------------------------------------------------------
    // SampleFlowVector
    // -------------------------------------------------------------------------

    /// <summary>SampleFlowVector at an exact voxel centre.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Sample")]
    public Vector3d SampleFlowVector_ExactVoxel()
    {
        return FlowFieldSurveyor.SampleFlowVector(_sampleExactPosition, _prebuiltFields);
    }

    /// <summary>SampleFlowVector at a fractional (between-voxel) position.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Sample")]
    public Vector3d SampleFlowVector_FractionalPosition()
    {
        return FlowFieldSurveyor.SampleFlowVector(_sampleFractionalPosition, _prebuiltFields);
    }
}
