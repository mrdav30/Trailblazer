using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures bounded exact-address ingress coalescing and deterministic backlog drain.</summary>
[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
[BenchmarkCategory("Phase2", "Graph", "Ingress")]
public class NavigationIngressBenchmarks
{
    private NavigationGridChangeIngress _ingress;
    private GridEventInfo[] _events;
    private GridEventInfo[] _drain;
    private NavigationGridChangeScope[] _blockedScopes;

    [Params(1_000, 100_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(EventCount, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        _events = new GridEventInfo[EventCount];
        for (int i = 0; i < EventCount; i++)
        {
            _events[i] = new GridEventInfo(
                1,
                0,
                1,
                configuration,
                1,
                GridEventKind.SparseVoxelAdded,
                new VoxelIndex(i, 0, 0),
                default,
                default,
                new GridChangeStamp((ulong)i + 1, (ulong)i + 1),
                true,
                true,
                0);
        }
        _drain = new GridEventInfo[1_024];
        _blockedScopes = new NavigationGridChangeScope[1];
    }

    [IterationSetup]
    public void ResetIngress() => _ingress = new NavigationGridChangeIngress(EventCount);

    [Benchmark]
    public int EnqueueAndDrainUniqueFinalStates()
    {
        for (int i = 0; i < _events.Length; i++)
            _ingress.Enqueue(_events[i]);
        int total = 0;
        int blockedScopeCount;
        bool blockAll;
        do
        {
            total += _ingress.DetachInto(
                _drain,
                _blockedScopes,
                out blockedScopeCount,
                out blockAll);
        }
        while (blockedScopeCount > 0 || blockAll);
        return total;
    }
}
