//=======================================================================
// NavigationGuidedFrameBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Chronicler;
using Chronicler.Serialization;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using Chronicler.Diagnostics;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures sustained headless guide following, retaining each complete host-frame observation.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Guidance", "Frame")]
public class NavigationGuidedFrameBenchmarks
{
    private const int Width = 100;
    private const int Length = 100;
    private const int MaximumAgentCount = 500;
    private const int PreparationFrames = 32;
    private const int MeasuredFrames = 64;
    private const string MapId = "guided-frame";
    private readonly long[] _elapsedTicks = new long[MeasuredFrames];
    private readonly long[] _allocatedBytes = new long[MeasuredFrames];
    private readonly int[] _collections = new int[MeasuredFrames];
    private readonly long[] _checksums = new long[MeasuredFrames];
    private readonly long[] _expectedChecksums = new long[MeasuredFrames];
    private readonly int?[] _phaseAssignments = new int?[MaximumAgentCount];
    private readonly int[] _preparationRayCounts = new int[PreparationFrames];
    private readonly int[] _periodicRayCounts = new int[MeasuredFrames];
    private readonly byte[] _preparationRayObservations =
        new byte[PreparationFrames * MaximumAgentCount];
    private readonly byte[] _periodicRayObservations =
        new byte[MeasuredFrames * MaximumAgentCount];
    private BenchmarkPathFixture _fixture;
    private BenchmarkNavigator[] _agents;
    private PathQuery[] _queries;
    private NavigationMap _publishedMap;
    private PathAlgorithm _algorithm;
    private long[] _movement;
    private bool _hasExpectedReplay;
    private int _iteration;
    private int _frames;
    private string _mode;
    private long _clockOrigin;
    private int _mandatoryRayCount;
    private long _firstAdmissionTicks;
    private long _firstAdmissionAllocatedBytes;
    private int _firstAdmissionCollectionMask;

