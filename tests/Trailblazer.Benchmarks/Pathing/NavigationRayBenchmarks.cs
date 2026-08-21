//=======================================================================
// NavigationRayBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures the production graph ray and its guide consumers with semantic preflights.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase6", "Graph", "Ray")]
public class NavigationRayBenchmarks
{
    private const int ShortLength = 8;
    private const int MediumLength = 64;
    private const int LongLength = 512;
    private RayScenario _ray;
    private RayScenario _secondRay;
    private AStarScenario _aStar;
    private FlowRejoinScenario _flow;
    private DirectContentionScenario _contention;
    private long _warmAllocatedBytes;
    private long _signal;

    /// <summary>Bounded production workload selected for the current benchmark.</summary>
    [ParamsSource(nameof(Cases))]
    public string Scenario { get; set; }

    /// <summary>Phase 6 ray, guide, Flow, and immediate-workspace workloads.</summary>
    public IEnumerable<string> Cases => new[]
    {
        "Short",
        "Medium",
        "Long",
        "SparseBlocked",
        "MixedSeamExplicit",
        "WorstGuidePoints",
        "BoundedSimplification",
        "FlowRejoin",
        "ImmediateContention"
    };

    [GlobalSetup]
    public void Setup()
    {
        switch (Scenario)
        {
            case "Short":
                _ray = RayScenario.CreateLine(ShortLength, NavigationRayStatus.Success);
                break;
            case "Medium":
                _ray = RayScenario.CreateLine(MediumLength, NavigationRayStatus.Success);
                break;
            case "Long":
                _ray = RayScenario.CreateLine(LongLength, NavigationRayStatus.Success);
                break;
            case "SparseBlocked":
                _ray = RayScenario.CreateSparseBlocked(MediumLength);
                break;
            case "MixedSeamExplicit":
                RayScenario.CreateMixed(out _ray, out _secondRay);
                break;
            case "WorstGuidePoints":
                _aStar = AStarScenario.Create(MediumLength, simplificationRays: 0);
                break;
            case "BoundedSimplification":
                _aStar = AStarScenario.Create(MediumLength, simplificationRays: 1);
                break;
            case "FlowRejoin":
                _flow = FlowRejoinScenario.Create(MediumLength);
                break;
            case "ImmediateContention":
                _contention = DirectContentionScenario.Create(MediumLength);
                break;
            default:
                throw new InvalidOperationException($"Unknown navigation-ray scenario '{Scenario}'.");
        }

        _signal = Execute();
        if (_signal == 0)
            throw new InvalidOperationException($"Navigation-ray preflight produced no signal for {Scenario}.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        _signal = Execute();
        _warmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (_signal == 0)
            throw new InvalidOperationException($"Navigation-ray preflight produced no signal for {Scenario}.");
        if ((_ray != null || _flow != null) && _warmAllocatedBytes != 0)
        {
            throw new InvalidOperationException(
                $"Warm navigation-ray preflight allocated for {Scenario}: {_warmAllocatedBytes} B.");
        }
        if (_contention != null && _contention.WorkerAllocatedBytes != 0)
        {
            throw new InvalidOperationException(
                $"Warm direct-ray workers allocated {_contention.WorkerAllocatedBytes} B.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        int status = GetStatus();
        int secondaryStatus = _secondRay == null ? -1 : (int)_secondRay.LastStatus;
        string queryMeterCounters = _ray == null && _aStar == null
            ? "trace_intervals=n/a covered_intervals=n/a evaluated_edges=n/a "
                + "connection_legs=n/a simplification_rays=n/a"
            : $"trace_intervals={GetTraceIntervals()} covered_intervals={GetCoveredIntervals()} "
                + $"evaluated_edges={GetEvaluatedEdges()} connection_legs={GetConnectionLegs()} "
                + $"simplification_rays={GetSimplificationRays()}";
        int guidePoints = _aStar?.GuidePointCount ?? 0;
        long contentionWorkerAllocatedBytes = _contention?.WorkerAllocatedBytes ?? 0;
        _contention?.Dispose();
        _flow?.Dispose();
        _aStar?.Dispose();
        _secondRay?.Dispose();
        _ray?.Dispose();
        Console.WriteLine(
            $"PHASE6_NAVIGATION_RAY scenario={Scenario} signal={_signal} "
            + $"warm_allocated_bytes={_warmAllocatedBytes} "
            + $"status={status} secondary_status={secondaryStatus} "
            + $"{queryMeterCounters} guide_points={guidePoints} "
            + $"flow_active_leases_after_cleanup={_flow?.ActiveLeaseCountAfterDispose ?? 0} "
            + $"contention_worker_allocated_bytes={contentionWorkerAllocatedBytes}");
    }

    /// <summary>Executes one semantically preflighted Phase 6 navigation-ray workload.</summary>
    [Benchmark]
    public long Execute() => Scenario switch
    {
        "MixedSeamExplicit" => _ray.Run() ^ _secondRay.Run(),
        "WorstGuidePoints" or "BoundedSimplification" => _aStar.Run(),
        "FlowRejoin" => _flow.Run(),
        "ImmediateContention" => _contention.Run(),
        _ => _ray.Run()
    };

    private int GetStatus() => _ray?.LastStatus is NavigationRayStatus rayStatus
        ? (int)rayStatus
        : _secondRay?.LastStatus is NavigationRayStatus secondStatus
            ? (int)secondStatus
            : _aStar?.LastStatus is NavigationSurfaceAStarStatus aStarStatus
                ? (int)aStarStatus
                : _flow?.LastStatus is NavigationGuideStatus flowStatus
                    ? (int)flowStatus
                    : _contention?.LastStatus is NavigationRayStatus contentionStatus
                        ? (int)contentionStatus
                        : -1;

    private int GetTraceIntervals() =>
        (_ray?.Meter.TraceIntervals ?? 0)
        + (_secondRay?.Meter.TraceIntervals ?? 0)
        + (_aStar?.Meter.TraceIntervals ?? 0);

    private int GetCoveredIntervals() =>
        (_ray?.Meter.CoveredVoxelIntervals ?? 0)
        + (_secondRay?.Meter.CoveredVoxelIntervals ?? 0)
        + (_aStar?.Meter.CoveredVoxelIntervals ?? 0);

    private int GetEvaluatedEdges() =>
        (_ray?.Meter.EvaluatedEdges ?? 0)
        + (_secondRay?.Meter.EvaluatedEdges ?? 0)
        + (_aStar?.Meter.EvaluatedEdges ?? 0);

    private int GetConnectionLegs() =>
        (_ray?.Meter.ConnectionLegs ?? 0)
        + (_secondRay?.Meter.ConnectionLegs ?? 0)
        + (_aStar?.Meter.ConnectionLegs ?? 0);

    private int GetSimplificationRays() => _aStar?.Meter.SimplificationRays ?? 0;

    private sealed class RayScenario : IDisposable
    {
        private readonly BenchmarkPathFixture _fixture;
        private readonly NavigationRayRequest _request;
        private readonly NavigationRayWorkspace _workspace;
        private readonly NavigationRayWork _work;
        private readonly NavigationWorkBudget _budget;
        private readonly NavigationRayStatus _expectedStatus;
        private readonly bool _ownsFixture;

        private RayScenario(
            BenchmarkPathFixture fixture,
            NavigationRayRequest request,
            int mapCapacity,
            int addressCapacity,
            NavigationWorkBudget budget,
            NavigationRayStatus expectedStatus,
            bool ownsFixture)
        {
            _fixture = fixture;
            _request = request;
            _workspace = new NavigationRayWorkspace(
                mapCapacity,
                pageCapacity: 256,
                componentCapacity: 256,
                addressCapacity,
                addressCapacity);
            _work = new NavigationRayWork(_workspace);
            _budget = budget;
            _expectedStatus = expectedStatus;
            _ownsFixture = ownsFixture;
            Meter = new NavigationWorkMeter(budget);
        }

        internal NavigationWorkMeter Meter { get; }

        internal NavigationRayStatus LastStatus { get; private set; }

        internal static RayScenario CreateLine(int length, NavigationRayStatus expectedStatus)
        {
            GridConfiguration configuration =
                NavigationGraphBenchmarkScenario.CreateConfiguration(length, 1);
            var fixture = new BenchmarkPathFixture();
            fixture.Setup(
                configuration,
                NavigationGraphBenchmarkScenario.CreateSettings(length, 1));
            PathQuery published = NavigationGraphBenchmarkScenario.Publish(
                fixture,
                configuration,
                $"phase6-ray-{length}",
                length,
                1,
                CreateBudget(length, simplificationRays: 0));
            return Create(
                fixture,
                published,
                chainConstraint: default,
                mapCapacity: 1,
                addressCapacity: checked(length * 4),
                expectedStatus);
        }

        internal static RayScenario CreateSparseBlocked(int length)
        {
            var fixture = new BenchmarkPathFixture();
            fixture.Setup(settings: NavigationGraphBenchmarkScenario.CreateSettings(length, 1));
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(length - 1, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse);
            VoxelIndex[] cells = { default, new(length - 1, 0, 0) };
            if (!fixture.World.TryAddGrid(configuration, cells, out _)
                || !configuration.TryNormalize(out NormalizedGridConfiguration binding))
            {
                throw new InvalidOperationException("Sparse navigation-ray world setup failed.");
            }
            string mapId = "phase6-ray-sparse";
            var builder = new NavigationMapBuilder(mapId, binding);
            builder.AddCell(cells[0], Cell).AddCell(cells[1], Cell);
            PathQuery query = Publish(
                fixture,
                new[] { builder.Build() },
                new[] { GetFoot(binding, cells[0]), GetFoot(binding, cells[1]) },
                mapId,
                CreateBudget(length, 0));
            return Create(
                fixture,
                query,
                chainConstraint: default,
                mapCapacity: 1,
                addressCapacity: checked(length * 4),
                NavigationRayStatus.Blocked);
        }

        internal static void CreateMixed(
            out RayScenario seam,
            out RayScenario explicitCorridor)
        {
            var fixture = new BenchmarkPathFixture();
            fixture.Setup();
            GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)2);
            var sourceConfiguration = new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: metrics,
                storageKind: GridStorageKind.Dense);
            var targetConfiguration = new GridConfiguration(
                new Vector3d(2, 0, 0),
                new Vector3d(2, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: metrics,
                storageKind: GridStorageKind.Dense);
            var explicitOrigin = new Vector3d(0, 0, 8);
            var explicitConfiguration = new GridConfiguration(
                explicitOrigin,
                explicitOrigin + new Vector3d(4, 1, 1),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            if (!fixture.World.TryAddGrid(sourceConfiguration, out _)
                || !fixture.World.TryAddGrid(targetConfiguration, out _)
                || !fixture.World.TryAddGrid(explicitConfiguration, out _)
                || !sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
                || !targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
                || !explicitConfiguration.TryNormalize(
                    out NormalizedGridConfiguration explicitBinding))
            {
                throw new InvalidOperationException("Mixed navigation-ray world setup failed.");
            }
            NavigationMap sourceMap = new NavigationMapBuilder("source", sourceBinding)
                .AddCell(default, Cell)
                .Build();
            NavigationMap targetMap = new NavigationMapBuilder("target", targetBinding)
                .AddCell(default, Cell)
                .Build();
            const string mapId = "phase6-explicit";
            VoxelIndex source = default;
            var firstWitness = new VoxelIndex(1, 0, 0);
            var secondWitness = new VoxelIndex(2, 0, 0);
            var destination = new VoxelIndex(3, 0, 0);
            Vector3d sourceFoot = GetFoot(explicitBinding, source);
            Vector3d destinationFoot = GetFoot(explicitBinding, destination);
            var connection = new NavigationConnection(
                "phase6-explicit-corridor",
                source,
                new NavigationCellAddress(mapId, destination),
                sourceFoot,
                destinationFoot,
                Fixed64.Zero,
                Fixed64.One,
                new[]
                {
                    new NavigationCellAddress(mapId, firstWitness),
                    new NavigationCellAddress(mapId, secondWitness)
                });
            NavigationMap map = new NavigationMapBuilder(mapId, explicitBinding)
                .AddCell(source, Cell)
                .AddCell(firstWitness, Cell)
                .AddCell(secondWitness, Cell)
                .AddCell(destination, Cell)
                .AddConnection(connection)
                .Build();
            NavigationWorkBudget budget = CreateBudget(16, 0);
            PathQuery seamQuery = Publish(
                fixture,
                new[] { sourceMap, map, targetMap },
                new[] { GetFoot(sourceBinding, default), GetFoot(targetBinding, default) },
                "phase6-mixed",
                budget,
                ZeroRadiusProfile);
            PathQuery explicitQuery = new(
                new NavigationEndpoint(sourceFoot, mapId),
                new NavigationEndpoint(destinationFoot, mapId),
                ZeroRadiusProfile,
                seamQuery.AreaPolicy,
                SurfaceIntent,
                PathAlgorithm.AStar,
                budget,
                allowTransitions: false);
            NavigationWorldGraph graph = fixture.Context.Pathing.NavigationGraphStore.Current;
            if (graph.AutomaticSeams.PairCount != 1)
                throw new InvalidOperationException("Mixed navigation-ray seam pair count was not one.");
            var sourceAddress = new NavigationCellAddress(mapId, source);
            var targetAddress = new NavigationCellAddress(mapId, destination);
            if (!graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode))
                throw new InvalidOperationException("Explicit navigation-ray source was not composed.");
            NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
            int explicitOrdinal = -1;
            while (edges.MoveNext())
            {
                if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                {
                    if (explicitOrdinal >= 0)
                    {
                        throw new InvalidOperationException(
                            "Mixed navigation-ray source composed multiple explicit edges.");
                    }
                    explicitOrdinal = edges.CurrentOrdinal;
                }
            }
            if (explicitOrdinal < 0)
                throw new InvalidOperationException("Explicit navigation-ray edge was not composed.");
            seam = Create(
                fixture,
                seamQuery,
                chainConstraint: default,
                mapCapacity: 2,
                addressCapacity: 32,
                NavigationRayStatus.Success,
                ownsFixture: true);
            explicitCorridor = Create(
                fixture,
                explicitQuery,
                NavigationRayChainConstraint.SelectedEdge(
                    sourceAddress,
                    targetAddress,
                    explicitOrdinal),
                mapCapacity: 1,
                addressCapacity: 64,
                NavigationRayStatus.Success,
                ownsFixture: false);
        }

        internal long Run()
        {
            Meter.Reset(_budget);
            _work.Begin(_request);
            while (_work.Status == NavigationRayStatus.Pending)
                _work.Advance(Meter);
            LastStatus = _work.Status;
            if (LastStatus != _expectedStatus)
            {
                NavigationRayResult failure = _work.Result;
                throw new InvalidOperationException(
                    $"Navigation-ray workload returned {LastStatus}; expected {_expectedStatus}; "
                    + $"start={failure.StartAddress}, end={failure.EndAddress}, "
                    + $"trace={Meter.TraceIntervals}, covered={Meter.CoveredVoxelIntervals}, "
                    + $"edges={Meter.EvaluatedEdges}, connections={Meter.ConnectionLegs}.");
            }
            NavigationRayResult result = _work.Result;
            return ((long)LastStatus << 56)
                ^ result.TraversalCost.m_rawValue
                ^ ((long)Meter.TraceIntervals << 24)
                ^ Meter.CoveredVoxelIntervals
                ^ 1L;
        }

        public void Dispose()
        {
            if (_ownsFixture)
                _fixture.Teardown();
        }

        private static RayScenario Create(
            BenchmarkPathFixture fixture,
            PathQuery query,
            NavigationRayChainConstraint chainConstraint,
            int mapCapacity,
            int addressCapacity,
            NavigationRayStatus expectedStatus,
            bool ownsFixture = true)
        {
            NavigationWorldGraph graph = fixture.Context.Pathing.NavigationGraphStore.Current;
            if (!graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy policy))
                throw new InvalidOperationException("Navigation-ray area policy was not published.");
            var request = new NavigationRayRequest(
                fixture.World,
                fixture.Context.Pathing.NavigationGraphStore,
                graph,
                query.Agent,
                policy,
                TraversalMedium.Solid,
                query.Start.Position,
                query.End.Position,
                NavigationRayEndpointAllowance.None,
                chainConstraint);
            return new RayScenario(
                fixture,
                request,
                mapCapacity,
                addressCapacity,
                query.Budget,
                expectedStatus,
                ownsFixture);
        }
    }

