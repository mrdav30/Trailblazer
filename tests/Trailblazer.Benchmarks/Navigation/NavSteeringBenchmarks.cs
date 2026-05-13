using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Support;

namespace Trailblazer.Benchmarks.Navigation;

// PathRecheckCooldownFrames default value (mirrors NavSteering.DefaultPathRecheckCooldown, which is protected)
internal static class NavSteeringConstants
{
    internal const int DefaultRecheckCooldown = 16;
}

/// <summary>
/// Benchmarks the frame-facing NavSteering hot paths: first-frame resolution, direct LOS
/// steady state (default and every-frame cooldown), guided A* and flow-field steady state,
/// and combined-steering scans at increasing occupant densities.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Navigation", "Steering")]
public class NavSteeringBenchmarks
{
    private static readonly Vector3d DirectOffset = Vector3d.Zero;
    private static readonly Vector3d AStarGuidedOffset = new(48, 0, 0);
    private static readonly Vector3d FlowFieldGuidedOffset = new(96, 0, 0);
    private static readonly Vector3d DensityOffset = new(0, 0, 64);

    private BenchmarkPathFixture _fixture;

    // -------------------------------------------------------------------------
    // LOS / direct-path scenario — open plane 32x32
    // -------------------------------------------------------------------------

    private BenchmarkSteerAgent _directAgent;
    private NavSteering _directSteer;
    private AStarPathRequest _directRequest;

    // -------------------------------------------------------------------------
    // Guided A* scenario — open plane 32x32 (agent midway, destination at far corner)
    // -------------------------------------------------------------------------

    private BenchmarkSteerAgent _astarGuidedAgent;
    private NavSteering _astarGuidedSteer;
    private AStarPathRequest _astarGuidedRequest;

    // -------------------------------------------------------------------------
    // Guided flow-field scenario — open plane 32x32
    // -------------------------------------------------------------------------

    private BenchmarkSteerAgent _ffGuidedAgent;
    private NavSteering _ffGuidedSteer;
    private FlowFieldPathRequest _ffGuidedRequest;

    // -------------------------------------------------------------------------
    // Occupant-density steering scan — 32 / 128 / 512 occupants
    // -------------------------------------------------------------------------

    private BenchmarkSteerAgent _density32Agent;
    private NavSteering _density32Steer;
    private BenchmarkOccupant[] _density32Occupants;

    private BenchmarkSteerAgent _density128Agent;
    private NavSteering _density128Steer;
    private BenchmarkOccupant[] _density128Occupants;

    private BenchmarkSteerAgent _density512Agent;
    private NavSteering _density512Steer;
    private BenchmarkOccupant[] _density512Occupants;
    private Vector3d _combinedSteeringSink;

