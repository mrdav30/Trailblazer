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
    // -------------------------------------------------------------------------
    // A* jump-link — two disconnected solid islands, single Jump transition
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _astarJumpFixture;
    private AStarPathRequest _astarJumpRequest;

    // -------------------------------------------------------------------------
    // A* swim-path — solid → SwimEntry → liquid corridor → SwimExit → solid
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _astarSwimFixture;
    private AStarPathRequest _astarSwimRequest;

    // -------------------------------------------------------------------------
    // Flow-field jump-link — same topology, staged flow-field guide
    // -------------------------------------------------------------------------

    private BenchmarkPathFixture _ffJumpFixture;
    private FlowFieldPathRequest _ffJumpRequest;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        SetupAStarJumpLink();
        SetupAStarSwimPath();
        SetupFlowFieldJumpLink();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _astarJumpFixture?.Teardown();
        _astarSwimFixture?.Teardown();
        _ffJumpFixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupAStarJumpLink()
    {
        // A world large enough to contain both islands and the transition lookup.
        _astarJumpFixture = new BenchmarkPathFixture();
        _astarJumpFixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(12, 4, 4)));

        // Island A: two walkable cells at x=0,1.
        RegisterTwoPointIsland("TransAStar_JumpA", Vector3d.Zero);
        // Island B: two walkable cells at x=3,4.
        RegisterTwoPointIsland("TransAStar_JumpB", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4));

        var origin = Vector3d.Zero;
        var destination = new Vector3d(4, 0, 0);

        _astarJumpRequest = AStarPathRequest.Create(
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_astarJumpRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: A* jump-link transition request could not be created.");

        // Prime the guide so warm benchmarks hit the cache.
        bool ok = PathGuideFactory.RequestGuide(_astarJumpRequest, out AStarGuide primeGuide);
        if (ok) PathGuideFactory.ReturnGuide(primeGuide);
    }

    private void SetupAStarSwimPath()
    {
        // Chart A: solid start (x=0,1); liquid bridge at x=2,3,4; Chart B: solid end (x=5,6).
        _astarSwimFixture = new BenchmarkPathFixture();
        _astarSwimFixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(12, 4, 4)));

        RegisterTwoPointIsland("TransAStar_SwimA", Vector3d.Zero);
        RegisterTwoPointIsland("TransAStar_SwimB", new Vector3d(5, 0, 0));

        // Register three liquid voxels as the swim corridor.
        RegisterLiquidVolumePoint(new Vector3d(2, 0, 0), "TransAStar_LiqA");
        RegisterLiquidVolumePoint(new Vector3d(3, 0, 0), "TransAStar_LiqB");
        RegisterLiquidVolumePoint(new Vector3d(4, 0, 0), "TransAStar_LiqC");

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-swim-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            pathCostModifier: 2));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-astar-swim-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(4, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(5, 0, 0)),
            pathCostModifier: 1));

        var origin = Vector3d.Zero;
        var destination = new Vector3d(6, 0, 0);

        _astarSwimRequest = AStarPathRequest.Create(
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_astarSwimRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: A* swim-path transition request could not be created.");

        bool ok = PathGuideFactory.RequestGuide(_astarSwimRequest, out AStarGuide primeGuide);
        if (ok) PathGuideFactory.ReturnGuide(primeGuide);
    }

    private void SetupFlowFieldJumpLink()
    {
        // Same jump-link topology as A*, but using FlowFieldPathRequest with AllowTraversalTransitions.
        _ffJumpFixture = new BenchmarkPathFixture();
        _ffJumpFixture.Setup(new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(12, 4, 4)));

        RegisterTwoPointIsland("TransFF_JumpA", Vector3d.Zero);
        RegisterTwoPointIsland("TransFF_JumpB", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "trans-ff-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4));

        var origin = Vector3d.Zero;
        var destination = new Vector3d(4, 0, 0);

        // FlowFieldPathRequest.Create returns null when endpoints cannot be resolved; transition-aware
        // requests need AllowTraversalTransitions set after creation because the factory resolves
        // endpoints before the flag can be consulted.
        _ffJumpRequest = FlowFieldPathRequest.Create(
            origin,
            destination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_ffJumpRequest == null)
            throw new System.InvalidOperationException(
                "Preflight: Flow-field jump-link transition request could not be created.");

        // Prime the staged guide so warm benchmarks start from a cache hit.
        bool ok = PathGuideFactory.RequestGuide(_ffJumpRequest, out FlowFieldGuide primeGuide);
        if (ok) PathGuideFactory.ReturnGuide(primeGuide);
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
        BenchmarkPathFixture.FlushGuideCache();
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