    private sealed class AStarScenario : IDisposable
    {
        private readonly BenchmarkPathFixture _fixture;
        private readonly PathQuery _query;
        private readonly NavigationAStarWorkspace _workspace;
        private readonly NavigationQueryAdmissionWork _admission;
        private readonly int _expectedGuidePoints;

        private AStarScenario(
            BenchmarkPathFixture fixture,
            PathQuery query,
            NavigationAStarWorkspace workspace,
            NavigationQueryAdmissionWork admission,
            int expectedGuidePoints)
        {
            _fixture = fixture;
            _query = query;
            _workspace = workspace;
            _admission = admission;
            _expectedGuidePoints = expectedGuidePoints;
        }

        internal NavigationWorkMeter Meter => _admission.Meter;

        internal NavigationSurfaceAStarStatus LastStatus { get; private set; }

        internal int GuidePointCount { get; private set; }

        internal static AStarScenario Create(int length, int simplificationRays)
        {
            GridConfiguration configuration =
                NavigationGraphBenchmarkScenario.CreateConfiguration(length, 1);
            var fixture = new BenchmarkPathFixture();
            fixture.Setup(
                configuration,
                NavigationGraphBenchmarkScenario.CreateSettings(length, 1));
            PathQuery query = NavigationGraphBenchmarkScenario.Publish(
                fixture,
                configuration,
                $"phase6-guide-{simplificationRays}",
                length,
                1,
                CreateBudget(length, simplificationRays));
            int pages = NavigationGraphBenchmarkScenario.GetPageCapacity(length);
            var workspace = new NavigationAStarWorkspace(
                1,
                pages,
                pages + 2,
                length,
                checked(length * 4),
                checked(length * 4),
                checked((length * 2) - 1));
            var admission = new NavigationQueryAdmissionWork(
                fixture.World,
                fixture.Context.Pathing.NavigationGraphStore,
                workspace.EndpointWorkspace,
                workspace.RayWorkspace,
                PathAlgorithm.AStar);
            return new AStarScenario(
                fixture,
                query,
                workspace,
                admission,
                simplificationRays == 0 ? checked((length * 2) - 1) : 2);
        }

