using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures the production movement-group owner inside complete guided controller frames.</summary>
[MemoryDiagnoser]
[Config(typeof(PerformanceGateConfig))]
public class NavigationMovementGroupFrameBenchmarks
{
    private const int Frames = 16;
    private const int PreparationFrames = 4;
    private const string MapId = "movement-group-frame";
    private static readonly string[] Workloads =
        { "Ungrouped", "Small8", "Large", "Sparse8x16", "SparseLargex4", "Churn8", "ChurnLarge" };
    private static object _allocationSink;
    private static long _sessionAllocationBytes;
    private readonly long[] _ticks = new long[Frames], _bytes = new long[Frames], _hashes = new long[Frames];
    private readonly long[] _expected = new long[Frames];
    private readonly int[] _collections = new int[Frames];
    private BenchmarkPathFixture _fixture;
    private BenchmarkNavigator[] _agents;
    private MovementGroupSession[] _membershipProbes;
    private PathQuery[] _queries;
    private long[] _movement;
    private PathQuery _template;
    private int _groupSize, _peakFactor, _iteration;
    private bool _hasReplay;
    private long _joinTicks, _joinBytes;

    [Params(100, 500)]
    public int AgentCount { get; set; }
    [ParamsSource(nameof(Cases))]
    public string Workload { get; set; }
    public static string[] Cases => Workloads;