    internal static int RunPhaseSpreadingSmoke()
    {
        var benchmark = new NavigationGuidedFrameBenchmarks
        {
            AgentCount = 100,
            Algorithm = "FlowField"
        };
        try
        {
            benchmark.Setup("BalancedPhases");
            benchmark.RunBoundedLifecycleSmoke();
            return 0;
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    [Params(100, 500)]
    public int AgentCount { get; set; }

    [Params("AStar", "FlowField")]
    public string Algorithm { get; set; }

    [GlobalSetup(Target = nameof(Simulate64GuidedFrames))]
    public void SetupOriginal() => Setup(null);

    [GlobalSetup(Target = nameof(Simulate64GuidedFramesInstrumentedUnphased))]
    public void SetupInstrumentedUnphased() => Setup("Unphased");

    [GlobalSetup(Target = nameof(Simulate64GuidedFramesInstrumentedBalancedPhases))]
    public void SetupInstrumentedBalancedPhases() => Setup("BalancedPhases");

    [GlobalSetup(Target = nameof(Simulate64GuidedFramesInstrumentedUnbalancedPhase))]
    public void SetupInstrumentedUnbalancedPhase() => Setup("UnbalancedPhase");

    private void Setup(string mode)
    {
        _mode = mode;
        _algorithm = Enum.Parse<PathAlgorithm>(Algorithm);
        GridConfiguration configuration = NavigationGraphBenchmarkScenario.CreateConfiguration(Width, Length);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(configuration, NavigationGraphBenchmarkScenario.CreateSettings(
            Width * Length, concurrentQueries: 1, cacheEntries: AgentCount, activeLeaseCapacity: AgentCount));
        _fixture.Context.SetFrameRate(32);
        ChronicleGuard.ThrowIfTrue(!configuration.TryNormalize(out NormalizedGridConfiguration binding),
            "Guided frame grid configuration is invalid.");
        NavigationAreaPolicyKey policy = PublishMap(binding);
        Vector3d destination = NavigationGraphBenchmarkScenario.GetFoot(binding, new VoxelIndex(98, 0, 50));
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None);
        // Real ray capacity is essential: the wall, not budget exhaustion, must prevent direct travel.
        var budget = new NavigationWorkBudget(
            maxLookupProbes: 10_240_000, maxEndpointCandidates: 16,
            maxExpandedNodes: Width * Length, maxEvaluatedEdges: 320_000,
            maxConnectionLegs: 0, maxTransitionCandidates: 0, maxTransitionPairs: 0,
            maxStagedLegAttempts: 0, maxTraceIntervals: 160_000,
            maxCoveredVoxelIntervals: 320_000, maxSimplificationRays: 1_024);
        _agents = new BenchmarkNavigator[AgentCount];
        _queries = new PathQuery[AgentCount];
        _movement = new long[AgentCount];
        for (int i = 0; i < AgentCount; i++)
        {
            Vector3d foot = NavigationGraphBenchmarkScenario.GetFoot(binding,
                new VoxelIndex(2 + i % 25, 0, 2 + i / 25 * 2));
            _agents[i] = new BenchmarkNavigator(_fixture.Context, foot, profile, i);
            _queries[i] = new PathQuery(
                new NavigationEndpoint(foot, MapId), new NavigationEndpoint(destination, MapId),
                profile, policy, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
                _algorithm, budget, allowTransitions: false);
            NavigationWorkMeter meter = _fixture.Context.Pathing.ImmediateRayWorkspace.WorkMeter;
            meter.Reset(default);
            NavigationRayStatus direct = _fixture.Context.Guides.TryGetDirectHeading(_queries[i], foot, out _);
            ChronicleGuard.ThrowIfTrue(
                direct != NavigationRayStatus.Blocked || meter.TraceIntervals <= 0 || meter.IsGuideSampleBridge,
                $"Guided frame direct-ray preflight failed for agent {i}: status={direct}, "
                + $"traceIntervals={meter.TraceIntervals}, guideSampleBridge={meter.IsGuideSampleBridge}.");
        }

        // Exercise the real lifecycle before timing. Later invocations must replay every frame exactly.
        Prepare();
        if (_mode == null)
            Simulate64GuidedFrames();
        else
            Simulate64GuidedFramesInstrumented();
        Array.Copy(_checksums, _expectedChecksums, MeasuredFrames);
        _hasExpectedReplay = true;
        ReleaseAgentsAndValidateDrained();
    }

    private NavigationAreaPolicyKey PublishMap(NormalizedGridConfiguration binding)
    {
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, (Fixed64)4, (Fixed64)4);
        var builder = new NavigationMapBuilder(MapId, binding);
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Length; z++)
            {
                // A five-cell opening joins the two sides; agents begin far from that opening.
                if (x != 50 || z >= 95)
                    builder.AddCell(new VoxelIndex(x, 0, z), cell);
            }
        }
        _publishedMap = builder.Build();
        var map = new NavigationMapCommitOperation(
            new PreparedNavigationMap(_publishedMap, bakeVersion: 1), OverlayReplacementPolicy.Clear,
            operationSequence: 1, effectiveFrame: _fixture.Context.FrameCount + 1);
        var key = new NavigationAreaPolicyKey(MapId, revision: 1);
        var policy = new NavigationAreaPolicyCommitOperation(new NavigationAreaPolicy(key,
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2, effectiveFrame: _fixture.Context.FrameCount + 1);
        ChronicleGuard.ThrowIfTrue(!_fixture.Context.Pathing.Admit(map) || !_fixture.Context.Pathing.Admit(policy),
            "Guided frame map or policy was not admitted.");
        for (int frame = 0; frame < 4_096
            && (map.Receipt.Status == NavigationOperationStatus.Pending
                || policy.Receipt.Status == NavigationOperationStatus.Pending); frame++)
        {
            _fixture.Context.Simulate();
        }
        ChronicleGuard.ThrowIfTrue(map.Receipt.Status != NavigationOperationStatus.Applied
            || policy.Receipt.Status != NavigationOperationStatus.Applied, "Guided frame publication failed.");
        return key;
    }

    [IterationSetup]
    public void Prepare()
    {
        // Release all old occupancy before registering the identical new starting formation.
        ReleaseAgentsAndValidateDrained();
        _frames = 0;
        Array.Clear(_movement);
        Array.Clear(_preparationRayCounts);
        Array.Clear(_periodicRayCounts);
        Array.Clear(_preparationRayObservations);
        Array.Clear(_periodicRayObservations);
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i].PrepareFrame();
            if (_mode != null)
            {
                int? phase = _mode switch
                {
                    "BalancedPhases" => i % 16,
                    "UnbalancedPhase" => 0,
                    _ => null
                };
                _phaseAssignments[i] = phase;
                _agents[i].Steering.ConfigurePathRechecks(16, phase);
            }
            _agents[i].ApplyGuidedTrekRequest(_queries[i], TrekRate.Slow);
        }
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            if (_mode == null)
            {
                SimulateAgents();
            }
            else if (frame == 0)
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);
                long started = Stopwatch.GetTimestamp();
                _preparationRayCounts[frame] = SimulateAgentsInstrumented(
                    _preparationRayObservations,
                    frame);
                _firstAdmissionTicks = Stopwatch.GetTimestamp() - started;
                _firstAdmissionAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                _firstAdmissionCollectionMask = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                    | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                    | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            }
            else
            {
                _preparationRayCounts[frame] = SimulateAgentsInstrumented(
                    _preparationRayObservations,
                    frame);
            }
            ValidateAgentFrame(frame);
        }
        if (_mode != null)
        {
            _mandatoryRayCount = _preparationRayCounts[0];
            _clockOrigin = checked(_fixture.Context.FrameCount + 1);
            ValidateRaySchedule(
                _preparationRayCounts,
                _preparationRayObservations,
                preparation: true);
        }
        ValidateActiveGuides();
    }

    /// <summary>The BenchmarkDotNet operation is a complete 64-frame block, not a single frame.</summary>
    [Benchmark]
    public long Simulate64GuidedFrames()
    {
        long checksum = 1_469_598_103_934_665_603L;
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            SimulateAgents();
            _elapsedTicks[frame] = Stopwatch.GetTimestamp() - started;
            _allocatedBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _collections[frame] = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            _frames = frame + 1;

            checksum = ValidateAndHashFrame(frame, checksum);
        }
        return checksum;
    }

    [Benchmark]
    public long Simulate64GuidedFramesInstrumentedUnphased() =>
        Simulate64GuidedFramesInstrumented();

    [Benchmark]
    public long Simulate64GuidedFramesInstrumentedBalancedPhases() =>
        Simulate64GuidedFramesInstrumented();

    [Benchmark]
    public long Simulate64GuidedFramesInstrumentedUnbalancedPhase() =>
        Simulate64GuidedFramesInstrumented();

    private long Simulate64GuidedFramesInstrumented()
    {
        long checksum = 1_469_598_103_934_665_603L;
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            _periodicRayCounts[frame] = SimulateAgentsInstrumented(
                _periodicRayObservations,
                frame);
            _elapsedTicks[frame] = Stopwatch.GetTimestamp() - started;
            _allocatedBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _collections[frame] = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            _frames = frame + 1;

            checksum = ValidateAndHashFrame(frame, checksum);
        }
        ValidateRaySchedule(_periodicRayCounts, _periodicRayObservations, preparation: false);
        return checksum;
    }

    private long ValidateAndHashFrame(int frame, long checksum)
    {
        // Semantic checks and replay hashing stay outside each containing-frame timing bracket.
        ValidateAgentFrame(frame);
        checksum = Fold(checksum, frame);
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            checksum = Fold(checksum, agent.Position.X.m_rawValue);
            checksum = Fold(checksum, agent.Position.Y.m_rawValue);
            checksum = Fold(checksum, agent.Position.Z.m_rawValue);
            checksum = Fold(checksum, agent.Velocity.X.m_rawValue);
            checksum = Fold(checksum, agent.Velocity.Y.m_rawValue);
            checksum = Fold(checksum, agent.Velocity.Z.m_rawValue);
            checksum = Fold(checksum, agent.LastCommittedCell.Value.Address.Index.x);
            checksum = Fold(checksum, agent.LastCommittedCell.Value.Address.Index.z);
        }
        _checksums[frame] = checksum;
        if (_hasExpectedReplay && checksum != _expectedChecksums[frame])
            ChronicleGuard.ThrowIfTrue(true, $"Guided frame replay diverged at relative frame {frame}.");
        return checksum;
    }

    private static long Fold(long checksum, long value) => unchecked((checksum ^ value) * 1_099_511_628_211L);

    private void SimulateAgents()
    {
        _fixture.Context.Simulate();
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i].Simulate();
            _agents[i].CommitFrameMotion();
        }
    }

    private int SimulateAgentsInstrumented(byte[] observations = null, int frame = 0)
    {
        _fixture.Context.Simulate();
        NavigationWorkMeter meter = _fixture.Context.Pathing.ImmediateRayWorkspace.WorkMeter;
        int rayCount = 0;
        for (int i = 0; i < AgentCount; i++)
        {
            meter.Reset(default);
            _agents[i].Simulate();
            ChronicleGuard.ThrowIfTrue(
                meter.IsGuideSampleBridge,
                $"Guided frame Flow recovery ray contaminated direct LOS attribution for agent {i}.");
            bool observed = meter.TraceIntervals > 0;
            if (observations != null)
                observations[frame * MaximumAgentCount + i] = observed ? (byte)1 : (byte)0;
            if (observed)
                rayCount++;
            _agents[i].CommitFrameMotion();
        }
        return rayCount;
    }

    private void ValidateRaySchedule(int[] counts, byte[] observations, bool preparation)
    {
        int length = preparation ? PreparationFrames : MeasuredFrames;
        int total = 0;
        for (int frame = 0; frame < length; frame++)
        {
            long logicalFrame = preparation
                ? _clockOrigin - PreparationFrames + frame
                : _clockOrigin + frame;
            int remainder = (int)(logicalFrame % 16);
            int expected = 0;
            int observed = 0;
            for (int agent = 0; agent < AgentCount; agent++)
            {
                bool expectedAgent = preparation && frame == 0
                    || _mode == "Unphased" && frame % 16 == 0
                    || _mode != "Unphased" && _phaseAssignments[agent] == remainder;
                bool observedAgent = observations[frame * MaximumAgentCount + agent] != 0;
                if (expectedAgent)
                    expected++;
                if (observedAgent)
                    observed++;
                ChronicleGuard.ThrowIfTrue(
                    observedAgent != expectedAgent,
                    $"Guided frame {_mode} {(preparation ? "preparation" : "measured")} per-agent LOS schedule "
                    + $"mismatch at frame {frame}, agent {agent}: expected={expectedAgent}, actual={observedAgent}.");
            }
            ChronicleGuard.ThrowIfTrue(
                counts[frame] != observed || observed != expected,
                $"Guided frame {_mode} {(preparation ? "preparation" : "measured")} LOS schedule "
                + $"mismatch at frame {frame}: expected={expected}, observed={observed}, actual={counts[frame]}.");
            if (!preparation)
                total += observed;
        }
        if (!preparation)
        {
            ChronicleGuard.ThrowIfTrue(
                total != AgentCount * 4,
                $"Guided frame {_mode} measured {total} periodic rays instead of {AgentCount * 4}.");
            for (int agent = 0; agent < AgentCount; agent++)
            {
                int agentTotal = 0;
                for (int frame = 0; frame < MeasuredFrames; frame++)
                    agentTotal += observations[frame * MaximumAgentCount + agent];
                ChronicleGuard.ThrowIfTrue(
                    agentTotal != 4,
                    $"Guided frame {_mode} agent {agent} measured {agentTotal} periodic rays instead of 4.");
            }
        }
    }

    private void RunBoundedLifecycleSmoke()
    {
        Console.WriteLine("NAVIGATION_GUIDED_BURST_SAMPLES " + JsonSerializer.Serialize(new
        {
            scenario = "simultaneous-initial-requests",
            cacheState = "cold-guide-payloads-warmed-direct-ray-workspace",
            logicalFrame = _clockOrigin - PreparationFrames,
            ticks = _firstAdmissionTicks,
            allocatedBytes = _firstAdmissionAllocatedBytes,
            collectionMask = _firstAdmissionCollectionMask,
            rayCount = _mandatoryRayCount,
            activeGuides = AgentCount
        }));

        Prepare();
        for (int i = 0; i < AgentCount; i++)
            _agents[i].Steering.CanMove = false;
        for (int frame = 0; frame < 17; frame++)
        {
            int pausedRays = CaptureBurstFrame($"pause-{frame + 1:D2}");
            ChronicleGuard.ThrowIfTrue(pausedRays != 0, "Paused guided agents performed LOS work.");
        }
        for (int i = 0; i < AgentCount; i++)
            _agents[i].Steering.CanMove = true;
        int resumedRays = CaptureBurstFrame("resume-overdue");
        ChronicleGuard.ThrowIfTrue(
            resumedRays != AgentCount,
            $"Resumed guided agents performed {resumedRays} overdue checks instead of {AgentCount}.");
        int realignedTotal = 0;
        for (int frame = 0; frame < 16; frame++)
        {
            int rays = CaptureBurstFrame($"resume-realigned-{frame + 1:D2}");
            int expected = GetAssignedPhaseCount((int)(_fixture.Context.FrameCount % 16));
            ChronicleGuard.ThrowIfTrue(
                rays != expected,
                $"Resumed LOS schedule did not realign at frame {frame}: expected={expected}, actual={rays}.");
            realignedTotal += rays;
        }
        ChronicleGuard.ThrowIfTrue(
            realignedTotal != AgentCount,
            $"Resumed LOS schedule performed {realignedTotal} checks across one interval.");

        var rebake = new NavigationMapCommitOperation(
            new PreparedNavigationMap(_publishedMap, bakeVersion: 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 3,
            effectiveFrame: _fixture.Context.FrameCount + 1);
        ChronicleGuard.ThrowIfTrue(
            !_fixture.Context.Pathing.Admit(rebake),
            "Guided burst same-map rebake was not admitted.");
        int publicationRays = CaptureBurstFrame(
            "same-map-rebake-invalidation-and-ordered-reacquisition",
            rebake.Receipt);
        ChronicleGuard.ThrowIfTrue(
            rebake.Receipt.Status != NavigationOperationStatus.Applied
            || publicationRays != AgentCount || CountActiveGuides() != AgentCount - 1
            || CountZeroTargetDirections() != 1
            || _agents[0].Steering.TargetDirection != Vector3d.Zero
            || _phaseAssignments[0] == (int)(_fixture.Context.FrameCount % 16),
            "Same-map rebake did not invalidate the old guide proof in its containing frame.");
        for (int i = 0; i < AgentCount; i++)
        {
            var steering = _agents[i].Steering;
            PathQuery? retry = steering.CurrentQuery;
            ChronicleGuard.ThrowIfTrue(
                steering.HasLineOfSightPath
                || !steering.ShouldMove
                || !retry.HasValue
                || retry.Value.AreaPolicy != _queries[i].AreaPolicy
                || retry.Value.End != _queries[i].End
                || retry.Value.Algorithm != _queries[i].Algorithm,
                $"Same-map rebake did not preserve same-query retry intent for agent {i}.");
        }
        int recoveryRays = CaptureBurstFrame(
            "same-map-rebake-pending-retry-reacquisition",
            rebake.Receipt);
        int expectedRecoveryRays = 1
            + GetAssignedPhaseCount((int)(_fixture.Context.FrameCount % 16));
        ChronicleGuard.ThrowIfTrue(
            recoveryRays != expectedRecoveryRays || CountActiveGuides() != AgentCount
            || CountZeroTargetDirections() != 0,
            "Same-query retry did not perform direct checks and reacquire guidance.");

        var replacementProfile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.FromFraction(1, 5), Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter,
            TraversalMedia.Solid, TraversalCapability.None);
        for (int i = 0; i < AgentCount; i++)
        {
            NavigationProfileReplacementStatus status =
                _agents[i].TryReplaceNavigationProfile(replacementProfile);
            ChronicleGuard.ThrowIfTrue(
                status != NavigationProfileReplacementStatus.Applied
                || _agents[i].Steering.PathRecheckPhase != i % 16,
                $"Guided burst profile replacement failed for agent {i}: {status}.");
        }
        ChronicleGuard.ThrowIfTrue(
            CountActiveGuides() != 0,
            "Profile replacement retained old guide leases.");
        int replacementRays = CaptureBurstFrame("profile-replacement-admission");
        ChronicleGuard.ThrowIfTrue(
            replacementRays != AgentCount || CountActiveGuides() != AgentCount,
            "Profile replacement delayed forced re-admission or failed to reacquire guidance.");

        long populationAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int populationGen0 = GC.CollectionCount(0);
        int populationGen1 = GC.CollectionCount(1);
        int populationGen2 = GC.CollectionCount(2);
        long populationStarted = Stopwatch.GetTimestamp();
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator source = _agents[i];
            string payload = JsonRecordSerializer.Serialize(source);
            Vector3d foot = source.FootPosition;
            source.Reset();
            var target = new BenchmarkNavigator(_fixture.Context, foot, replacementProfile, i);
            target.PrepareFrame();
            JsonRecordSerializer.Populate(target, payload);
            ChronicleGuard.ThrowIfTrue(
                target.Steering.PathRecheckPhase != i % 16
                || target.Steering.PathRecheckCooldownFrames != 16
                || target.Steering.HasNavigationGuidance,
                $"Guided burst restore did not retain phase without acquiring a lease for agent {i}.");
            _agents[i] = target;
        }
        long populationTicks = Stopwatch.GetTimestamp() - populationStarted;
        long populationAllocated = GC.GetAllocatedBytesForCurrentThread() - populationAllocatedBefore;
        int populationCollectionMask = (GC.CollectionCount(0) != populationGen0 ? 1 : 0)
            | (GC.CollectionCount(1) != populationGen1 ? 2 : 0)
            | (GC.CollectionCount(2) != populationGen2 ? 4 : 0);
        ChronicleGuard.ThrowIfTrue(
            CountActiveGuides() != 0,
            "Guided burst population acquired guide leases.");
        Console.WriteLine("NAVIGATION_GUIDED_BURST_SAMPLES " + JsonSerializer.Serialize(new
        {
            scenario = "json-end-to-end-restore-preparation",
            logicalFrame = _fixture.Context.FrameCount,
            ticks = populationTicks,
            allocatedBytes = populationAllocated,
            collectionMask = populationCollectionMask,
            rayCount = 0,
            activeGuides = 0
        }));
        int restoreRays = CaptureBurstFrame("restore-next-admission");
        ChronicleGuard.ThrowIfTrue(
            restoreRays != AgentCount || CountActiveGuides() != AgentCount,
            "Restored guided agents delayed admission or failed to reacquire guidance.");
    }

    private int CaptureBurstFrame(string scenario, NavigationOperationReceipt receipt = null)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long started = Stopwatch.GetTimestamp();
        int rayCount = SimulateAgentsInstrumented();
        long ticks = Stopwatch.GetTimestamp() - started;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int collectionMask = (GC.CollectionCount(0) != gen0 ? 1 : 0)
            | (GC.CollectionCount(1) != gen1 ? 2 : 0)
            | (GC.CollectionCount(2) != gen2 ? 4 : 0);
        Console.WriteLine("NAVIGATION_GUIDED_BURST_SAMPLES " + JsonSerializer.Serialize(new
        {
            scenario,
            logicalFrame = _fixture.Context.FrameCount,
            ticks,
            allocatedBytes,
            collectionMask,
            rayCount,
            activeGuides = CountActiveGuides(),
            zeroTargetDirections = CountZeroTargetDirections(),
            publicationStatus = receipt?.Status.ToString()
        }));
        return rayCount;
    }

    private int GetAssignedPhaseCount(int phase)
    {
        int count = 0;
        for (int i = 0; i < AgentCount; i++)
        {
            if (_phaseAssignments[i] == phase)
                count++;
        }
        return count;
    }

    private int CountActiveGuides()
    {
        int count = 0;
        for (int i = 0; i < AgentCount; i++)
        {
            if (_agents[i].Steering.HasNavigationGuidance)
                count++;
        }
        return count;
    }

    private int CountZeroTargetDirections()
    {
        int count = 0;
        for (int i = 0; i < AgentCount; i++)
        {
            if (_agents[i].Steering.TargetDirection == Vector3d.Zero)
                count++;
        }
        return count;
    }

    private void ValidateAgentFrame(int frame)
    {
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            NavigationCommittedCellState? committed = agent.LastCommittedCell;
            if (!agent.IsGuided || !agent.Steering.HasNavigationGuidance
                || !committed.HasValue || committed.Value.SimulationFrame != _fixture.Context.FrameCount
                || committed.Value.Medium != TraversalMedium.Solid || committed.Value.Address.MapId != MapId
                || committed.Value.Address.Index.x < 0 || committed.Value.Address.Index.x >= 50
                || committed.Value.Address.Index.y != 0
                || committed.Value.Address.Index.z < 0 || committed.Value.Address.Index.z >= Length
                || agent.FootPosition.X - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.X + agent.Radius >= (Fixed64)49 + Fixed64.Half
                || agent.FootPosition.Z - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.Z + agent.Radius >= (Fixed64)99 + Fixed64.Half)
            {
                ChronicleGuard.ThrowIfTrue(true,
                    $"Guided agent invalid: frame={frame}, agent={i}, position={agent.Position}, "
                    + $"guided={agent.Steering.HasNavigationGuidance}, cellFrame={committed?.SimulationFrame}, "
                    + $"expectedFrame={_fixture.Context.FrameCount}, cell={committed?.Address}.");
            }
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            if ((frame + 1) % 32 == 0)
            {
                if (_movement[i] == 0)
                    ChronicleGuard.ThrowIfTrue(true, $"Guided agent {i} did not move during a 32-frame block.");
                _movement[i] = 0;
            }
        }
    }

    private void ValidateActiveGuides()
    {
        NavigationAStarPayloadCache astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationFlowFieldPayloadCache flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        ChronicleGuard.ThrowIfTrue(astar.ActiveLeaseCount != (_algorithm == PathAlgorithm.AStar ? AgentCount : 0)
            || flow.ActiveLeaseCount != (_algorithm == PathAlgorithm.FlowField ? AgentCount : 0),
            "Guided frame did not retain one real guide per Navigator.");
    }

    [IterationCleanup]
    public void ValidateAndExport()
    {
        ChronicleGuard.ThrowIfTrue(_frames != MeasuredFrames, "Guided frame block ended early.");
        ValidateActiveGuides();
        ReleaseAgentsAndValidateDrained();
        if (_mode == null)
        {
            Console.WriteLine("NAVIGATION_GUIDED_SAMPLES " + JsonSerializer.Serialize(new
            {
                process = Environment.ProcessId,
                iteration = ++_iteration,
                algorithm = Algorithm,
                agents = AgentCount,
                frameRate = 32,
                preparationFrames = PreparationFrames,
                frames = _frames,
                stopwatchFrequency = Stopwatch.Frequency,
                ticks = _elapsedTicks,
                allocatedBytes = _allocatedBytes,
                collectionMasks = _collections,
                checksums = _checksums
            }));
            return;
        }

        Console.WriteLine("NAVIGATION_GUIDED_SAMPLES " + JsonSerializer.Serialize(new
        {
            process = Environment.ProcessId,
            iteration = ++_iteration,
            algorithm = Algorithm,
            agents = AgentCount,
            frameRate = 32,
            preparationFrames = PreparationFrames,
            frames = _frames,
            stopwatchFrequency = Stopwatch.Frequency,
            ticks = _elapsedTicks,
            allocatedBytes = _allocatedBytes,
            collectionMasks = _collections,
            checksums = _checksums,
            mode = _mode,
            intervalFrames = 16,
            phaseAssignments = _phaseAssignments.AsSpan(0, AgentCount).ToArray(),
            clockOrigin = _clockOrigin,
            mandatoryRayCount = _mandatoryRayCount,
            preparationRayCounts = _preparationRayCounts,
            periodicRayCounts = _periodicRayCounts,
            firstAdmissionTicks = _firstAdmissionTicks,
            firstAdmissionAllocatedBytes = _firstAdmissionAllocatedBytes,
            firstAdmissionCollectionMask = _firstAdmissionCollectionMask
        }));
    }

    private void ReleaseAgentsAndValidateDrained()
    {
        if (_agents != null)
            foreach (BenchmarkNavigator agent in _agents)
                agent?.Reset();
        NavigationAStarPayloadCache astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationFlowFieldPayloadCache flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        ChronicleGuard.ThrowIfTrue(astar.ActiveLeaseCount != 0 || astar.ReservedLeaseCount != 0
            || astar.ReservedPayloadBytes != 0 || astar.LeasedBytes != 0 || astar.DetachedBytes != 0
            || flow.ActiveLeaseCount != 0 || flow.ReservedLeaseCount != 0
            || flow.ReservedPayloadBytes != 0 || flow.LeasedBytes != 0 || flow.DetachedBytes != 0
            || _fixture.Context.Guides.Scheduler.RetainedRequestCount != 0
            || _fixture.Context.Guides.Scheduler.RetainedDescriptorBytes != 0,
            "Guided frame retained active, reserved, detached, or scheduled ownership after reset.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_fixture == null)
            return;
        try
        {
            ReleaseAgentsAndValidateDrained();
        }
        finally
        {
            _fixture.Teardown();
        }
    }
}
