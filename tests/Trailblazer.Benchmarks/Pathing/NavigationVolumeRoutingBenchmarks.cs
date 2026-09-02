//=======================================================================
// NavigationVolumeRoutingBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures unified Gas/Liquid routing, semantic actions, and warm guide reuse.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Volume", "Transition")]
public class NavigationVolumeRoutingBenchmarks
{
    private VolumeRoutingScenario _scenario;
    private long _signal;

    /// <summary>Bounded volume-routing workload selected for the current benchmark.</summary>
    [ParamsSource(nameof(Cases))]
    public string Scenario { get; set; }

    /// <summary>Volume, action, direction-set, and cache workloads.</summary>
    public IEnumerable<string> Cases => new[]
    {
        "RectOpen2DAStar",
        "RectObstructed2DAStar",
        "RectOpen3DFlow",
        "RectObstructed3DFlow",
        "LargeBodyAStar",
        "HexVerticalDiagonalAStar",
        "RuleScanAStar",
        "LadderAStar",
        "DuckFlow",
        "MixedRouteAStar",
        "FaceOnlyControlAStar",
        "FullDirectionsAStar",
        "WarmAStarCacheHit",
        "WarmFlowCacheHit"
    };

    [GlobalSetup]
    public void Setup()
    {
        _scenario = VolumeRoutingScenario.Create(Scenario);
        _scenario.RunCold();
        if (_scenario.IsWarm)
            _scenario.WarmCache();
        _signal = Execute();
        if (_signal == 0)
            throw new InvalidOperationException($"Navigation preflight produced no signal for {Scenario}.");
        _scenario.ValidatePreflight();
        long before = GC.GetAllocatedBytesForCurrentThread();
        _signal = Execute();
        _scenario.WarmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (_scenario.IsWarm && _scenario.WarmAllocatedBytes != 0)
        {
            throw new InvalidOperationException(
                $"Warm guide reuse allocated for {Scenario}: {_scenario.WarmAllocatedBytes} B.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        string warmSampleAllocatedBytes = _scenario.IsWarm
            ? _scenario.WarmAllocatedBytes.ToString(CultureInfo.InvariantCulture)
            : "n/a";
        string dependencyComponents = _scenario.DependencyComponents < 0
            ? "n/a"
            : _scenario.DependencyComponents.ToString(CultureInfo.InvariantCulture);
        string dependencyPages = _scenario.DependencyPages < 0
            ? "n/a"
            : _scenario.DependencyPages.ToString(CultureInfo.InvariantCulture);
        Console.WriteLine(
            $"NAVIGATION_VOLUME_ROUTING scenario={Scenario} signal={_signal} "
            + $"status={_scenario.Status} settled_medium_states={_scenario.SettledStates} "
            + $"evaluated_edges={_scenario.EvaluatedEdges} "
            + $"primary_volume_candidates={_scenario.PrimaryCandidates} "
            + $"shortcut_volume_candidates={_scenario.ShortcutCandidates} "
            + $"volume_union_checks={_scenario.UnionChecks} "
            + $"covered_voxel_intervals={_scenario.CoveredIntervals} "
            + $"transition_candidates={_scenario.TransitionCandidates} "
            + $"transition_pairs={_scenario.TransitionPairs} "
            + $"successful_dependency_merges={_scenario.DependencyMerges} "
            + $"dependency_components={dependencyComponents} "
            + $"dependency_pages={dependencyPages} "
            + $"astar_guide_steps={_scenario.AStarGuideSteps} "
            + $"flow_payload_nodes={_scenario.FlowPayloadNodes} "
            + $"immutable_payload_bytes={_scenario.PayloadBytes} "
            + $"warm_sample_allocated_bytes={warmSampleAllocatedBytes}");
        _scenario?.Dispose();
    }

    /// <summary>Executes one preflighted production query or warm cache acquisition.</summary>
    [Benchmark]
    public long Execute() => _scenario.IsWarm
        ? _scenario.RunWarm()
        : _scenario.RunCold();

    private sealed class VolumeRoutingScenario : IDisposable
    {
        private const int NodeCapacity = 512;
        private static readonly NavigationWorkBudget Budget = new(
            int.MaxValue,
            64,
            NodeCapacity,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);
        private static readonly GuideSampleWorkBudget SampleBudget = new(
            65_536,
            65_536,
            65_536,
            65_536,
            65_536,
            65_536,
            65_536);

        private readonly BenchmarkPathFixture _fixture;
        private readonly PathQuery _query;
        private readonly string _scenarioName;
        private readonly NavigationCellAddress _originAddress;
        private readonly NavigationAStarWorkspace _aStarWorkspace;
        private readonly NavigationFlowFieldWorkspace _flowWorkspace;
        private readonly NavigationQueryAdmissionWork _admission;
        private readonly NavigationMediumStateRef[] _referenceStates;
        private readonly Fixed64[] _referenceCosts;
        private readonly bool[] _referenceClosed;
        private NavigationAStarPayloadKey _aStarPayloadKey;
        private NavigationFlowFieldPayloadKey _flowPayloadKey;
        private NavigationTransitionInstruction _transition;
        private Fixed64 _traversalCost;
        private int _transitionCount;

        private VolumeRoutingScenario(
            BenchmarkPathFixture fixture,
            PathQuery query,
            ScenarioDefinition definition)
        {
            _fixture = fixture;
            _query = query;
            _scenarioName = definition.Scenario;
            _originAddress = new NavigationCellAddress(definition.MapId, definition.Start);
            IsWarm = definition.IsWarm;
            bool ownsReference = _scenarioName is "FaceOnlyControlAStar"
                or "FullDirectionsAStar";
            _referenceStates = ownsReference
                ? new NavigationMediumStateRef[NodeCapacity]
                : Array.Empty<NavigationMediumStateRef>();
            _referenceCosts = ownsReference
                ? new Fixed64[NodeCapacity]
                : Array.Empty<Fixed64>();
            _referenceClosed = ownsReference
                ? new bool[NodeCapacity]
                : Array.Empty<bool>();
            if (query.Algorithm == PathAlgorithm.AStar)
            {
                _aStarWorkspace = new NavigationAStarWorkspace(
                    mapCapacity: 1,
                    endpointPageCapacity: 64,
                    componentCapacity: 64,
                    nodeCapacity: NodeCapacity,
                    rayCoveredAddressCapacity: NodeCapacity,
                    rayTraceIntervalCapacity: NodeCapacity,
                    guidePointCapacity: (NodeCapacity * 2) - 1);
                _admission = new NavigationQueryAdmissionWork(
                    fixture.World,
                    fixture.Context.Pathing.NavigationGraphStore,
                    _aStarWorkspace.EndpointWorkspace,
                    _aStarWorkspace.RayWorkspace,
                    PathAlgorithm.AStar);
            }
            else
            {
                _flowWorkspace = new NavigationFlowFieldWorkspace(
                    1,
                    64,
                    64,
                    NodeCapacity,
                    NodeCapacity,
                    NodeCapacity);
                _admission = new NavigationQueryAdmissionWork(
                    fixture.World,
                    fixture.Context.Pathing.NavigationGraphStore,
                    _flowWorkspace.EndpointWorkspace,
                    _flowWorkspace.RayWorkspace,
                    PathAlgorithm.FlowField);
            }
        }

        internal bool IsWarm { get; }
        internal string Status { get; private set; } = "Pending";
        internal int SettledStates { get; private set; }
        internal int EvaluatedEdges { get; private set; }
        internal int PrimaryCandidates { get; private set; }
        internal int ShortcutCandidates { get; private set; }
        internal long UnionChecks { get; private set; }
        internal int CoveredIntervals { get; private set; }
        internal int TransitionCandidates { get; private set; }
        internal int TransitionPairs { get; private set; }
        internal int DependencyMerges { get; private set; }
        internal int DependencyComponents { get; private set; }
        internal int DependencyPages { get; private set; }
        internal int AStarGuideSteps { get; private set; }
        internal int FlowPayloadNodes { get; private set; }
        internal long PayloadBytes { get; private set; }
        internal long WarmAllocatedBytes { get; set; }

        internal static VolumeRoutingScenario Create(string scenario)
        {
            ScenarioDefinition definition = ScenarioDefinition.Create(scenario);
            var fixture = new BenchmarkPathFixture();
            try
            {
                fixture.Setup(settings: NavigationGraphBenchmarkScenario.CreateSettings(
                    NodeCapacity,
                    concurrentQueries: 2));
                if (definition.Indices == null)
                {
                    if (!fixture.World.TryAddGrid(definition.Configuration, out _))
                        throw new InvalidOperationException("The volume benchmark grid was rejected.");
                }
                else if (!fixture.World.TryAddGrid(
                    definition.Configuration,
                    definition.Indices,
                    out _))
                {
                    throw new InvalidOperationException("The sparse volume benchmark grid was rejected.");
                }
                if (!definition.Configuration.TryNormalize(
                    out NormalizedGridConfiguration binding))
                {
                    throw new InvalidOperationException("The volume benchmark grid could not normalize.");
                }

                var builder = new NavigationMapBuilder(definition.MapId, binding);
                for (int i = 0; i < definition.Cells.Length; i++)
                    builder.AddCell(definition.Cells[i].Index, definition.Cells[i].Cell);
                for (int i = 0; i < definition.Transitions.Length; i++)
                    builder.AddTransition(definition.Transitions[i]);
                for (int i = 0; i < definition.Rules.Length; i++)
                    builder.AddTransitionRule(definition.Rules[i]);
                var policyKey = new NavigationAreaPolicyKey(definition.MapId, 1);
                Publish(fixture.Context, builder.Build(), policyKey);

                var profile = new NavigationAgentProfile(
                    definition.Shape,
                    Fixed64.Zero,
                    Fixed64.Zero,
                    Fixed64.Zero,
                    TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
                    TraversalCapability.Jump
                        | TraversalCapability.Climb
                        | TraversalCapability.Swim
                        | TraversalCapability.Fly
                        | TraversalCapability.Teleport);
                PathQuery query = new(
                    new NavigationEndpoint(
                        NavigationGraphBenchmarkScenario.GetFoot(binding, definition.Start),
                        definition.MapId),
                    new NavigationEndpoint(
                        NavigationGraphBenchmarkScenario.GetFoot(binding, definition.End),
                        definition.MapId),
                    profile,
                    policyKey,
                    new TraversalIntent(definition.StartMedium, definition.TargetMedia),
                    definition.Algorithm,
                    Budget,
                    definition.AllowTransitions,
                    definition.Algorithm == PathAlgorithm.FlowField
                        ? new FlowFieldQueryOptions(Fixed64.Zero)
                        : default);
                return new VolumeRoutingScenario(fixture, query, definition);
            }
            catch
            {
                fixture.Teardown();
                throw;
            }
        }

        internal long RunCold()
        {
            NavigationWorldGraphLease lease = _fixture.Context.Pathing.TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException("The volume benchmark could not acquire its graph.");
            _admission.Begin(
                lease,
                _query,
                _query.Traversal.StartMedium,
                _query.Traversal.TargetMedia);
            try
            {
                while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                    _admission.Advance(int.MaxValue, int.MaxValue);
                if (_admission.Status != NavigationQueryAdmissionStatus.Success)
                {
                    throw new InvalidOperationException(
                        $"Volume endpoint admission failed with {_admission.Status}.");
                }
                if (_scenarioName == "FaceOnlyControlAStar")
                {
                    return RunFaceOnlyReference(
                        _admission.Result,
                        capture: true,
                        out _,
                        out _);
                }
                return _query.Algorithm == PathAlgorithm.AStar
                    ? RunAStar()
                    : RunFlow();
            }
            finally
            {
                _admission.Result.ReleaseLease();
                _admission.Dispose();
            }
        }

        internal void WarmCache()
        {
            if (_query.Algorithm == PathAlgorithm.AStar)
            {
                using NavigationGuideLease guide = AcquireAStarGuide();
                ProveAStarCacheCheckout();
                return;
            }
            using NavigationFlowFieldLease flow = AcquireFlowGuide();
            NavigationGuideStatus status = flow.TrySample(
                _query.Start.Position,
                SampleBudget,
                out _);
            if (status != NavigationGuideStatus.Success)
                throw new InvalidOperationException($"Warm Flow preflight failed with {status}.");
            ProveFlowCacheCheckout();
        }

        internal long RunWarm()
        {
            if (_query.Algorithm == PathAlgorithm.AStar)
            {
                using NavigationGuideLease guide = AcquireAStarGuide();
                NavigationGuideStatus stepStatus = guide.TryGetCurrentStep(
                    out NavigationGuideStep step);
                if (stepStatus != NavigationGuideStatus.Success
                    || step.Medium != _query.Traversal.StartMedium
                    || step.HasTransition)
                {
                    throw new InvalidOperationException(
                        $"Warm A* step validation failed with {stepStatus}.");
                }
                Status = "Success";
                return guide.TotalCost.m_rawValue ^ guide.StepCount;
            }
            using NavigationFlowFieldLease flow = AcquireFlowGuide();
            NavigationGuideStatus status = flow.TrySample(
                _query.Start.Position,
                SampleBudget,
                out NavigationFlowSample sample);
            if (status != NavigationGuideStatus.Success)
                throw new InvalidOperationException($"Warm Flow sample failed with {status}.");
            if (sample.Medium != _query.Traversal.StartMedium
                || sample.HasTransition
                || sample.Heading == Vector3d.Zero
                || sample.Target != new Vector3d(
                    Fixed64.One,
                    Fixed64.Half,
                    Fixed64.One))
            {
                throw new InvalidOperationException("Warm Flow sample evidence was not exact.");
            }
            Status = "Success";
            return sample.Target.X.m_rawValue
                ^ sample.Target.Y.m_rawValue
                ^ sample.Target.Z.m_rawValue
                ^ (long)sample.Medium;
        }

        private long RunAStar()
        {
            using var work = new NavigationSurfaceAStarWork(
                _fixture.World,
                _fixture.Context.Pathing.NavigationGraphStore,
                _admission.Result,
                _aStarWorkspace,
                _admission.RayWork,
                long.MaxValue);
            while (work.Status == NavigationSurfaceAStarStatus.Pending)
                work.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            if (work.Status != NavigationSurfaceAStarStatus.Success)
                throw new InvalidOperationException($"Volume A* failed with {work.Status}.");
            NavigationAStarPayload payload = work.Result;
            Capture(_admission.Meter, payload.Dependencies);
            Status = "Success";
            AStarGuideSteps = payload.GuidePoints.Length;
            PayloadBytes = payload.RetainedBytes;
            _aStarPayloadKey = payload.Key;
            _traversalCost = payload.Cost;
            CaptureTransition(payload.TransitionInstructions);
            return payload.Cost.m_rawValue ^ payload.GuidePoints.Length;
        }

        private long RunFlow()
        {
            using var work = new NavigationFlowFieldWork(
                _fixture.World,
                _admission.Result,
                _flowWorkspace,
                _fixture.Context.Pathing.NavigationFlowAdmissionGate
                    .PayloadCache.MaximumSinglePayloadBytes);
            while (work.Status == NavigationFlowFieldStatus.Pending)
                work.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            if (work.Status != NavigationFlowFieldStatus.Success || work.Result == null)
                throw new InvalidOperationException($"Volume Flow failed with {work.Status}.");
            NavigationFlowFieldPayload payload = work.Result;
            Capture(_admission.Meter, payload.Dependencies);
            Status = "Success";
            FlowPayloadNodes = payload.Nodes.Length;
            PayloadBytes = payload.RetainedBytes;
            _flowPayloadKey = payload.Key;
            _traversalCost = payload.LastSettledCost;
            CaptureTransition(payload.TransitionInstructions);
            return payload.LastSettledCost.m_rawValue ^ payload.Nodes.Length;
        }

        internal void ValidatePreflight()
        {
            if (Status != "Success")
                throw new InvalidOperationException($"Unexpected benchmark status '{Status}'.");

            switch (_scenarioName)
            {
                case "RectOpen2DAStar":
                case "WarmAStarCacheHit":
                    RequireCost(42_518_007_000L);
                    break;
                case "RectObstructed2DAStar":
                    RequireCost(50_065_807_776L);
                    break;
                case "RectOpen3DFlow":
                case "WarmFlowCacheHit":
                    RequireCost(22_317_304_722L);
                    break;
                case "RectObstructed3DFlow":
                    RequireCost(28_177_038_166L);
                    break;
                case "LargeBodyAStar":
                    RequireCost(14_878_203_148L);
                    Require(UnionChecks > 0 && CoveredIntervals > 0,
                        "large-body union evidence");
                    break;
                case "HexVerticalDiagonalAStar":
                    RequireCost(17_179_869_185L);
                    Require(ShortcutCandidates > 0, "hex shortcut evidence");
                    break;
                case "RuleScanAStar":
                    RequireCost(4_294_967_296L);
                    Require(TransitionCandidates == 2_176 && TransitionPairs == 33,
                        "rule scan counters");
                    RequireTransition(
                        "rule-00",
                        TraversalTransitionType.Takeoff,
                        TraversalMedium.Liquid,
                        TraversalMedium.Gas,
                        TraversalTransitionLocomotionHints.None);
                    break;
                case "LadderAStar":
                    RequireCost(0);
                    Require(TransitionCandidates == 2 && TransitionPairs == 2,
                        "ladder counters");
                    RequireTransition(
                        "ladder-down",
                        TraversalTransitionType.Climb,
                        TraversalMedium.Solid,
                        TraversalMedium.Liquid,
                        TraversalTransitionLocomotionHints.RequestClimb);
                    break;
                case "DuckFlow":
                    RequireCost(12_884_901_886L);
                    Require(TransitionCandidates == 44 && TransitionPairs == 3,
                        "duck counters");
                    RequireTransition(
                        "duck-takeoff",
                        TraversalTransitionType.Takeoff,
                        TraversalMedium.Liquid,
                        TraversalMedium.Gas,
                        TraversalTransitionLocomotionHints.None);
                    break;
                case "MixedRouteAStar":
                    RequireCost(8_589_934_592L);
                    Require(TransitionCandidates == 2 && TransitionPairs == 2,
                        "mixed-route counters");
                    RequireTransition(
                        "mixed-land",
                        TraversalTransitionType.Landing,
                        TraversalMedium.Gas,
                        TraversalMedium.Solid,
                        TraversalTransitionLocomotionHints.None);
                    break;
                case "FaceOnlyControlAStar":
                    RequireCost(25_769_803_776L);
                    Require(ShortcutCandidates > 0, "filtered shortcut candidates");
                    break;
                case "FullDirectionsAStar":
                    RequireCost(14_878_203_148L);
                    Require(ShortcutCandidates > 0, "full shortcut candidates");
                    long signal = RunStandaloneFaceOnlyReference(
                        out Fixed64 faceCost,
                        out int faceSettled);
                    Require(signal != 0, "face-only reference signal");
                    Require(_traversalCost < faceCost, "full-direction shortcut use");
                    Require(_traversalCost <= faceCost, "full-direction cost bound");
                    Require(SettledStates <= faceSettled, "full-direction settled-state bound");
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No semantic preflight exists for '{_scenarioName}'.");
            }

            if (!IsWarm
                && _scenarioName is not "RuleScanAStar"
                and not "LadderAStar"
                and not "DuckFlow"
                and not "MixedRouteAStar")
            {
                Require(_transitionCount == 0, "absence of semantic actions");
            }
        }

        private void Capture(NavigationWorkMeter meter, GraphDependencyStamp dependencies)
        {
            SettledStates = meter.ExpandedNodes;
            EvaluatedEdges = meter.EvaluatedEdges;
            PrimaryCandidates = meter.PrimaryVolumeCandidates;
            ShortcutCandidates = meter.ShortcutVolumeCandidates;
            UnionChecks = meter.VolumeUnionChecks;
            CoveredIntervals = meter.CoveredVoxelIntervals;
            TransitionCandidates = meter.TransitionCandidates;
            TransitionPairs = meter.TransitionPairs;
            DependencyMerges = meter.SuccessfulDependencyMerges;
            DependencyComponents = dependencies.Components.Length;
            DependencyPages = dependencies.Pages.Length;
        }

        private void CaptureTransition(
            NavigationTransitionInstruction[] transitionInstructions)
        {
            _transitionCount = transitionInstructions.Length;
            _transition = transitionInstructions.Length == 0
                ? default
                : transitionInstructions[0];
            if (transitionInstructions.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{_scenarioName} produced more than one semantic action.");
            }
        }

        private long RunStandaloneFaceOnlyReference(
            out Fixed64 cost,
            out int settled)
        {
            NavigationWorldGraphLease lease = _fixture.Context.Pathing
                .TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException(
                    "The face-only reference could not acquire its graph.");
            _admission.Begin(
                lease,
                _query,
                _query.Traversal.StartMedium,
                _query.Traversal.TargetMedia);
            try
            {
                while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                    _admission.Advance(int.MaxValue, int.MaxValue);
                if (_admission.Status != NavigationQueryAdmissionStatus.Success)
                {
                    throw new InvalidOperationException(
                        $"Face-only endpoint admission failed with {_admission.Status}.");
                }
                return RunFaceOnlyReference(
                    _admission.Result,
                    capture: false,
                    out cost,
                    out settled);
            }
            finally
            {
                _admission.Result.ReleaseLease();
                _admission.Dispose();
            }
        }

        private long RunFaceOnlyReference(
            NavigationResolvedPathQuery query,
            bool capture,
            out Fixed64 resultCost,
            out int settled)
        {
            Array.Clear(_referenceClosed, 0, _referenceClosed.Length);
            _aStarWorkspace.RayWorkspace.Reset();
            NavigationWorkMeter meter = query.Meter;

            var start = new NavigationMediumStateRef(
                query.Start.Node,
                query.StartMedium);
            var target = new NavigationMediumStateRef(
                query.End.Node,
                query.End.ResolutionMedium);
            _referenceStates[0] = start;
            _referenceCosts[0] = Fixed64.Zero;
            int stateCount = 1;
            settled = 0;
            resultCost = default;

            while (true)
            {
                int selected = SelectReferenceState(stateCount);
                if (selected < 0)
                    throw new InvalidOperationException("The face-only reference found no path.");
                if (!meter.TryConsumeExpandedNodes(1))
                    throw new InvalidOperationException("The face-only reference exceeded its node budget.");
                _referenceClosed[selected] = true;
                settled++;
                NavigationMediumStateRef source = _referenceStates[selected];
                if (source.Equals(target))
                {
                    resultCost = _referenceCosts[selected];
                    break;
                }

                var edges = new NavigationTraversalEdgeEnumerator(
                    _fixture.World,
                    query.Graph,
                    source,
                    _query.Agent,
                    query.AreaPolicy,
                    _aStarWorkspace.RayWorkspace,
                    allowTransitions: false,
                    emittedSurfaceOrdinal: -1);
                int edgeRemaining = int.MaxValue;
                int connectionRemaining = int.MaxValue;
                while (true)
                {
                    NavigationTraversalEdgeAdvanceStatus status = edges.AdvanceOne(
                        meter,
                        _aStarWorkspace.RayWorkspace.Dependencies,
                        ref edgeRemaining,
                        ref connectionRemaining);
                    if (status == NavigationTraversalEdgeAdvanceStatus.Pending)
                        continue;
                    if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                        break;
                    if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                    {
                        throw new InvalidOperationException(
                            $"The face-only dispatcher stopped with {status}.");
                    }
                    if (edges.CurrentKind != NavigationTraversalEdgeKind.Volume)
                        throw new InvalidOperationException("The face-only reference left volume routing.");
                    if (edges.CurrentVolumeIsShortcut)
                        continue;
                    if (!Fixed64.TryAdd(
                            _referenceCosts[selected],
                            edges.CurrentCost,
                            out Fixed64 candidateCost))
                    {
                        throw new InvalidOperationException("The face-only reference cost overflowed.");
                    }
                    int targetOrdinal = FindReferenceState(
                        edges.CurrentTarget,
                        stateCount);
                    if (targetOrdinal < 0)
                    {
                        if (stateCount == _referenceStates.Length)
                            throw new InvalidOperationException("The face-only reference exhausted its states.");
                        targetOrdinal = stateCount++;
                        _referenceStates[targetOrdinal] = edges.CurrentTarget;
                        _referenceCosts[targetOrdinal] = candidateCost;
                    }
                    else if (!_referenceClosed[targetOrdinal]
                        && candidateCost < _referenceCosts[targetOrdinal])
                    {
                        _referenceCosts[targetOrdinal] = candidateCost;
                    }
                }
            }

            if (capture)
            {
                Status = "Success";
                _traversalCost = resultCost;
                SettledStates = settled;
                EvaluatedEdges = meter.EvaluatedEdges;
                PrimaryCandidates = meter.PrimaryVolumeCandidates;
                ShortcutCandidates = meter.ShortcutVolumeCandidates;
                UnionChecks = meter.VolumeUnionChecks;
                CoveredIntervals = meter.CoveredVoxelIntervals;
                TransitionCandidates = meter.TransitionCandidates;
                TransitionPairs = meter.TransitionPairs;
                DependencyMerges = meter.SuccessfulDependencyMerges;
                DependencyComponents = -1;
                DependencyPages = -1;
                AStarGuideSteps = 0;
                FlowPayloadNodes = 0;
                PayloadBytes = 0;
                _transitionCount = 0;
                _transition = default;
            }
            return resultCost.m_rawValue ^ settled;
        }

        private int SelectReferenceState(int count)
        {
            int selected = -1;
            for (int i = 0; i < count; i++)
            {
                if (_referenceClosed[i])
                    continue;
                if (selected < 0
                    || _referenceCosts[i] < _referenceCosts[selected]
                    || (_referenceCosts[i] == _referenceCosts[selected]
                        && _referenceStates[i].CompareTo(_referenceStates[selected]) < 0))
                {
                    selected = i;
                }
            }
            return selected;
        }

        private int FindReferenceState(NavigationMediumStateRef state, int count)
        {
            for (int i = 0; i < count; i++)
                if (_referenceStates[i].Equals(state))
                    return i;
            return -1;
        }

        private void ProveAStarCacheCheckout()
        {
            using NavigationWorldGraphLease graph = _fixture.Context.Pathing
                .TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException("The A* cache proof could not acquire its graph.");
            NavigationAStarPayloadCache cache = _fixture.Context.Pathing
                .NavigationAStarAdmissionGate.PayloadCache;
            if (!cache.TryReservePayload(
                    PayloadBytes,
                    out NavigationAStarPayloadReservation reservation))
            {
                throw new InvalidOperationException("The exact A* payload could not be reserved.");
            }
            try
            {
                if (!cache.TryCheckoutReserved(
                        _aStarPayloadKey,
                        graph.Graph,
                        ref reservation,
                        out NavigationAStarPayloadLease lease))
                {
                    throw new InvalidOperationException("The exact A* payload was not cached.");
                }
                using (lease)
                {
                    if (lease.Payload.Cost != _traversalCost)
                        throw new InvalidOperationException("The cached A* cost changed.");
                }
            }
            finally
            {
                cache.ReleasePayloadReservation(ref reservation);
            }
        }

        private void ProveFlowCacheCheckout()
        {
            using NavigationWorldGraphLease graph = _fixture.Context.Pathing
                .TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException("The Flow cache proof could not acquire its graph.");
            NavigationFlowFieldPayloadCache cache = _fixture.Context.Pathing
                .NavigationFlowAdmissionGate.PayloadCache;
            NavigationFlowFieldStatus status = cache.TryCheckout(
                _fixture.Context.Pathing.NavigationGraphStore,
                graph.Graph,
                _flowPayloadKey,
                _originAddress,
                out NavigationFlowFieldPayloadLease lease,
                out NavigationFlowFieldPayload proof);
            if (status != NavigationFlowFieldStatus.Success || proof == null)
                throw new InvalidOperationException($"The exact Flow payload was not cached: {status}.");
            using (lease)
            {
                if (lease.TryGetPayload(out NavigationFlowFieldPayload payload)
                        != NavigationFlowFieldStatus.Success
                    || !ReferenceEquals(payload, proof)
                    || payload.LastSettledCost != _traversalCost)
                {
                    throw new InvalidOperationException("The cached Flow proof changed.");
                }
            }
        }

        private void RequireCost(long expectedRaw)
        {
            if (_traversalCost.m_rawValue != expectedRaw)
            {
                throw new InvalidOperationException(
                    $"{_scenarioName} cost {_traversalCost.m_rawValue} != {expectedRaw}.");
            }
        }

        private void RequireTransition(
            string id,
            TraversalTransitionType type,
            TraversalMedium source,
            TraversalMedium destination,
            TraversalTransitionLocomotionHints hints)
        {
            Require(_transitionCount == 1, "one selected semantic action");
            Require(_transition.Id == id
                && _transition.Type == type
                && _transition.SourceMedium == source
                && _transition.DestinationMedium == destination
                && _transition.LocomotionHints == hints,
                $"semantic action {id}");
        }

        private void Require(bool condition, string fact)
        {
            if (!condition)
                throw new InvalidOperationException($"{_scenarioName} did not prove {fact}.");
        }

        private NavigationGuideLease AcquireAStarGuide()
        {
            NavigationGuideStatus status = _fixture.Context.Guides.RequestGuide(
                _query,
                out NavigationGuideLease? acquired);
            if (status != NavigationGuideStatus.Success || !acquired.HasValue)
                throw new InvalidOperationException($"Warm A* acquisition failed with {status}.");
            return acquired.Value;
        }

        private NavigationFlowFieldLease AcquireFlowGuide()
        {
            NavigationGuideStatus status = _fixture.Context.Guides.RequestFlowField(
                _query,
                out NavigationFlowFieldLease? acquired);
            if (status != NavigationGuideStatus.Success || !acquired.HasValue)
                throw new InvalidOperationException($"Warm Flow acquisition failed with {status}.");
            return acquired.Value;
        }

        public void Dispose()
        {
            _admission?.Dispose();
            _fixture.Teardown();
        }

        private static void Publish(
            TrailblazerWorldContext context,
            NavigationMap map,
            NavigationAreaPolicyKey policyKey)
        {
            var mapOperation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                new NavigationAreaPolicy(
                    policyKey,
                    new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
                publicationSequence: 2,
                effectiveFrame: context.FrameCount + 1);
            if (!context.Pathing.Admit(mapOperation) || !context.Pathing.Admit(policyOperation))
                throw new InvalidOperationException("The volume benchmark publication was rejected.");
            for (int frame = 0;
                frame < 4_096
                && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                    || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
                frame++)
            {
                context.Simulate();
            }
            if (mapOperation.Receipt.Status != NavigationOperationStatus.Applied
                || policyOperation.Receipt.Status != NavigationOperationStatus.Applied)
            {
                throw new InvalidOperationException(
                    $"Volume publication failed: map={mapOperation.Receipt.Status}, "
                    + $"policy={policyOperation.Receipt.Status}.");
            }
        }
    }

    private sealed class ScenarioDefinition
    {
        private ScenarioDefinition(
            string scenario,
            string mapId,
            GridConfiguration configuration,
            VoxelIndex[] indices,
            (VoxelIndex Index, NavigationCell Cell)[] cells,
            TraversalTransitionDefinition[] transitions,
            TraversalTransitionRule[] rules,
            VoxelIndex start,
            VoxelIndex end,
            TraversalMedium startMedium,
            TraversalMedia targetMedia,
            PathAlgorithm algorithm,
            bool allowTransitions,
            bool isWarm,
            KinematicBodyShape shape)
        {
            Scenario = scenario;
            MapId = mapId;
            Configuration = configuration;
            Indices = indices;
            Cells = cells;
            Transitions = transitions;
            Rules = rules;
            Start = start;
            End = end;
            StartMedium = startMedium;
            TargetMedia = targetMedia;
            Algorithm = algorithm;
            AllowTransitions = allowTransitions;
            IsWarm = isWarm;
            Shape = shape;
        }

        internal string Scenario { get; }
        internal string MapId { get; }
        internal GridConfiguration Configuration { get; }
        internal VoxelIndex[] Indices { get; }
        internal (VoxelIndex Index, NavigationCell Cell)[] Cells { get; }
        internal TraversalTransitionDefinition[] Transitions { get; }
        internal TraversalTransitionRule[] Rules { get; }
        internal VoxelIndex Start { get; }
        internal VoxelIndex End { get; }
        internal TraversalMedium StartMedium { get; }
        internal TraversalMedia TargetMedia { get; }
        internal PathAlgorithm Algorithm { get; }
        internal bool AllowTransitions { get; }
        internal bool IsWarm { get; }
        internal KinematicBodyShape Shape { get; }

        internal static ScenarioDefinition Create(string scenario) => scenario switch
        {
            "RectOpen2DAStar" => CreateRect(scenario, 8, 1, 8, false, PathAlgorithm.AStar),
            "RectObstructed2DAStar" => CreateRect(scenario, 8, 1, 8, true, PathAlgorithm.AStar),
            "RectOpen3DFlow" => CreateRect(scenario, 4, 4, 4, false, PathAlgorithm.FlowField),
            "RectObstructed3DFlow" => CreateRect(scenario, 4, 4, 4, true, PathAlgorithm.FlowField),
            "LargeBodyAStar" => CreateLargeBody(scenario),
            "HexVerticalDiagonalAStar" => CreateHex(scenario),
            "RuleScanAStar" => CreateRuleScan(scenario),
            "LadderAStar" => CreateLadder(scenario),
            "DuckFlow" => CreateDuck(scenario),
            "MixedRouteAStar" => CreateMixed(scenario),
            "FaceOnlyControlAStar" => CreateRect(scenario, 3, 3, 3, false, PathAlgorithm.AStar),
            "FullDirectionsAStar" => CreateRect(scenario, 3, 3, 3, false, PathAlgorithm.AStar),
            "WarmAStarCacheHit" => CreateRect(scenario, 8, 1, 8, false, PathAlgorithm.AStar, true),
            "WarmFlowCacheHit" => CreateRect(scenario, 4, 4, 4, false, PathAlgorithm.FlowField, true),
            _ => throw new InvalidOperationException($"Unknown navigation benchmark scenario '{scenario}'.")
        };

        private static ScenarioDefinition CreateRect(
            string scenario,
            int width,
            int height,
            int length,
            bool obstructed,
            PathAlgorithm algorithm,
            bool isWarm = false)
        {
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(width - 1, height - 1, length - 1),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            var cells = new List<(VoxelIndex, NavigationCell)>(width * height * length);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < length; z++)
                    {
                        bool blocked = obstructed
                            && x == width / 2
                            && z != length - 2;
                        cells.Add((
                            new VoxelIndex(x, y, z),
                            Cell(blocked
                                ? TraversalMedia.Solid
                                : TraversalMedia.Gas)));
                    }
                }
            }
            return Basic(
                scenario,
                configuration,
                null,
                cells.ToArray(),
                default,
                new VoxelIndex(width - 1, height - 1, length - 1),
                TraversalMedium.Gas,
                TraversalMedia.Gas,
                algorithm,
                isWarm: isWarm);
        }

