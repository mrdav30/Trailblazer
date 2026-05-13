using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the volume-path hot paths: raw VolumeSurveyor survey cost, cold and warm
/// guide resolution for a direct corridor and an L-shaped detour that produces a VolumeGuide
/// with waypoints.
/// </summary>
/// <remarks>
/// Volume paths traverse arbitrary space registered via <see cref="TraversalMedia.Gas"/> or
/// <see cref="TraversalMedia.Liquid"/> chart cells, independent of solid NavigationChart
/// walkability. These benchmarks focus on aerial/swimming movement through authored volume cells.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "Volume")]
public class VolumePathRequestBenchmarks
{
    private static readonly Vector3d DirectCorridorOffset = Vector3d.Zero;
    private static readonly Vector3d LShapeOffset = new(16, 0, 0);

    private BenchmarkPathFixture _fixture;

    // -------------------------------------------------------------------------
    // Direct gas corridor — 5 adjacent gas voxels in a straight line
    // -------------------------------------------------------------------------

    private VolumePathRequest _directCorridorRequest;
    private Vector3d _directCorridorOrigin;
    private Vector3d _directCorridorDestination;

    // -------------------------------------------------------------------------
    // L-shape gas path — 7 gas voxels forming an L, producing waypoints
    // -------------------------------------------------------------------------