    [GlobalSetup]
    public void Setup()
    {
        if (_sessionAllocationBytes == 0)
            _sessionAllocationBytes = SessionAllocationSize();
        var configuration = NavigationGraphBenchmarkScenario.CreateConfiguration(100, 100);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(configuration, NavigationGraphBenchmarkScenario.CreateSettings(10_000, 1,
            cacheEntries: 4, activeLeaseCapacity: AgentCount));
        _fixture.Context.SetFrameRate(32);
        PathQuery published = NavigationGraphBenchmarkScenario.Publish(_fixture, configuration, MapId, 100, 100,
            new NavigationWorkBudget(10_240_000, 16, 10_000, 320_000, 0, 0, 0, 0, 160_000, 320_000, 0));
        _template = new PathQuery(published.Start,
            new NavigationEndpoint(new Vector3d((Fixed64)70, published.Start.Position.Y, (Fixed64)50), MapId),
            published.Agent, published.AreaPolicy, published.Traversal, PathAlgorithm.FlowField,
            published.Budget, allowTransitions: false);
        _groupSize = Workload.Contains('8') ? 8 : AgentCount;
        _peakFactor = Workload == "Sparse8x16" ? 16 : Workload == "SparseLargex4" ? 4 : 1;
        _agents = new BenchmarkNavigator[AgentCount];
        _membershipProbes = new MovementGroupSession[AgentCount];
        _queries = new PathQuery[AgentCount];
        _movement = new long[AgentCount];
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i] = new BenchmarkNavigator(_fixture.Context,
                published.Start.Position + new Vector3d(2 + i % 25, 0, 2 + i / 25 * 2), published.Agent, i);
            _membershipProbes[i] = new MovementGroupSession { GroupId = GroupId(i) };
        }
        Prepare();
        Simulate16Frames();
        Array.Copy(_hashes, _expected, Frames);
        _hasReplay = true;
    }

    private int GroupId(int i) => Workload == "Ungrouped" ? -1 : i / _groupSize;

    private PathQuery QueryFor(BenchmarkNavigator agent) => _template.WithStartState(agent.FootPosition, TraversalMedium.Solid);

    [IterationSetup]
    public void Prepare()
    {
        foreach (BenchmarkNavigator agent in _agents)
            agent.Reset();
        _fixture.Context.Navigation.MovementGroups.Reset();
        long before = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        // Padding is admitted before real agents and removed only after they join. This leaves
        // prefix holes at the same high-water mark without retaining a second live membership owner.
        var padding = new System.Collections.Generic.List<MovementGroupSession>();
        if (_peakFactor > 1)
            for (int group = 0; group * _groupSize < AgentCount; group++)
                for (int j = 0; j < Math.Min(_groupSize, AgentCount - group * _groupSize) * (_peakFactor - 1); j++)
                {
                    var session = new MovementGroupSession { GroupId = group };
                    _fixture.Context.Navigation.MovementGroups.UpdateTarget(session, _template.End.Position,
                        _template.Start.Position, _template.Agent.Shape.Radius);
                    padding.Add(session);
                }
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            agent.PrepareFrame();
            agent.Steering.GroupFactor = Fixed64.Zero;
            agent.Steering.ConfigurePathRechecks(16, i % 16);
            _queries[i] = QueryFor(agent);
            agent.ApplyGuidedTrekRequest(_queries[i], Navigation.TrekRate.Slow, groupId: GroupId(i));
        }
        foreach (MovementGroupSession session in padding)
            _fixture.Context.Navigation.MovementGroups.Remove(session);
        _joinTicks = Stopwatch.GetTimestamp() - started;
        _joinBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            SimulateFrame(frame);
            ValidateAndHash();
        }
    }

    private void SimulateFrame(int frame)
    {
        if (Workload.StartsWith("Churn", StringComparison.Ordinal))
            for (int group = 0; group * _groupSize < AgentCount; group++)
            {
                int size = Math.Min(_groupSize, AgentCount - group * _groupSize);
                int i = group * _groupSize + frame % Math.Max(1, size - 1);
                _agents[i].Steering.LeaveMovementGroup();
                _queries[i] = QueryFor(_agents[i]);
                _agents[i].ApplyGuidedTrekRequest(_queries[i], Navigation.TrekRate.Slow, groupId: group);
            }
        _fixture.Context.Simulate();
        foreach (BenchmarkNavigator agent in _agents)
        {
            agent.Simulate();
            agent.CommitFrameMotion();
        }
        _fixture.Context.LateSimulate();
    }

    [Benchmark]
    public long Simulate16Frames()
    {
        Array.Clear(_movement);
        for (int frame = 0; frame < Frames; frame++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            SimulateFrame(frame + PreparationFrames);
            _ticks[frame] = Stopwatch.GetTimestamp() - started;
            _bytes[frame] = GC.GetAllocatedBytesForCurrentThread() - before;
            _collections[frame] = (GC.CollectionCount(0) != g0 ? 1 : 0)
                | (GC.CollectionCount(1) != g1 ? 2 : 0) | (GC.CollectionCount(2) != g2 ? 4 : 0);
            _hashes[frame] = ValidateAndHash();
            Require(!_hasReplay || _hashes[frame] == _expected[frame], "Movement-group replay diverged.");
        }
        foreach (long movement in _movement)
            Require(movement > 0, "A guided controller never moved during the measured block.");
        return _hashes[Frames - 1];
    }

    private long ValidateAndHash()
    {
        long hash = 1_469_598_103_934_665_603L;
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            var cell = agent.LastCommittedCell;
            bool valid = agent.IsGuided && agent.Steering.ShouldMove
                && agent.Position.Y == _template.Start.Position.Y && cell.HasValue
                && cell.Value.SimulationFrame == _fixture.Context.FrameCount
                && cell.Value.Address.MapId == MapId && cell.Value.Medium == TraversalMedium.Solid;
            if (!valid)
                throw new InvalidOperationException($"Group motion: workload={Workload}, agent={i}, "
                    + $"frame={_fixture.Context.FrameCount}, guided={agent.IsGuided}, shouldMove={agent.Steering.ShouldMove}, "
                    + $"position={agent.Position}, last={agent.LastPosition}, status={agent.Steering.LastAcquisitionStatus}, cell={cell?.Address}.");
            Require(Navigation.NavigatorOccupancyTracker.TryResolveVoxel(_fixture.World, agent.Position,
                out _, out Voxel voxel) && voxel.Index == cell.Value.Address.Index
                && GridOccupantManager.TryGetOccupancyTicket(_fixture.World, agent, voxel.WorldIndex, out _),
                "Missing actual occupancy.");
            Require(agent.Steering.MovementGroupID == GroupId(i), "Group identity changed.");
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            Require(_fixture.Context.Navigation.MovementGroups.IsNeighbor(_membershipProbes[i], agent.GlobalId,
                _template.End.Position, _fixture.Context.FrameCount) == (GroupId(i) >= 0),
                "Missing/falsely retained group membership.");
            hash = Fold(hash, agent.Position.X.m_rawValue);
            hash = Fold(hash, agent.Position.Z.m_rawValue);
            hash = Fold(hash, agent.Velocity.X.m_rawValue);
            hash = Fold(hash, agent.Velocity.Z.m_rawValue);
            hash = Fold(hash, agent.Steering.Destination.X.m_rawValue);
            hash = Fold(hash, agent.Steering.Destination.Z.m_rawValue);
            hash = Fold(hash, cell.Value.Address.Index.x);
            hash = Fold(hash, cell.Value.Address.Index.z);
        }
        return hash;
    }

    private static long Fold(long hash, long value) => unchecked((hash ^ value) * 1_099_511_628_211L);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Reflection stays outside timing and reads the actual runtime arrays in either frozen arm.
    private long BackingArrayBytes()
    {
        var groups = (SwiftDictionary<int, MovementGroupState>)typeof(MovementGroupCoordinatorState)
            .GetField("_movementGroups", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_fixture.Context.Navigation.MovementGroups);
        long bytes = 0;
        foreach (var pair in groups)
        {
            object group = pair.Value;
            object members = group.GetType().GetProperty("Members").GetValue(group);
            bytes += ArrayBytes(group) + ArrayBytes(members);
        }
        return bytes;
    }

    private static long ArrayBytes(object owner)
    {
        long bytes = 0;
        foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.GetValue(owner) is Array array)
            {
                Type element = array.GetType().GetElementType();
                int size = (int)typeof(NavigationMovementGroupFrameBenchmarks).GetMethod(nameof(ElementSize),
                    BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(element).Invoke(null, null);
                bytes += array.LongLength * size;
            }
        return bytes;
    }
    private static int ElementSize<T>() => Unsafe.SizeOf<T>();

    private static long SessionAllocationSize()
    {
        var sessions = new MovementGroupSession[4_096];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < sessions.Length; i++) sessions[i] = new MovementGroupSession();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _allocationSink = sessions;
        return bytes / sessions.Length;
    }

    private static void VerifySaturatedOrder()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var coordinator = context.Navigation.MovementGroups;
        var sessions = new MovementGroupSession[4];
        Fixed64[] x = { Fixed64.MaxValue, Fixed64.Zero, Fixed64.MaxValue, -Fixed64.MaxValue };
        for (int i = 0; i < sessions.Length; i++)
        {
            sessions[i] = new MovementGroupSession { GroupId = 17 };
            coordinator.UpdateTarget(sessions[i], Vector3d.Zero,
                new Vector3d(x[i], Fixed64.Zero, Fixed64.Zero), Fixed64.Zero);
        }
        coordinator.Remove(sessions[1]);
        var groups = (SwiftDictionary<int, MovementGroupState>)typeof(MovementGroupCoordinatorState)
            .GetField("_movementGroups", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
        object[] arguments = { groups[17], Vector3d.Zero, context.FrameCount - 1, Vector3d.Zero, Fixed64.Zero, 0 };
        bool valid = (bool)typeof(MovementGroupCoordinatorState).GetMethod("TryGetFormationMetrics",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(coordinator, arguments);
        Require(valid && (Vector3d)arguments[3] == Vector3d.Zero && (int)arguments[5] == 3,
            "Saturated formation sum lost baseline slot order after non-tail removal.");
        Console.WriteLine("MOVEMENT_GROUP_ORDER_CONTROL center=0 members=3 passed=true");
    }

    [IterationCleanup]
    public void Export() => Console.WriteLine("MOVEMENT_GROUP_SAMPLES " + JsonSerializer.Serialize(new
    {
        process = Environment.ProcessId, iteration = ++_iteration, workload = Workload, agents = AgentCount,
        frames = Frames, preparationFrames = PreparationFrames, frameRate = 32, stopwatchFrequency = Stopwatch.Frequency,
        ticks = _ticks, allocatedBytes = _bytes, collectionMasks = _collections, hashes = _hashes,
        joinTicks = _joinTicks, joinBytes = _joinBytes, backingArrayElementBytes = BackingArrayBytes(),
        sessionAllocationBytes = _sessionAllocationBytes
    }));

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_agents != null)
            foreach (BenchmarkNavigator agent in _agents) agent.Reset();
        if (_fixture != null)
        {
            Require(_fixture.Context.Navigation.MovementGroups.IsEmpty, "Groups survived controller reset.");
            _fixture.Teardown();
        }
    }

    internal static int RunCapture(int launch, string selection)
    {
        using Process process = Process.GetCurrentProcess();
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        _sessionAllocationBytes = SessionAllocationSize();
        VerifySaturatedOrder();
        Console.WriteLine($"MOVEMENT_GROUP_RUNTIME launch={launch} benchmark={typeof(NavigationMovementGroupFrameBenchmarks).Assembly.Location} core={typeof(Navigation.Navigator).Assembly.Location}");
        string[] workloads = (string[])Workloads.Clone();
        if (launch % 2 == 0) Array.Reverse(workloads);
        foreach (int count in new[] { 100, 500 })
        foreach (string workload in workloads)
        {
            if (selection != null && selection != workload) continue;
            var scenario = new NavigationMovementGroupFrameBenchmarks { AgentCount = count, Workload = workload };
            try
            {
                scenario.Setup();
                var warmup = Stopwatch.StartNew();
                for (int block = 0; block < 3 || warmup.ElapsedMilliseconds < 750; block++)
                {
                    scenario.Prepare();
                    scenario.Simulate16Frames();
                }
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    scenario.Prepare();
                    scenario.Simulate16Frames();
                    scenario.Export();
                }
            }
            finally { scenario.Cleanup(); }
        }
        return 0;
    }
}