        private static ScenarioDefinition CreateLargeBody(string scenario)
        {
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(6, 6, 6),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2));
            var cells = new List<(VoxelIndex, NavigationCell)>(64);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    for (int z = 0; z < 4; z++)
                        cells.Add((new VoxelIndex(x, y, z), Cell(TraversalMedia.Gas)));
            return Basic(
                scenario,
                configuration,
                null,
                cells.ToArray(),
                new VoxelIndex(1, 1, 1),
                new VoxelIndex(2, 2, 2),
                TraversalMedium.Gas,
                TraversalMedia.Gas,
                PathAlgorithm.AStar,
                shape: new KinematicBodyShape((Fixed64)5 / (Fixed64)4, Fixed64.One, Fixed64.Zero));
        }

        private static ScenarioDefinition CreateHex(string scenario)
        {
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(12, 12, 12),
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(
                    (Fixed64)2,
                    (Fixed64)2,
                    HexOrientation.PointyTop),
                storageKind: GridStorageKind.Sparse);
            if (!configuration.TryNormalize(out NormalizedGridConfiguration binding))
                throw new InvalidOperationException("The hex benchmark configuration is invalid.");
            VoxelIndex source = FindCompleteHexCenter(binding);
            var cells = new (VoxelIndex, NavigationCell)[HexDirectionUtility.Offsets.Length + 1];
            var indices = new VoxelIndex[cells.Length];
            cells[0] = (source, Cell(TraversalMedia.Liquid));
            indices[0] = source;
            VoxelIndex target = default;
            for (int i = 0; i < HexDirectionUtility.Offsets.Length; i++)
            {
                VoxelIndex offset = HexDirectionUtility.Offsets[i];
                VoxelIndex index = new(
                    source.x + offset.x,
                    source.y + offset.y,
                    source.z + offset.z);
                cells[i + 1] = (index, Cell(TraversalMedia.Liquid));
                indices[i + 1] = index;
                var direction = (HexDirection)i;
                if (target == default
                    && !HexDirectionUtility.IsPlanar(direction)
                    && !HexDirectionUtility.IsVertical(direction))
                {
                    target = index;
                }
            }
            return Basic(
                scenario,
                configuration,
                indices,
                cells,
                source,
                target,
                TraversalMedium.Liquid,
                TraversalMedia.Liquid,
                PathAlgorithm.AStar);
        }

        private static ScenarioDefinition CreateRuleScan(string scenario)
        {
            var index = default(VoxelIndex);
            TraversalTransitionRule[] rules = new TraversalTransitionRule[32];
            for (int i = 0; i < rules.Length; i++)
            {
                rules[i] = new TraversalTransitionRule(
                    $"rule-{i:D2}",
                    TraversalTransitionType.Takeoff,
                    TraversalMedium.Liquid,
                    TraversalMedium.Gas,
                    TraversalTransitionRuleScope.SameCell,
                    TraversalCapability.Fly,
                    actionCost: (Fixed64)(i + 1),
                    TraversalTransitionLocomotionHints.None);
            }
            return Basic(
                scenario,
                LineConfiguration(1),
                null,
                new[] { (index, Cell(TraversalMedia.Liquid | TraversalMedia.Gas)) },
                index,
                index,
                TraversalMedium.Liquid,
                TraversalMedia.Gas,
                PathAlgorithm.AStar,
                allowTransitions: true,
                rules: rules);
        }

        private static ScenarioDefinition CreateLadder(string scenario)
        {
            VoxelIndex source = default;
            var target = new VoxelIndex(2, 0, 0);
            string mapId = $"phase7-bench-{scenario}";
            return Basic(
                scenario,
                LineConfiguration(3),
                null,
                new[]
                {
                    (source, Cell(TraversalMedia.Solid)),
                    (target, Cell(TraversalMedia.Liquid))
                },
                source,
                target,
                TraversalMedium.Solid,
                TraversalMedia.Liquid,
                PathAlgorithm.AStar,
                allowTransitions: true,
                transitions: new[]
                {
                    new TraversalTransitionDefinition(
                        "ladder-down",
                        TraversalTransitionType.Climb,
                        source,
                        TraversalMedium.Solid,
                        new NavigationCellAddress(mapId, target),
                        TraversalMedium.Liquid,
                        TraversalCapability.Climb,
                        locomotionHints: TraversalTransitionLocomotionHints.RequestClimb)
                });
        }

        private static ScenarioDefinition CreateDuck(string scenario)
        {
            VoxelIndex source = default;
            var target = new VoxelIndex(1, 0, 0);
            return Basic(
                scenario,
                LineConfiguration(2),
                null,
                new[]
                {
                    (source, Cell(TraversalMedia.Liquid)),
                    (target, Cell(TraversalMedia.Gas))
                },
                source,
                target,
                TraversalMedium.Liquid,
                TraversalMedia.Gas,
                PathAlgorithm.FlowField,
                allowTransitions: true,
                rules: new[]
                {
                    new TraversalTransitionRule(
                        "duck-takeoff",
                        TraversalTransitionType.Takeoff,
                        TraversalMedium.Liquid,
                        TraversalMedium.Gas,
                        TraversalTransitionRuleScope.PositiveFaceContact,
                        TraversalCapability.Swim | TraversalCapability.Fly,
                        (Fixed64)2,
                        TraversalTransitionLocomotionHints.None)
                });
        }

        private static ScenarioDefinition CreateMixed(string scenario)
        {
            VoxelIndex source = default;
            var actionSource = new VoxelIndex(1, 0, 0);
            var target = new VoxelIndex(3, 0, 0);
            string mapId = $"phase7-bench-{scenario}";
            return Basic(
                scenario,
                LineConfiguration(4),
                null,
                new[]
                {
                    (source, Cell(TraversalMedia.Gas)),
                    (actionSource, Cell(TraversalMedia.Gas)),
                    (target, Cell(TraversalMedia.Solid))
                },
                source,
                target,
                TraversalMedium.Gas,
                TraversalMedia.Solid,
                PathAlgorithm.AStar,
                allowTransitions: true,
                transitions: new[]
                {
                    new TraversalTransitionDefinition(
                        "mixed-land",
                        TraversalTransitionType.Landing,
                        actionSource,
                        TraversalMedium.Gas,
                        new NavigationCellAddress(mapId, target),
                        TraversalMedium.Solid,
                        TraversalCapability.Fly,
                        actionCost: Fixed64.One)
                });
        }

        private static ScenarioDefinition Basic(
            string scenario,
            GridConfiguration configuration,
            VoxelIndex[] indices,
            (VoxelIndex Index, NavigationCell Cell)[] cells,
            VoxelIndex start,
            VoxelIndex end,
            TraversalMedium startMedium,
            TraversalMedia targetMedia,
            PathAlgorithm algorithm,
            bool allowTransitions = false,
            bool isWarm = false,
            KinematicBodyShape shape = default,
            TraversalTransitionDefinition[] transitions = null,
            TraversalTransitionRule[] rules = null) => new(
                scenario,
                scenario is "FaceOnlyControlAStar" or "FullDirectionsAStar"
                    ? "phase7-bench-direction-control"
                    : $"phase7-bench-{scenario}",
                configuration,
                indices,
                cells,
                transitions ?? Array.Empty<TraversalTransitionDefinition>(),
                rules ?? Array.Empty<TraversalTransitionRule>(),
                start,
                end,
                startMedium,
                targetMedia,
                algorithm,
                allowTransitions,
                isWarm,
                shape == default
                    ? new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero)
                    : shape);

        private static GridConfiguration LineConfiguration(int width) => new(
            Vector3d.Zero,
            new Vector3d(width - 1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));

        private static NavigationCell Cell(TraversalMedia media) => new(
            media,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);

        private static VoxelIndex FindCompleteHexCenter(
            NormalizedGridConfiguration binding)
        {
            for (int y = 1; y < binding.Height - 1; y++)
                for (int q = 1; q < binding.Width - 1; q++)
                    for (int r = 1; r < binding.Length - 1; r++)
                    {
                        var candidate = new VoxelIndex(q, y, r);
                        bool complete = binding.IsValidIndex(candidate);
                        for (int i = 0;
                            complete && i < HexDirectionUtility.Offsets.Length;
                            i++)
                        {
                            VoxelIndex offset = HexDirectionUtility.Offsets[i];
                            complete = binding.IsValidIndex(new VoxelIndex(
                                candidate.x + offset.x,
                                candidate.y + offset.y,
                                candidate.z + offset.z));
                        }
                        if (complete)
                            return candidate;
                    }
            throw new InvalidOperationException("The hex benchmark has no complete center.");
        }
    }
}
