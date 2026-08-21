using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures immutable overlay preparation and bounded candidate folding.</summary>
[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
[BenchmarkCategory("Phase1", "Map", "Overlay")]
public class NavigationOperationFoldBenchmarks
{
    private NavigationCellOverlayOperation[] _changes;
    private NavigationOperationProcessor _processor;
    private long _sequence;
    private int _frame;

    /// <summary>Number of addressed cell operations prepared and folded.</summary>
    [Params(1, 64, 1_024, 100_000)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(100_000, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding);
        var map = new NavigationMapBuilder("map", binding).Build();
        _processor = new NavigationOperationProcessor(new NavigationOperationLimits(
            maxPendingOperations: 16,
            maxPendingDescriptorBytes: 16_000_000,
            maxPreparedMapBytes: 16_000_000,
            maxBatchItems: 16,
            maxBatchDescriptorBytes: 16_000_000,
            maxBatchSortScratchBytes: 16_000_000,
            maxCorridorCells: 64,
            maxMaps: 4,
            maxRetainedMapIdentities: 16,
            maxOverlayCellsPerMap: 100_000,
            maxOverlayConnectionsPerMap: 0,
            maxOverlayTransitionsPerMap: 0,
            maxOverlayCells: 100_000,
            maxOverlayConnections: 0,
            maxOverlayTransitions: 0,
            maxTransitionRulesPerMap: 1,
            maxTransitionRules: 1));
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 0);
        _processor.Admit(install);
        _processor.ProcessFrame(0);
        _sequence = 1;

        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero);
        _changes = new NavigationCellOverlayOperation[OperationCount];
        for (int i = 0; i < _changes.Length; i++)
            _changes[i] = NavigationCellOverlayOperation.Set(new VoxelIndex(i, 0, 0), cell);
    }

    [Benchmark]
    public long PrepareImmutableTransaction()
    {
        var delta = new NavigationMapOverlayDelta("map", _changes);
        var transaction = new NavigationOverlayTransaction(new[] { delta });
        return new PreparedNavigationOverlay(transaction).DescriptorBytes;
    }

    [Benchmark]
    public NavigationOperationStatus PrepareAdmitAndFold()
    {
        bool suppress = (_sequence & 1) == 0;
        for (int i = 0; i < _changes.Length; i++)
        {
            VoxelIndex index = new(i, 0, 0);
            _changes[i] = suppress
                ? NavigationCellOverlayOperation.Suppress(index)
                : NavigationCellOverlayOperation.RevertToBake(index);
        }

        var prepared = new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta("map", _changes) }));
        var operation = new NavigationOverlayCommitOperation(
            prepared,
            ++_sequence,
            ++_frame);
        _processor.Admit(operation);
        _processor.ProcessFrame(_frame);
        return operation.Receipt.Status;
    }
}