        internal long Run()
        {
            NavigationWorldGraphLease lease = _fixture.Context.Pathing.TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException("A* ray benchmark could not acquire the graph.");
            _admission.Begin(
                lease,
                _query,
                TraversalMedium.Solid,
                TraversalMedia.Solid);
            try
            {
                while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                    _admission.Advance(int.MaxValue, int.MaxValue);
                if (_admission.Status != NavigationQueryAdmissionStatus.Success)
                {
                    throw new InvalidOperationException(
                        $"A* ray admission failed with {_admission.Status}.");
                }
                using var search = new NavigationSurfaceAStarWork(
                    _fixture.World,
                    _fixture.Context.Pathing.NavigationGraphStore,
                    _admission.Result,
                    _workspace,
                    _admission.RayWork,
                    long.MaxValue);
                while (search.Status == NavigationSurfaceAStarStatus.Pending)
                    search.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
                LastStatus = search.Status;
                if (LastStatus != NavigationSurfaceAStarStatus.Success)
                    throw new InvalidOperationException($"A* ray benchmark failed with {LastStatus}.");
                NavigationAStarPayload payload = search.Result;
                GuidePointCount = payload.GuidePoints.Length;
                if (GuidePointCount != _expectedGuidePoints)
                {
                    throw new InvalidOperationException(
                        $"A* ray guide points were {GuidePointCount}; expected {_expectedGuidePoints}.");
                }
                return payload.Cost.m_rawValue
                    ^ ((long)GuidePointCount << 32)
                    ^ Meter.SimplificationRays
                    ^ 1L;
            }
            finally
            {
                _admission.Dispose();
            }
        }

