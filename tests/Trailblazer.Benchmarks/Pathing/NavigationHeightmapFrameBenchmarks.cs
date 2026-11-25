using System;
using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections.Dimensions;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Profiles baseline heightmap grounding inside complete input-driven controller frames.</summary>
[MemoryDiagnoser]
[Config(typeof(PerformanceGateConfig))]
public class NavigationHeightmapFrameBenchmarks
{
    private const int PreparationFrames = 32;
    private const int Frames = 64;
    private const string MapId = "heightmap-frame";
    private static readonly string[] Workloads =
    {
        "Disabled", "Empty", "Preferred1", "Preferred16", "Preferred64",
        "Missing16", "Missing64", "Crossing64", "Churn64", "Stacked64", "YRejected64"
    };
    private readonly long[] _ticks = new long[Frames];
    private readonly long[] _allocations = new long[Frames];
    private readonly int[] _collections = new int[Frames];
    private readonly long[] _groundingTicks = new long[Frames];
    private readonly int[] _calls = new int[Frames];
    private readonly int[] _fallbacks = new int[Frames];
    private readonly long[] _hashes = new long[Frames];
    private readonly long[] _expectedHashes = new long[Frames];
    private BenchmarkPathFixture _fixture;
    private GroundedNavigator[] _agents;
    private int[] _expectedStripes;
    private HeightmapSurface[] _surfaces;
    private HeightmapSurface _alternate;
    private Fixed64 _groundY;
    private int _layerCount;
    private bool _alternateActive;
    private bool _hasReplay;
    private int _iteration;
    private long _firstFrameTicks;
    private long _firstFrameAllocation;
    private long _registrationTicks;
    private long _registrationAllocation;

    [Params(100, 500)]
    public int AgentCount { get; set; }

    [ParamsSource(nameof(Cases))]
    public string Workload { get; set; }

    public static string[] Cases => Workloads;