    // -------------------------------------------------------------------------
    // GlobalSetup / GlobalCleanup
    // -------------------------------------------------------------------------

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForArea(maxXExclusive: 196, maxZExclusive: 264));

        SetupDirectLos();
        SetupGuidedAStar();
        SetupGuidedFlowField();
        SetupOccupantDensities();
        ValidateConfiguredRequests();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        RemoveOccupants(_density32Occupants);
        RemoveOccupants(_density128Occupants);
        RemoveOccupants(_density512Occupants);

        _fixture?.Teardown();
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void SetupDirectLos()
    {
        const int size = 32;

        BenchmarkChartFactory.RegisterOpenPlane("SteeringDirect32", size, DirectOffset);

        // Agent at origin, destination 2 cells away — short LOS, no guide needed.
        Vector3d start = DirectOffset;
        Vector3d end = DirectOffset + new Vector3d(2, 0, 0);

        BenchmarkPreflight.AssertAStarRouteExists(_fixture.Context, start, end, Fixed64.One);
        _fixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);

        _directAgent = new BenchmarkSteerAgent(start) { Speed = Fixed64.One };
        _directSteer = NavSteering.CreateNew(_fixture.Context, _directAgent.Radius);

        _directRequest = AStarPathRequest.Create(_fixture.Context, start, end, Fixed64.One);
        _directSteer.ApplyPathRequest(_directRequest);

        // Warm the LOS path on the first call so subsequent iterations measure steady state.
        _directSteer.GetHeading(_directAgent);
    }

    private void SetupGuidedAStar()
    {
        const int size = 32;

        BenchmarkChartFactory.RegisterOpenPlane("SteeringAStarGuided32", size, AStarGuidedOffset);

        // Place the agent off-centre so LOS to the far corner is unlikely to be trivially direct.
        Vector3d start = AStarGuidedOffset + new Vector3d(1, 0, 1);
        Vector3d end = AStarGuidedOffset + new Vector3d(size - 1, 0, size - 1);

        BenchmarkPreflight.AssertAStarRouteExists(_fixture.Context, start, end, Fixed64.One);
        _fixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);

        _astarGuidedAgent = new BenchmarkSteerAgent(start) { Speed = Fixed64.One };
        _astarGuidedSteer = NavSteering.CreateNew(_fixture.Context, _astarGuidedAgent.Radius);

        // Disable LOS recheck so the guide-backed path stays active and measures the guided path.
        _astarGuidedSteer.PathRecheckCooldownFrames = int.MaxValue;

        _astarGuidedRequest = AStarPathRequest.Create(_fixture.Context, start, end, Fixed64.One);
        _astarGuidedSteer.ApplyPathRequest(_astarGuidedRequest);

        // Prime the guide so the benchmark body measures steady-state guide consumption, not cold resolution.
        _astarGuidedSteer.GetHeading(_astarGuidedAgent);
    }

    private void SetupGuidedFlowField()
    {
        const int size = 32;

        BenchmarkChartFactory.RegisterOpenPlane("SteeringFFGuided32", size, FlowFieldGuidedOffset);

        Vector3d start = FlowFieldGuidedOffset + new Vector3d(1, 0, 1);
        Vector3d end = FlowFieldGuidedOffset + new Vector3d(size - 1, 0, size - 1);

        BenchmarkPreflight.AssertFlowFieldRouteExists(_fixture.Context, start, end, Fixed64.One);
        _fixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);

        _ffGuidedAgent = new BenchmarkSteerAgent(start) { Speed = Fixed64.One };
        _ffGuidedSteer = NavSteering.CreateNew(_fixture.Context, _ffGuidedAgent.Radius);
        _ffGuidedSteer.PathRecheckCooldownFrames = int.MaxValue;

        _ffGuidedRequest = FlowFieldPathRequest.Create(_fixture.Context, start, end, Fixed64.One);
        _ffGuidedSteer.ApplyPathRequest(_ffGuidedRequest);

        // Prime the guide.
        _ffGuidedSteer.GetHeading(_ffGuidedAgent);
    }

    private void SetupOccupantDensities()
    {
        const int size = 196;
        BenchmarkChartFactory.RegisterOpenPlane("SteeringDensityShared", size, DensityOffset);

        SetupOccupantDensityScenario(32, 8, 4, 0, 64,
            out _density32Agent, out _density32Steer, out _density32Occupants);
        SetupOccupantDensityScenario(128, 16, 8, 64, 64,
            out _density128Agent, out _density128Steer, out _density128Occupants);
        SetupOccupantDensityScenario(512, 32, 16, 128, 64,
            out _density512Agent, out _density512Steer, out _density512Occupants);
    }

    private void ValidateConfiguredRequests()
    {
        EnsureAStarGuideResolves(_directRequest, nameof(_directRequest));
        EnsureAStarGuideResolves(_astarGuidedRequest, nameof(_astarGuidedRequest));
        EnsureFlowFieldGuideResolves(_ffGuidedRequest, nameof(_ffGuidedRequest));
        _fixture.FlushGuideCache();
        BenchmarkPreflight.AssertNoCacheLeak(_fixture.Context);
    }

    private static void EnsureAStarGuideResolves(AStarPathRequest request, string requestName)
    {
        if (!request.Context.Guides.RequestGuide(request, out AStarGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: {requestName} failed after all nav-steering benchmark charts were configured.");

        request.Context.Guides.ReturnGuide(guide);
    }

    private static void EnsureFlowFieldGuideResolves(FlowFieldPathRequest request, string requestName)
    {
        if (!request.Context.Guides.RequestGuide(request, out FlowFieldGuide guide))
            throw new System.InvalidOperationException(
                $"Preflight: {requestName} failed after all nav-steering benchmark charts were configured.");

        request.Context.Guides.ReturnGuide(guide);
    }

    private void SetupOccupantDensityScenario(
        int occupantCount,
        int gridWidth,
        int gridDepth,
        int originX,
        int originZ,
        out BenchmarkSteerAgent agent,
        out NavSteering steer,
        out BenchmarkOccupant[] occupants)
    {
        occupants = BenchmarkScenarioFactory.CreateOccupants(
            occupantCount,
            gridWidth,
            gridDepth,
            originX: originX,
            originZ: originZ);

        // Register each occupant with the VoxelGrid so the steering scan can find them.
        foreach (BenchmarkOccupant occupant in occupants)
        {
            if (_fixture.World.TryGetGrid(occupant.Position, out VoxelGrid grid))
                grid.TryAddVoxelOccupant(occupant);
        }

        // Place the measured agent in the middle of the occupant cloud.
        var agentPos = new Vector3d(originX + gridWidth / 2, 0, originZ + gridDepth / 2);
        agent = new BenchmarkSteerAgent(agentPos) { Speed = Fixed64.One };
        steer = NavSteering.CreateNew(_fixture.Context, agent.Radius);
    }

    private void RemoveOccupants(BenchmarkOccupant[] occupants)
    {
        if (occupants == null)
            return;

        foreach (BenchmarkOccupant occupant in occupants)
        {
            if (_fixture.World.TryGetGrid(occupant.Position, out VoxelGrid grid))
                grid.TryRemoveVoxelOccupant(occupant);
        }
    }

    // -------------------------------------------------------------------------
    // Iteration setup: re-apply path request before first-frame benchmarks
    // -------------------------------------------------------------------------

    [IterationSetup(Targets = new[]
    {
        nameof(FirstFrame_DirectLOS),
        nameof(FirstFrame_GuidedAStar)
    })]
    public void ReapplyRequestForFirstFrame()
    {
        _fixture.FlushGuideCache();
        _directSteer.ApplyPathRequest(_directRequest);
        _astarGuidedSteer.ApplyPathRequest(_astarGuidedRequest);
    }

    // -------------------------------------------------------------------------
    // First-frame costs
    // -------------------------------------------------------------------------

    /// <summary>
    /// First call to GetHeading after ApplyPathRequest — measures initial LOS check
    /// and potential guide resolution for a direct-path scenario.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "FirstFrame")]
    public Vector3d FirstFrame_DirectLOS()
    {
        return _directSteer.GetHeading(_directAgent);
    }

    /// <summary>
    /// First call to GetHeading after ApplyPathRequest for a long guided A* request —
    /// measures cold LOS check plus guide cold-resolution cost.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "FirstFrame")]
    public Vector3d FirstFrame_GuidedAStar()
    {
        return _astarGuidedSteer.GetHeading(_astarGuidedAgent);
    }

    // -------------------------------------------------------------------------
    // Steady-state LOS (default cooldown vs. every-frame)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Steady-state GetHeading for a direct-LOS path with default PathRecheckCooldownFrames.
    /// LOS is rechecked once every 16 frames; this measures the typical per-frame cost.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Navigation", "Steering", "SteadyState")]
    public Vector3d SteadyState_DirectLOS_DefaultCooldown()
    {
        return _directSteer.GetHeading(_directAgent);
    }

    /// <summary>
    /// Steady-state GetHeading with PathRecheckCooldownFrames = 0.
    /// LOS is re-evaluated every frame — measures the maximum per-frame LOS check cost.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "SteadyState")]
    public Vector3d SteadyState_DirectLOS_EveryFrameRecheck()
    {
        _directSteer.PathRecheckCooldownFrames = 0;
        Vector3d heading = _directSteer.GetHeading(_directAgent);
        _directSteer.PathRecheckCooldownFrames = NavSteeringConstants.DefaultRecheckCooldown;
        return heading;
    }

    // -------------------------------------------------------------------------
    // Guided A* steady state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Steady-state GetHeading following a cached A* guide (guide already resolved).
    /// LOS recheck cooldown is set to max to keep the guided path active.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "SteadyState", "AStar")]
    public Vector3d SteadyState_GuidedAStar()
    {
        return _astarGuidedSteer.GetHeading(_astarGuidedAgent);
    }

    // -------------------------------------------------------------------------
    // Guided flow-field steady state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Steady-state GetHeading following a cached flow-field guide.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "SteadyState", "FlowField")]
    public Vector3d SteadyState_GuidedFlowField()
    {
        return _ffGuidedSteer.GetHeading(_ffGuidedAgent);
    }

    // -------------------------------------------------------------------------
    // ComputeCombinedSteering — occupant density
    // -------------------------------------------------------------------------

    /// <summary>Combined-steering scan with 32 occupants in the world.</summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "Density")]
    public void CombinedSteering_Density32()
    {
        _combinedSteeringSink = _density32Steer.ComputeCombinedSteering(
            _density32Agent.Position,
            _density32Agent.Velocity,
            _density32Agent.Speed,
            _density32Agent.Size,
            _density32Agent.GlobalId);
    }

    /// <summary>Combined-steering scan with 128 occupants in the world.</summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "Density")]
    public void CombinedSteering_Density128()
    {
        _combinedSteeringSink = _density128Steer.ComputeCombinedSteering(
            _density128Agent.Position,
            _density128Agent.Velocity,
            _density128Agent.Speed,
            _density128Agent.Size,
            _density128Agent.GlobalId);
    }

    /// <summary>Combined-steering scan with 512 occupants in the world.</summary>
    [Benchmark]
    [BenchmarkCategory("Navigation", "Steering", "Density")]
    public void CombinedSteering_Density512()
    {
        _combinedSteeringSink = _density512Steer.ComputeCombinedSteering(
            _density512Agent.Position,
            _density512Agent.Velocity,
            _density512Agent.Speed,
            _density512Agent.Size,
            _density512Agent.GlobalId);
    }
}