        public void Dispose()
        {
            _admission.Dispose();
            _fixture.Teardown();
        }
    }

    private sealed class FlowRejoinScenario : IDisposable
    {
        private static readonly GuideSampleWorkBudget Budget = new(
            128,
            128,
            8,
            32,
            32,
            32,
            1);
        private readonly BenchmarkPathFixture _fixture;
        private readonly NavigationFlowFieldLease _guide;
        private readonly Vector3d _actualFoot;

        private FlowRejoinScenario(
            BenchmarkPathFixture fixture,
            NavigationFlowFieldLease guide,
            Vector3d actualFoot)
        {
            _fixture = fixture;
            _guide = guide;
            _actualFoot = actualFoot;
        }

        internal NavigationGuideStatus LastStatus { get; private set; }

        internal int ActiveLeaseCount =>
            _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount;

        internal int ActiveLeaseCountAfterDispose { get; private set; } = -1;

        internal static FlowRejoinScenario Create(int length)
        {
            GridConfiguration configuration =
                NavigationGraphBenchmarkScenario.CreateConfiguration(length, 1);
            var fixture = new BenchmarkPathFixture();
            fixture.Setup(
                configuration,
                NavigationGraphBenchmarkScenario.CreateSettings(length, 1));
            PathQuery published = NavigationGraphBenchmarkScenario.Publish(
                fixture,
                configuration,
                "phase6-flow-rejoin",
                length,
                1,
                CreateBudget(length, 0));
            PathQuery query = new(
                published.Start,
                published.End,
                published.Agent,
                published.AreaPolicy,
                published.Traversal,
                PathAlgorithm.FlowField,
                published.Budget,
                allowTransitions: false,
                new FlowFieldQueryOptions(Fixed64.Zero));
            NavigationGuideStatus status = fixture.Context.Guides.RequestFlowField(
                query,
                out NavigationFlowFieldLease? result);
            if (status != NavigationGuideStatus.Success || !result.HasValue)
                throw new InvalidOperationException($"Flow rejoin setup failed with {status}.");
            NavigationWorldGraph graph = fixture.Context.Pathing.NavigationGraphStore.Current;
            var sourceAddress = new NavigationCellAddress(
                query.Start.MapId!,
                default);
            if (!graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceRef)
                || !graph.TryGetNodeState(sourceRef, out NavigationNodeState source))
            {
                result.Value.Dispose();
                throw new InvalidOperationException("Flow rejoin source was not composed.");
            }
            Vector3d actualFoot = source.FootAnchor
                + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
            return new FlowRejoinScenario(fixture, result.Value, actualFoot);
        }

