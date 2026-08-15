using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures context graph lookup and local physical copy-on-write publication scaling.</summary>
[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
[BenchmarkCategory("Phase2", "Graph", "Lifecycle")]
public class NavigationGraphLifecycleBenchmarks
{
    private GridWorld _world;
    private NavigationGraphRuntime _runtime;
    private VoxelGrid _changedGrid;
    private Voxel _changedVoxel;
    private ObstacleToken _obstacle;
    private bool _isBlocked;
    private int _frame;

    /// <summary>Number of independently mapped grids in the context.</summary>
    [Params(1, 16, 128)]
    public int MapCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld();
        _runtime = new NavigationGraphRuntime(_world, CreateSettings());
        _world.OnChangeCommitted += _runtime.EnqueueCommittedChange;
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);

        for (int i = 0; i < MapCount; i++)
        {
            Vector3d origin = new(i * 4, 0, 0);
            var configuration = new GridConfiguration(
                origin,
                origin,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            _world.TryAddGrid(configuration, out ushort gridIndex);
            configuration.TryNormalize(out NormalizedGridConfiguration binding);
            NavigationMap map = new NavigationMapBuilder($"map-{i:D3}", binding)
                .AddCell(default, cell)
                .Build();
            _runtime.Admit(new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, 1),
                OverlayReplacementPolicy.Clear,
                i + 1,
                effectiveFrame: 1));
            if (i == 0)
            {
                _changedGrid = _world.ActiveGrids[gridIndex];
                _changedGrid.TryGetVoxel(default(VoxelIndex), out _changedVoxel);
            }
        }

        _runtime.Maintain(1);
        _frame = 1;
        using NavigationWorldGraphLease warm = _runtime.TryAcquire();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world.OnChangeCommitted -= _runtime.EnqueueCommittedChange;
        _runtime.Dispose();
        _world.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long AcquireAndResolveMap()
    {
        using NavigationWorldGraphLease lease = _runtime.TryAcquire();
        lease.Graph.TryGetMap("map-000", out _);
        return lease.Graph.GraphVersion;
    }

    [Benchmark]
    public long PublishOnePhysicalCellChange()
    {
        if (_isBlocked)
        {
            _changedGrid.TryRemoveObstacle(_changedVoxel, _obstacle);
        }
        else
        {
            _obstacle = _world.AllocateObstacleToken();
            _changedGrid.TryAddObstacle(_changedVoxel, _obstacle);
        }
        _isBlocked = !_isBlocked;
        _runtime.Maintain(++_frame);
        return _runtime.Current.GraphVersion;
    }

    internal static TrailblazerWorldContextSettings CreateSettings()
    {
        var operations = new NavigationOperationLimits(
            maxPendingOperations: 256,
            maxPendingDescriptorBytes: 64_000_000,
            maxPreparedMapBytes: 64_000_000,
            maxBatchItems: 256,
            maxBatchDescriptorBytes: 64_000_000,
            maxBatchSortScratchBytes: 64_000_000,
            maxCorridorCells: 64,
            maxMaps: 128,
            maxRetainedMapIdentities: 128,
            maxOverlayCellsPerMap: 256,
            maxOverlayConnectionsPerMap: 256,
            maxOverlayTransitionsPerMap: 256,
            maxOverlayCells: 32_768,
            maxOverlayConnections: 32_768,
            maxOverlayTransitions: 32_768);
        return new TrailblazerWorldContextSettings(
            operations,
            new MaintenanceWorkBudget(
                4_096,
                65_536,
                65_536,
                65_536,
                65_536,
                65_536,
                65_536),
            maxIngressEntries: 4_096,
            maxIngressBytes: 4_096 * 256L,
            maxActiveSnapshots: 3,
            maxActiveSnapshotBytes: 128_000_000,
            maxRetiredSnapshots: 8,
            maxRetiredSnapshotBytes: 256_000_000,
            maxPersistentGraphPages: 65_536,
            maxDynamicCellSlotsPerMap: 256,
            maxDynamicCellSlots: 32_768,
            navigationAreaCount: 1,
            maxAreaPolicies: 64,
            maxAreaRulesPerPolicy: 4_096,
            maxAreaRules: 65_536,
            maxConcurrentSnapshotLeases: 8,
            queryLimits: NavigationQueryLimits.Default);
    }
}