    // Call-boundary timers are a separate attribution pass, never the uninstrumented frame baseline.
    public bool Profile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration = NavigationGraphBenchmarkScenario.CreateConfiguration(100, 100);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(configuration, NavigationGraphBenchmarkScenario.CreateSettings(10_000, 1));
        _fixture.Context.SetFrameRate(32);
        PathQuery query = NavigationGraphBenchmarkScenario.Publish(_fixture, configuration, MapId, 100, 100,
            new NavigationWorkBudget(4_096, 16, 10_000, 320_000, 0, 0, 0, 0, 0, 0, 0));
        _groundY = query.Start.Position.Y;
        _layerCount = Workload is "Disabled" or "Empty" ? 0
            : Workload == "Preferred1" ? 1 : Workload.EndsWith("16", StringComparison.Ordinal) ? 16 : 64;
        _surfaces = new HeightmapSurface[_layerCount];
        var compression = new HeightmapCompression(_groundY, Fixed64.One);
        for (int i = 0; i < _layerCount; i++)
        {
            bool crossing = Workload == "Crossing64";
            bool full = Workload is "Stacked64" or "YRejected64"
                || (i == 0 && !Workload.StartsWith("Missing", StringComparison.Ordinal));
            Vector3d min = crossing ? new Vector3d((Fixed64)i - Fixed64.Half, Fixed64.Zero, -Fixed64.Half)
                : full ? new Vector3d(-1, 0, -1) : new Vector3d(1_000 + i * 200, 0, -1);
            _surfaces[i] = HeightmapSurface.FromCompressed("layer-" + i,
                new SwiftShortArray2D(2, crossing ? 101 : 2), min,
                crossing ? Fixed64.One : (Fixed64)100, compression);
        }
        if (Workload == "Churn64")
            _alternate = HeightmapSurface.FromCompressed("alternate", new SwiftShortArray2D(2, 2),
                new Vector3d(-1, 0, -1), (Fixed64)100, compression);
        _agents = new GroundedNavigator[AgentCount];
        _expectedStripes = new int[AgentCount];
        for (int i = 0; i < AgentCount; i++)
            _agents[i] = new GroundedNavigator(_fixture.Context,
                query.Start.Position + new Vector3d((Fixed64)(2 + i % 25) + Fixed64.FromFraction(i % 32, 32),
                    Fixed64.Zero, (Fixed64)(2 + i / 25 * 2)), query.Agent, i)
            { Profile = Profile };
        Prepare();
        Simulate64Frames();
        Array.Copy(_hashes, _expectedHashes, Frames);
        _hasReplay = true;
    }

    [IterationSetup]
    public void Prepare()
    {
        _fixture.Context.Heightmaps.Reset();
        Array.Fill(_expectedStripes, -1);
        _alternateActive = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < _surfaces.Length; i++)
            Register(_surfaces[i], i);
        _registrationTicks = Stopwatch.GetTimestamp() - started;
        _registrationAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        foreach (GroundedNavigator agent in _agents)
        {
            agent.PrepareFrame();
            agent.ConfigureHeightmapGrounding(Workload == "Disabled"
                ? HeightmapGroundingMode.Disabled : HeightmapGroundingMode.SurfaceLevelAndPosition);
        }
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            SimulateFrame(frame);
            if (frame == 0)
            {
                _firstFrameTicks = Stopwatch.GetTimestamp() - started;
                _firstFrameAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            ValidateAndHash();
        }
    }

    private void Register(HeightmapSurface surface, int ordinal)
    {
        bool rejected = Workload == "YRejected64";
        Require(_fixture.Context.Heightmaps.Register(surface,
            _groundY + (rejected ? (Fixed64)2 : -Fixed64.One),
            _groundY + (rejected ? (Fixed64)3 : Fixed64.One),
            priority: Workload == "Stacked64" ? ordinal % 3 : 0), "Heightmap registration failed.");
    }

    private void SimulateFrame(int frame)
    {
        // Real host mutation: alternate names so the cached preference genuinely becomes invalid.
        if (Workload == "Churn64" && frame % 16 == 0 && frame != 0)
        {
            Require(_fixture.Context.Heightmaps.Unregister(_alternateActive ? "alternate" : "layer-0"),
                "Churn removal failed.");
            _alternateActive = !_alternateActive;
            Register(_alternateActive ? _alternate : _surfaces[0], 0);
        }
        _fixture.Context.Simulate();
        foreach (GroundedNavigator agent in _agents)
        {
            agent.GroundingTicks = 0;
            agent.Calls = agent.Fallbacks = 0;
            agent.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Slow);
            agent.Simulate();
            agent.CommitFrameMotion();
        }
        _fixture.Context.LateSimulate();
    }

    [Benchmark]
    public long Simulate64Frames()
    {
        for (int frame = 0; frame < Frames; frame++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            SimulateFrame(frame + PreparationFrames);
            _ticks[frame] = Stopwatch.GetTimestamp() - started;
            _allocations[frame] = GC.GetAllocatedBytesForCurrentThread() - before;
            _collections[frame] = (GC.CollectionCount(0) != g0 ? 1 : 0)
                | (GC.CollectionCount(1) != g1 ? 2 : 0) | (GC.CollectionCount(2) != g2 ? 4 : 0);
            _groundingTicks[frame] = 0;
            _calls[frame] = _fallbacks[frame] = 0;
            foreach (GroundedNavigator agent in _agents)
            {
                _groundingTicks[frame] += agent.GroundingTicks;
                _calls[frame] += agent.Calls;
                _fallbacks[frame] += agent.Fallbacks;
            }
            _hashes[frame] = ValidateAndHash();
            Require(!_hasReplay || _hashes[frame] == _expectedHashes[frame], "Grounded frame replay diverged.");
        }
        return _hashes[Frames - 1];
    }

    private long ValidateAndHash()
    {
        long hash = 1_469_598_103_934_665_603L;
        for (int i = 0; i < _agents.Length; i++)
        {
            GroundedNavigator agent = _agents[i];
            NavigationCommittedCellState? cell = agent.LastCommittedCell;
            Require(agent.Position.X > agent.LastPosition.X && agent.Position.Y == _groundY
                && agent.Velocity.Y == Fixed64.Zero && cell.HasValue
                && cell.Value.SimulationFrame == _fixture.Context.FrameCount
                && cell.Value.Address.MapId == MapId && cell.Value.Address.Index.y == 0
                && cell.Value.Medium == TraversalMedium.Solid, "Invalid grounded motion/occupancy.");
            Require(NavigatorOccupancyTracker.TryResolveVoxel(_fixture.World, agent.Position, out _, out Voxel voxel)
                && voxel.Index == cell.Value.Address.Index
                && GridOccupantManager.TryGetOccupancyTicket(_fixture.World, agent, voxel.WorldIndex, out _),
                "Committed cell lacks an actual occupancy ticket.");
            Require(agent.Position.X >= (Fixed64)voxel.Index.x - Fixed64.Half
                && agent.Position.X <= (Fixed64)voxel.Index.x + Fixed64.Half
                && agent.Position.Z >= (Fixed64)voxel.Index.z - Fixed64.Half
                && agent.Position.Z <= (Fixed64)voxel.Index.z + Fixed64.Half,
                "Committed cell disagrees with the authored unit rectangle.");
            Require(NavigatorOccupancyTracker.TryResolveVoxel(_fixture.World, agent.LastPosition,
                out _, out Voxel oldVoxel) && (oldVoxel == voxel
                || !GridOccupantManager.TryGetOccupancyTicket(_fixture.World, agent, oldVoxel.WorldIndex, out _)),
                "Old-cell occupancy survived transfer.");
            if (Workload == "Crossing64")
            {
                int stripe = _expectedStripes[i];
                if (stripe < 0 || agent.Position.X < (Fixed64)stripe - Fixed64.Half
                    || agent.Position.X > (Fixed64)stripe + Fixed64.Half)
                {
                    Fixed64 offset = agent.Position.X + Fixed64.Half;
                    stripe = offset.FloorToInt();
                    // At an inclusive shared edge, the lower registration order wins fallback.
                    if (stripe > 0 && offset == (Fixed64)stripe)
                        stripe--;
                    _expectedStripes[i] = stripe;
                }
            }
            string expected = Workload switch
            {
                "Disabled" or "Empty" or "Missing16" or "Missing64" or "YRejected64" => null,
                "Stacked64" => "layer-2",
                "Churn64" => _alternateActive ? "alternate" : "layer-0",
                "Crossing64" => _surfaces[_expectedStripes[i]].Name,
                _ => "layer-0"
            };
            Require(agent.HeightmapGrounding.ActiveLayerName == expected, "Wrong selected heightmap layer.");
            hash = Fold(hash, agent.Position.X.m_rawValue);
            hash = Fold(hash, agent.Position.Y.m_rawValue);
            hash = Fold(hash, agent.Position.Z.m_rawValue);
            hash = Fold(hash, agent.Velocity.X.m_rawValue);
            hash = Fold(hash, cell.Value.Address.Index.x);
            hash = Fold(hash, cell.Value.Address.Index.z);
            // String.GetHashCode is process-dependent; fold characters for cross-launch replay.
            if (expected != null)
                foreach (char c in expected)
                    hash = Fold(hash, c);
        }
        return hash;
    }

    private static long Fold(long hash, long value) => unchecked((hash ^ value) * 1_099_511_628_211L);
    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }

    [IterationCleanup]
    public void Export() => Console.WriteLine("HEIGHTMAP_FRAME_SAMPLES " + JsonSerializer.Serialize(new
    {
        process = Environment.ProcessId, iteration = ++_iteration, workload = Workload, agents = AgentCount,
        profile = Profile, layers = _layerCount, frameRate = 32, preparationFrames = PreparationFrames,
        stopwatchFrequency = Stopwatch.Frequency, ticks = _ticks, allocatedBytes = _allocations,
        collectionMasks = _collections, groundingTicks = _groundingTicks, calls = _calls,
        fallbacks = _fallbacks, hashes = _hashes, firstFrameTicks = _firstFrameTicks,
        firstFrameAllocation = _firstFrameAllocation, registrationTicks = _registrationTicks,
        registrationAllocation = _registrationAllocation, finalFirstXRaw = _agents[0].Position.X.m_rawValue
    }));

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_agents != null)
            foreach (GroundedNavigator agent in _agents)
            {
                agent.Reset();
                Require(!System.Linq.Enumerable.Any(GridOccupantManager.GetOccupiedIndices(_fixture.World, agent)),
                    "Reset retained occupancy.");
            }
        _fixture?.Teardown();
    }

    internal static int RunCapture(int launch, bool profile)
    {
        using Process process = Process.GetCurrentProcess();
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        Console.WriteLine($"HEIGHTMAP_CAPTURE launch={launch} profile={profile} priority=BelowNormal");
        Console.WriteLine("HEIGHTMAP_RUNTIME " + JsonSerializer.Serialize(new
        {
            benchmark = typeof(NavigationHeightmapFrameBenchmarks).Assembly.Location,
            trailblazer = typeof(Navigator).Assembly.Location,
            gridForge = typeof(GridForge.Grids.GridWorld).Assembly.Location,
            fixedMathSharp = typeof(Fixed64).Assembly.Location,
            swiftCollections = typeof(SwiftShortArray2D).Assembly.Location,
            runtime = Environment.Version.ToString(), stopwatchFrequency = Stopwatch.Frequency
        }));
        string[] orderedWorkloads = (string[])Workloads.Clone();
        if (launch % 2 == 0)
            Array.Reverse(orderedWorkloads);
        foreach (int count in new[] { 100, 500 })
        foreach (string workload in orderedWorkloads)
        {
            var scenario = new NavigationHeightmapFrameBenchmarks
            { AgentCount = count, Workload = workload, Profile = profile };
            try
            {
                scenario.Setup();
                var warmupClock = Stopwatch.StartNew();
                for (int warmup = 0; warmup < 4 || warmupClock.ElapsedMilliseconds < 300; warmup++)
                {
                    scenario.Prepare();
                    scenario.Simulate64Frames();
                }
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    scenario.Prepare();
                    scenario.Simulate64Frames();
                    scenario.Export();
                }
            }
            finally
            {
                scenario.Cleanup();
            }
        }
        return 0;
    }

    private sealed class GroundedNavigator : BenchmarkNavigator
    {
        internal GroundedNavigator(TrailblazerWorldContext context, Vector3d foot,
            NavigationAgentProfile profile, int index) : base(context, foot, profile, index) { }

        internal bool Profile;
        internal long GroundingTicks;
        internal int Calls, Fallbacks;

        public override void CheckTrekCondition()
        {
            base.CheckTrekCondition();
            string previous = HeightmapGrounding.ActiveLayerName;
            long started = Profile ? Stopwatch.GetTimestamp() : 0;
            bool sampled = TryApplyHeightmapGrounding();
            if (Profile)
                GroundingTicks += Stopwatch.GetTimestamp() - started;
            Calls++;
            if (HeightmapGrounding.Mode != HeightmapGroundingMode.Disabled
                && (!sampled || previous == null || previous != HeightmapGrounding.ActiveLayerName))
                Fallbacks++;
        }
    }
}
