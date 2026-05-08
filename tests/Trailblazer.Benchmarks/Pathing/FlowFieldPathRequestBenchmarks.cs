using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
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
    private static readonly Vector3d OpenPlane64Offset = Vector3d.Zero;
    private static readonly Vector3d OpenPlane128Offset = new(160, 0, 0);
    private static readonly Vector3d Cluster64Offset = new(0, 0, 160);
    private static readonly Vector3d Blocker64Offset = new(160, 0, 160);
    private static readonly Vector3d SampleVector32Offset = new(320, 0, 0);

    // -------------------------------------------------------------------------
    // Open plane 64x64 — cold / warm guide and raw survey
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _fixture;
    private FlowFieldPathRequest _openPlane64Request;
    private Vector3d _openPlane64Origin;
    private Vector3d _openPlane64Destination;

    // -------------------------------------------------------------------------
    // Open plane 128x128 — ExtraFloodRange scaling and batch many-start reuse
    // -------------------------------------------------------------------------

    private Vector3d _openPlane128Origin;
    private Vector3d _openPlane128Destination;
    private FlowFieldPathRequest _openPlane128Request;

    // -------------------------------------------------------------------------
    // Destination cluster — many starts, one destination
    // -------------------------------------------------------------------------

    private FlowFieldPathRequest[] _clusterRequests;
    private FlowFieldGuide[] _clusterGuideBuffer;
    private Vector3d _clusterDestination;

    // -------------------------------------------------------------------------
    // Blocker field 64x64 — with and without enlarged ExtraFloodRange
    // -------------------------------------------------------------------------

    private FlowFieldPathRequest _blockerDefaultFloodRequest;
    private FlowFieldPathRequest _blockerLargeFloodRequest;

    // -------------------------------------------------------------------------
    // SampleFlowVector diagnostic — prebuilt field
    // -------------------------------------------------------------------------

    private FlowFieldSurveyResult _prebuiltResult;
    private Vector3d _sampleExactPosition;
    private Vector3d _sampleFractionalPosition;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForArea(maxXExclusive: 352, maxZExclusive: 224));

        SetupOpenPlane64();
        SetupOpenPlane128();
        SetupDestinationCluster64();
        SetupBlockerFloodScaling64();
        SetupSampleFlowVector32();
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

    private void SetupOpenPlane64()
    {
        (_openPlane64Origin, _openPlane64Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFOpenPlane64", 64, OpenPlane64Offset);

        BenchmarkPreflight.AssertFlowFieldRouteExists(
            _openPlane64Origin, _openPlane64Destination, Fixed64.One);

        BenchmarkPathFixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak();

        _openPlane64Request = FlowFieldPathRequest.Create(
            _openPlane64Origin, _openPlane64Destination, Fixed64.One);
    }

    private void SetupOpenPlane128()
    {
        (_openPlane128Origin, _openPlane128Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFOpenPlane128", 128, OpenPlane128Offset);

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

        var (starts, destination) =
            BenchmarkChartFactory.RegisterDestinationCluster("FFCluster64", size, startCount, Cluster64Offset);

        _clusterDestination = destination;

        _clusterRequests = new FlowFieldPathRequest[startCount];
        _clusterGuideBuffer = new FlowFieldGuide[startCount];
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
        var (origin, destination) =
            BenchmarkChartFactory.RegisterSparseBlockerField("FFBlocker64", 64, Blocker64Offset);

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

        var (origin, destination) =
            BenchmarkChartFactory.RegisterOpenPlane("FFSampleVector32", size, SampleVector32Offset);

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);

        if (!result.HasPath || result.Fields == null)
            throw new System.InvalidOperationException(
                "Preflight: Could not build flow field for SampleFlowVector benchmark.");

        _prebuiltResult = result;
        _sampleExactPosition = new Vector3d(
            origin.x + (Fixed64)(size / 2),
            origin.y,
            origin.z + (Fixed64)(size / 2));
        _sampleFractionalPosition = new Vector3d(
            origin.x + (Fixed64)(size / 2) + Fixed64.Half,
            origin.y,
            origin.z + (Fixed64)(size / 2) + Fixed64.Half);
    }

    private void ValidateConfiguredRequests()
    {
        EnsureFlowGuideResolves(_openPlane64Request, nameof(_openPlane64Request));
        EnsureFlowGuideResolves(_openPlane128Request, nameof(_openPlane128Request));
        EnsureFlowGuideResolves(_blockerDefaultFloodRequest, nameof(_blockerDefaultFloodRequest));
        EnsureFlowGuideResolves(_blockerLargeFloodRequest, nameof(_blockerLargeFloodRequest));

        for (int i = 0; i < _clusterRequests.Length; i++)
            EnsureFlowGuideResolves(_clusterRequests[i], $"{nameof(_clusterRequests)}[{i}]");
    }

    private static void EnsureFlowGuideResolves(FlowFieldPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: {requestName} failed after all flow-field benchmark fixtures were configured.");

        PathGuideFactory.ReturnGuide(guide);
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

    /// <summary>Constructs a flow-field request for the 64x64 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Request")]
    public FlowFieldPathRequest RequestConstruction_OpenPlane64()
    {
        return FlowFieldPathRequest.Create(_openPlane64Origin, _openPlane64Destination, Fixed64.One);
    }

    /// <summary>Reads the cache key for a pre-created 64x64 open-plane flow-field request.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Request", "Key")]
    public int RequestCacheKey_OpenPlane64()
    {
        return _openPlane64Request.RequestCacheKey;
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
        FlowFieldGuide[] guides = _clusterGuideBuffer;
        int resolved = 0;

        for (int i = 0; i < _clusterRequests.Length; i++)
        {
            if (PathGuideFactory.RequestGuide(_clusterRequests[i], out FlowFieldGuide guide))
            {
                guides[resolved++] = guide;
            }
        }

        for (int i = 0; i < resolved; i++)
        {
            PathGuideFactory.ReturnGuide(guides[i]);
            guides[i] = null;
        }

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
        return FlowFieldSurveyor.SampleFlowVector(_sampleExactPosition, _prebuiltResult);
    }

    /// <summary>SampleFlowVector at a fractional (between-voxel) position.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "FlowField", "Sample")]
    public Vector3d SampleFlowVector_FractionalPosition()
    {
        return FlowFieldSurveyor.SampleFlowVector(_sampleFractionalPosition, _prebuiltResult);
    }
}