        internal long Run()
        {
            LastStatus = _guide.TrySample(_actualFoot, Budget, out Vector3d heading);
            if (LastStatus != NavigationGuideStatus.Success
                || heading != Vector3d.Backward
                || _guide.Status != NavigationGuideStatus.Success
                || ActiveLeaseCount != 1)
            {
                throw new InvalidOperationException(
                    $"Flow rejoin preflight failed: status={LastStatus}, heading={heading}, "
                    + $"lease={_guide.Status}, active={ActiveLeaseCount}.");
            }
            return _guide.OriginIntegrationCost.m_rawValue ^ heading.GetHashCode() ^ 1L;
        }

        public void Dispose()
        {
            _guide.Dispose();
            ActiveLeaseCountAfterDispose = ActiveLeaseCount;
            if (ActiveLeaseCountAfterDispose != 0)
                throw new InvalidOperationException("Flow rejoin guide did not drain.");
            _fixture.Teardown();
        }
    }

    private sealed class DirectContentionScenario : IDisposable
    {
        private readonly BenchmarkPathFixture _fixture;
        private readonly PathQuery _query;
        private readonly Thread[] _workers = new Thread[2];
        private readonly AutoResetEvent[] _starts = new AutoResetEvent[2];
        private readonly AutoResetEvent[] _completed = new AutoResetEvent[2];
        private readonly NavigationRayStatus[] _statuses = new NavigationRayStatus[2];
        private readonly Vector3d[] _headings = new Vector3d[2];
        private readonly Exception[] _failures = new Exception[2];
        private int _stopping;
        private long _workerAllocatedBytes;

        private DirectContentionScenario(BenchmarkPathFixture fixture, PathQuery query)
        {
            _fixture = fixture;
            _query = query;
            for (int i = 0; i < _workers.Length; i++)
            {
                int worker = i;
                _starts[i] = new AutoResetEvent(false);
                _completed[i] = new AutoResetEvent(false);
                _workers[i] = new Thread(() => RunWorker(worker))
                {
                    IsBackground = true,
                    Name = $"Trailblazer navigation-ray contention worker {i}"
                };
                _workers[i].Start();
            }
        }

        internal NavigationRayStatus LastStatus { get; private set; }

        internal long WorkerAllocatedBytes => _workerAllocatedBytes;

        internal static DirectContentionScenario Create(int length)
        {
            GridConfiguration configuration =
                NavigationGraphBenchmarkScenario.CreateConfiguration(length, 1);
            var fixture = new BenchmarkPathFixture();
            fixture.Setup(
                configuration,
                NavigationGraphBenchmarkScenario.CreateSettings(length, 2));
            PathQuery query = NavigationGraphBenchmarkScenario.Publish(
                fixture,
                configuration,
                "phase6-direct-contention",
                length,
                1,
                CreateBudget(length, 0));
            return new DirectContentionScenario(fixture, query);
        }

        internal long Run()
        {
            _workerAllocatedBytes = 0;
            for (int i = 0; i < _workers.Length; i++)
            {
                _failures[i] = null;
                _starts[i].Set();
            }
            for (int i = 0; i < _workers.Length; i++)
                _completed[i].WaitOne();
            for (int i = 0; i < _workers.Length; i++)
            {
                if (_failures[i] != null)
                    throw new InvalidOperationException($"Direct-ray worker {i} failed.", _failures[i]);
                if (_statuses[i] != NavigationRayStatus.Success
                    || _headings[i] != Vector3d.Right)
                {
                    throw new InvalidOperationException(
                        $"Direct-ray worker {i}: status={_statuses[i]}, heading={_headings[i]}.");
                }
            }
            LastStatus = _statuses[0];
            return ((long)LastStatus << 32)
                ^ _headings[0].GetHashCode()
                ^ _headings[1].GetHashCode()
                ^ 1L;
        }

        public void Dispose()
        {
            Volatile.Write(ref _stopping, 1);
            for (int i = 0; i < _workers.Length; i++)
                _starts[i].Set();
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i].Join();
                _starts[i].Dispose();
                _completed[i].Dispose();
            }
            _fixture.Teardown();
        }

        private void RunWorker(int worker)
        {
            while (true)
            {
                _starts[worker].WaitOne();
                if (Volatile.Read(ref _stopping) != 0)
                    return;
                long before = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    _statuses[worker] = _fixture.Context.Guides.TryGetDirectHeading(
                        _query,
                        _query.Start.Position,
                        out _headings[worker]);
                }
                catch (Exception failure)
                {
                    _failures[worker] = failure;
                }
                finally
                {
                    Interlocked.Add(
                        ref _workerAllocatedBytes,
                        GC.GetAllocatedBytesForCurrentThread() - before);
                    _completed[worker].Set();
                }
            }
        }
    }

    private static PathQuery Publish(
        BenchmarkPathFixture fixture,
        NavigationMap[] maps,
        Vector3d[] endpoints,
        string policyId,
        NavigationWorkBudget budget,
        NavigationAgentProfile? profile = null)
    {
        var operations = new NavigationMapCommitOperation[maps.Length];
        for (int i = 0; i < maps.Length; i++)
        {
            operations[i] = new NavigationMapCommitOperation(
                new PreparedNavigationMap(maps[i], 1),
                OverlayReplacementPolicy.Clear,
                i + 1,
                fixture.Context.FrameCount + 1);
            if (!fixture.Context.Pathing.Admit(operations[i]))
                throw new InvalidOperationException($"Navigation-ray map {i} was not admitted.");
        }
        var policyKey = new NavigationAreaPolicyKey(policyId, 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            maps.Length + 1,
            fixture.Context.FrameCount + 1);
        if (!fixture.Context.Pathing.Admit(policyOperation))
            throw new InvalidOperationException("Navigation-ray policy was not admitted.");
        for (int frame = 0; frame < 4_096; frame++)
        {
            bool pending = policyOperation.Receipt.Status == NavigationOperationStatus.Pending;
            for (int i = 0; i < operations.Length; i++)
                pending |= operations[i].Receipt.Status == NavigationOperationStatus.Pending;
            if (!pending)
                break;
            fixture.Context.Simulate();
        }
        for (int i = 0; i < operations.Length; i++)
        {
            if (operations[i].Receipt.Status != NavigationOperationStatus.Applied)
            {
                throw new InvalidOperationException(
                    $"Navigation-ray map {i} failed with {operations[i].Receipt.Status}/"
                    + $"{operations[i].Receipt.Rejection}.");
            }
        }
        if (policyOperation.Receipt.Status != NavigationOperationStatus.Applied)
        {
            throw new InvalidOperationException(
                $"Navigation-ray policy failed with {policyOperation.Receipt.Status}/"
                + $"{policyOperation.Receipt.Rejection}.");
        }
        return new PathQuery(
            new NavigationEndpoint(endpoints[0], maps[0].MapId),
            new NavigationEndpoint(endpoints[1], maps[^1].MapId),
            profile ?? Profile,
            policyKey,
            SurfaceIntent,
            PathAlgorithm.AStar,
            budget,
            allowTransitions: false);
    }

    private static NavigationWorkBudget CreateBudget(int nodeCount, int simplificationRays) => new(
        maxLookupProbes: Math.Max(8_192, checked(nodeCount * 128)),
        maxEndpointCandidates: 32,
        maxExpandedNodes: nodeCount,
        maxEvaluatedEdges: checked(nodeCount * 16),
        maxConnectionLegs: checked(nodeCount * 4),
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: checked(nodeCount * 4),
        maxCoveredVoxelIntervals: checked(nodeCount * 4),
        maxSimplificationRays: simplificationRays);

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        if (!binding.TryGetCellPrism(index, out GridCellPrism prism))
            throw new InvalidOperationException($"Navigation-ray cell {index} has no prism.");
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static NavigationCell Cell => new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    private static NavigationAgentProfile Profile => new(
        new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        TraversalMedia.Solid,
        TraversalCapability.None);

    private static NavigationAgentProfile ZeroRadiusProfile => new(
        new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        TraversalMedia.Solid,
        TraversalCapability.None);

    private static TraversalIntent SurfaceIntent => new(
        TraversalDomain.Surface,
        TraversalMedium.Solid,
        TraversalDomain.Surface);
}
