using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the transition-aware hot paths: A* and flow-field cold and warm guide
/// resolution across disconnected charts connected by TraversalTransitions.
/// </summary>
/// <remarks>
/// Transition-aware paths are harder routing cases that layer on top of the direct A*/flow-field
/// baselines. Place these benchmarks after the simpler A* and flow-field paths are stable.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "Transition")]
public class TransitionFallbackBenchmarks
{
    private static readonly Vector3d AStarJumpOffset = Vector3d.Zero;
    private static readonly Vector3d AStarSwimOffset = new(16, 0, 0);
    private static readonly Vector3d FlowFieldJumpOffset = new(32, 0, 0);

    private BenchmarkPathFixture _fixture;

    // -------------------------------------------------------------------------
    // A* jump-link — two disconnected solid islands, single Jump transition
    // -------------------------------------------------------------------------

    private AStarPathRequest _astarJumpRequest;

    // -------------------------------------------------------------------------
    // A* swim-path — solid → SwimEntry → liquid corridor → SwimExit → solid
    // -------------------------------------------------------------------------

    private AStarPathRequest _astarSwimRequest;

    // -------------------------------------------------------------------------
    // Flow-field jump-link — same topology, staged flow-field guide
    // -------------------------------------------------------------------------

    private FlowFieldPathRequest _ffJumpRequest;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(48, 4, 4)));

        SetupAStarJumpLink();
        SetupAStarSwimPath();
        SetupFlowFieldJumpLink();

        _fixture.FlushGuideCache();
        PrimeConfiguredRequests();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupAStarJumpLink()
    {
        // Island A: two walkable cells at x=0,1.
        RegisterTwoPointIsland("TransAStar_JumpA", AStarJumpOffset);
        // Island B: two walkable cells at x=3,4.
        RegisterTwoPointIsland("TransAStar_JumpB", AStarJumpOffset + new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(AStarJumpOffset + new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(AStarJumpOffset + new Vector3d(3, 0, 0)),
            pathCostModifier: 4));

        var origin = AStarJumpOffset;
        var destination = AStarJumpOffset + new Vector3d(4, 0, 0);

        _astarJumpRequest = AStarPathRequest.Create(_fixture.Context,
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_astarJumpRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: A* jump-link transition request could not be created.");
    }

    private void SetupAStarSwimPath()
    {
        // Chart A: solid start (x=0,1); liquid bridge at x=2,3,4; Chart B: solid end (x=5,6).
        RegisterTwoPointIsland("TransAStar_SwimA", AStarSwimOffset);
        RegisterTwoPointIsland("TransAStar_SwimB", AStarSwimOffset + new Vector3d(5, 0, 0));

        // Register three liquid voxels as the swim corridor.
        RegisterLiquidVolumePoint(AStarSwimOffset + new Vector3d(2, 0, 0), "TransAStar_LiqA");
        RegisterLiquidVolumePoint(AStarSwimOffset + new Vector3d(3, 0, 0), "TransAStar_LiqB");
        RegisterLiquidVolumePoint(AStarSwimOffset + new Vector3d(4, 0, 0), "TransAStar_LiqC");

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-swim-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(AStarSwimOffset + new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Liquid(AStarSwimOffset + new Vector3d(2, 0, 0)),
            pathCostModifier: 2));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-swim-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(AStarSwimOffset + new Vector3d(4, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(AStarSwimOffset + new Vector3d(5, 0, 0)),
            pathCostModifier: 1));

        var origin = AStarSwimOffset;
        var destination = AStarSwimOffset + new Vector3d(6, 0, 0);

        _astarSwimRequest = AStarPathRequest.Create(_fixture.Context,
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_astarSwimRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: A* swim-path transition request could not be created.");
    }

    private void SetupFlowFieldJumpLink()
    {
        // Same jump-link topology as A*, but using FlowFieldPathRequest with AllowTraversalTransitions.
        RegisterTwoPointIsland("TransFF_JumpA", FlowFieldJumpOffset);
        RegisterTwoPointIsland("TransFF_JumpB", FlowFieldJumpOffset + new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-ff-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(FlowFieldJumpOffset + new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(FlowFieldJumpOffset + new Vector3d(3, 0, 0)),
            pathCostModifier: 4));

        var origin = FlowFieldJumpOffset;
        var destination = FlowFieldJumpOffset + new Vector3d(4, 0, 0);

        // FlowFieldPathRequest.Create returns null when endpoints cannot be resolved; transition
        // fallback must be enabled during creation so disconnected endpoint requests can be formed.
        _ffJumpRequest = FlowFieldPathRequest.Create(_fixture.Context,
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_ffJumpRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: Flow-field jump-link transition request could not be created.");
    }

    private void PrimeConfiguredRequests()
    {
        EnsureAStarGuideResolves(_astarJumpRequest, nameof(_astarJumpRequest));
        EnsureAStarGuideResolves(_astarSwimRequest, nameof(_astarSwimRequest));
        EnsureFlowFieldGuideResolves(_ffJumpRequest, nameof(_ffJumpRequest));
    }

    private static void EnsureAStarGuideResolves(AStarPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out AStarGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: {requestName} failed after all transition benchmark fixtures were configured.");

        PathGuideFactory.ReturnGuide(guide);
    }

    private static void EnsureFlowFieldGuideResolves(FlowFieldPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: {requestName} failed after all transition benchmark fixtures were configured.");

        PathGuideFactory.ReturnGuide(guide);
    }

    // -------------------------------------------------------------------------
    // Iteration setup: flush cache before cold benchmarks
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(ColdGuide_AStar_JumpLink),
        nameof(ColdGuide_AStar_SwimPath),
        nameof(ColdGuide_FlowField_JumpLink)
    })]
    public void FlushCacheBeforeCold()
    {
        _fixture.FlushGuideCache();
    }

    // -------------------------------------------------------------------------
    // A* jump-link benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cold A* guide request across a disconnected jump-link transition.
    /// Transition fallback path is exercised on every iteration.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Transition", "Cold", "AStar")]
    public bool ColdGuide_AStar_JumpLink()
    {
        bool ok = PathGuideFactory.RequestGuide(_astarJumpRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>
    /// Warm A* guide request across a jump-link transition (cache hit).
    /// This is the baseline for all transition benchmarks.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pathing", "Transition", "Warm", "AStar")]
    public bool WarmGuide_AStar_JumpLink()
    {
        bool ok = PathGuideFactory.RequestGuide(_astarJumpRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // A* swim-path benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cold A* guide request through a solid → swim-entry → liquid → swim-exit → solid path.
    /// Exercises multi-segment transition assembly.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Transition", "Cold", "AStar")]
    public bool ColdGuide_AStar_SwimPath()
    {
        bool ok = PathGuideFactory.RequestGuide(_astarSwimRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>
    /// Warm A* guide request through the swim path (cache hit).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Transition", "Warm", "AStar")]
    public bool WarmGuide_AStar_SwimPath()
    {
        bool ok = PathGuideFactory.RequestGuide(_astarSwimRequest, out AStarGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Flow-field jump-link benchmarks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cold flow-field guide request across a disconnected jump-link transition.
    /// Returns a staged FlowFieldGuide covering each segment between transitions.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Transition", "Cold", "FlowField")]
    public bool ColdGuide_FlowField_JumpLink()
    {
        bool ok = PathGuideFactory.RequestGuide(_ffJumpRequest, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    /// <summary>
    /// Warm flow-field staged guide request across a jump-link transition (cache hit).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Transition", "Warm", "FlowField")]
    public bool WarmGuide_FlowField_JumpLink()
    {
        bool ok = PathGuideFactory.RequestGuide(_ffJumpRequest, out FlowFieldGuide guide);
        if (ok) PathGuideFactory.ReturnGuide(guide);
        return ok;
    }

    // -------------------------------------------------------------------------
    // Private chart registration helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a minimal two-cell walkable solid chart anchored at <paramref name="origin"/>,
    /// covering positions <c>origin</c> and <c>origin + (1,0,0)</c>.
    /// Equivalent to the PathTestFactory.RegisterFromData pattern used in transition tests.
    /// </summary>
    private static void RegisterTwoPointIsland(string chartName, Vector3d origin)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);
    }

    /// <summary>
    /// Registers a single liquid volume voxel at <paramref name="position"/> using its own chart.
    /// The chart is a 3×3×3 grid with only the centre cell set to <see cref="TraversalMedia.Liquid"/>.
    /// </summary>
    private static void RegisterLiquidVolumePoint(Vector3d position, string chartName)
    {
        Vector3d minBounds = position - new Vector3d(1, 1, 1);
        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(TraversalMedia.Liquid);

        NavigationChart chart = NavigationChart.From3D(chartName, data, minBounds, Fixed64.One);
        PathManager.Register(chart);
    }
}