    private VolumePathRequest _lShapeRequest;
    private Vector3d _lShapeOrigin;
    private Vector3d _lShapeDestination;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(32, 6, 8)));

        SetupDirectGasCorridor();
        SetupLShapeGasPath();
        ValidateConfiguredRequests();
        _fixture.FlushGuideCache();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupDirectGasCorridor()
    {
        // Five adjacent gas voxels in a straight line along X.
        // Adjacent voxels are required for VolumeSurveyor to find a path.
        var positions = new[]
        {
            DirectCorridorOffset + new Vector3d(0, 1, 0),
            DirectCorridorOffset + new Vector3d(1, 1, 0),
            DirectCorridorOffset + new Vector3d(2, 1, 0),
            DirectCorridorOffset + new Vector3d(3, 1, 0),
            DirectCorridorOffset + new Vector3d(4, 1, 0),
        };

        for (int i = 0; i < positions.Length; i++)
            RegisterGasVolumePoint(positions[i], $"VolDir_{i}");

        _directCorridorOrigin = positions[0];
        _directCorridorDestination = positions[positions.Length - 1];

        _directCorridorRequest = VolumePathRequest.Create(_fixture.Context,
            _directCorridorOrigin,
            _directCorridorDestination,
            Fixed64.One,
            medium: TraversalMedium.Gas);

        if (_directCorridorRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: Volume direct corridor request could not be created.");

        // Validate the survey finds a route.
        VolumeSurveyResult check = VolumeSurveyor.Shared.FindPath(_directCorridorRequest);
        if (!check.HasPath)
            throw new System.InvalidOperationException(
                "Preflight: VolumeSurveyor found no path for the direct gas corridor.");

        // Prime the cache so warm benchmarks start from a hit.
        VolumeGuide primeGuide = PathGuideFactory.RequestVolume(_directCorridorRequest);
        if (primeGuide != null) PathGuideFactory.ReturnGuide(primeGuide);
    }

    private void SetupLShapeGasPath()
    {
        // Seven gas voxels in an L-shape: 4 cells along X, then a 90-degree turn, 3 cells along Z.
        //
        //   (0,1,0) → (1,1,0) → (2,1,0) → (2,1,1) → (2,1,2) → (2,1,3)
        //
        // The direction change forces VolumeSurveyor to generate an intermediate waypoint.
        var positions = new[]
        {
            LShapeOffset + new Vector3d(0, 1, 0),
            LShapeOffset + new Vector3d(1, 1, 0),
            LShapeOffset + new Vector3d(2, 1, 0),
            LShapeOffset + new Vector3d(2, 1, 1),
            LShapeOffset + new Vector3d(2, 1, 2),
            LShapeOffset + new Vector3d(2, 1, 3),
        };

        for (int i = 0; i < positions.Length; i++)
            RegisterGasVolumePoint(positions[i], $"VolLShape_{i}");

        _lShapeOrigin = positions[0];
        _lShapeDestination = positions[positions.Length - 1];

        _lShapeRequest = VolumePathRequest.Create(_fixture.Context,
            _lShapeOrigin,
            _lShapeDestination,
            Fixed64.One,
            medium: TraversalMedium.Gas);

        if (_lShapeRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: Volume L-shape request could not be created.");

        VolumeSurveyResult check = VolumeSurveyor.Shared.FindPath(_lShapeRequest);
        if (!check.HasPath)
            throw new System.InvalidOperationException(
                "Preflight: VolumeSurveyor found no path for the L-shape gas path.");

        VolumeGuide primeGuide = PathGuideFactory.RequestVolume(_lShapeRequest);
        if (primeGuide != null) PathGuideFactory.ReturnGuide(primeGuide);
    }

    private void ValidateConfiguredRequests()
    {
        EnsureVolumeSurveyResolves(_directCorridorRequest, nameof(_directCorridorRequest));
        EnsureVolumeSurveyResolves(_lShapeRequest, nameof(_lShapeRequest));
        EnsureVolumeGuideResolves(_directCorridorRequest, nameof(_directCorridorRequest));
        EnsureVolumeGuideResolves(_lShapeRequest, nameof(_lShapeRequest));
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);
    }

    private static void EnsureVolumeSurveyResolves(VolumePathRequest request, string requestName)
    {
        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        if (!result.HasPath)
            throw new System.InvalidOperationException(
                $"Preflight: raw volume survey for {requestName} failed after all volume benchmark charts were configured.");
    }

    private static void EnsureVolumeGuideResolves(VolumePathRequest request, string requestName)
    {
        VolumeGuide guide = PathGuideFactory.RequestVolume(request);
        if (guide == null)
            throw new System.InvalidOperationException(
                $"Preflight: volume guide request for {requestName} failed after all volume benchmark charts were configured.");

        if (guide.ActiveWaypoints.Length == 0)
            throw new System.InvalidOperationException(
                $"Preflight: volume guide request for {requestName} returned no waypoints.");

        PathGuideFactory.ReturnGuide(guide);
    }

    // -------------------------------------------------------------------------
    // Iteration setup: flush cache before cold benchmarks
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(RawSurvey_DirectGasCorridor),
        nameof(ColdGuide_DirectGasCorridor),
        nameof(ColdGuide_LShapeGasPath)
    })]
    public void FlushCacheBeforeCold()
    {
        _fixture.FlushGuideCache();
    }

    // -------------------------------------------------------------------------
    // Raw survey benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raw VolumeSurveyor cost for a 5-cell straight gas corridor.
    /// Measures survey work without guide allocation or cache interaction.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Raw")]
    public bool RawSurvey_DirectGasCorridor()
    {
        return VolumeSurveyor.Shared.FindPath(_directCorridorRequest).HasPath;
    }

    // -------------------------------------------------------------------------
    // Cold guide benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cold volume guide request for a direct 5-cell gas corridor.
    /// Guide cache is flushed before each iteration.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Cold")]
    public bool ColdGuide_DirectGasCorridor()
    {
        VolumeGuide guide = PathGuideFactory.RequestVolume(_directCorridorRequest);
        if (guide != null) PathGuideFactory.ReturnGuide(guide);
        return guide != null;
    }

    /// <summary>
    /// Cold volume guide request for an L-shaped gas path that requires waypoint reconstruction.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Cold")]
    public bool ColdGuide_LShapeGasPath()
    {
        VolumeGuide guide = PathGuideFactory.RequestVolume(_lShapeRequest);
        if (guide != null) PathGuideFactory.ReturnGuide(guide);
        return guide != null;
    }

    // -------------------------------------------------------------------------
    // Warm guide benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Warm volume guide request for a direct 5-cell gas corridor (cache hit).
    /// This is the baseline for all volume benchmarks.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pathing", "Volume", "Warm")]
    public bool WarmGuide_DirectGasCorridor()
    {
        VolumeGuide guide = PathGuideFactory.RequestVolume(_directCorridorRequest);
        if (guide != null) PathGuideFactory.ReturnGuide(guide);
        return guide != null;
    }

    /// <summary>
    /// Warm volume guide request for an L-shaped gas path (cache hit).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Warm")]
    public bool WarmGuide_LShapeGasPath()
    {
        VolumeGuide guide = PathGuideFactory.RequestVolume(_lShapeRequest);
        if (guide != null) PathGuideFactory.ReturnGuide(guide);
        return guide != null;
    }

    /// <summary>Constructs a volume request for the direct gas corridor.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Request")]
    public VolumePathRequest RequestConstruction_DirectGasCorridor()
    {
        return VolumePathRequest.Create(_fixture.Context,
            _directCorridorOrigin,
            _directCorridorDestination,
            Fixed64.One,
            medium: TraversalMedium.Gas);
    }

    /// <summary>Reads the cache key for a pre-created direct gas-corridor volume request.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Volume", "Request", "Key")]
    public int RequestCacheKey_DirectGasCorridor()
    {
        return _directCorridorRequest.RequestCacheKey;
    }

    // -------------------------------------------------------------------------
    // Private chart registration helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a single gas volume voxel at <paramref name="position"/> using its own chart.
    /// The chart is a 3×3×3 grid with only the centre cell set to <see cref="TraversalMedia.Gas"/>.
    /// </summary>
    private static void RegisterGasVolumePoint(Vector3d position, string chartName)
    {
        Vector3d minBounds = position - new Vector3d(1, 1, 1);
        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(TraversalMedia.Gas);

        NavigationChart chart = NavigationChart.From3D(chartName, data, minBounds, Fixed64.One);
        PathManager.Register(chart);
    }
}
