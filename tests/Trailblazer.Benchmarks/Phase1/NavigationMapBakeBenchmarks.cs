using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Phase1;

/// <summary>
/// Measures canonical immutable-map bake scaling, including retained map storage
/// and temporary normalization allocations reported by the memory diagnoser.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Phase1", "NavigationMap", "Bake")]
public class NavigationMapBakeBenchmarks
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    private NavigationMapBuilder _builder;

    /// <summary>Representative authored-cell counts from the Phase 1 performance matrix.</summary>
    [Params(1_000, 100_000, 1_000_000)]
    public int CellCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d((Fixed64)(CellCount - 1), Fixed64.Zero, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        _builder = new NavigationMapBuilder("benchmark", configuration);
        for (int x = CellCount - 1; x >= 0; x--)
            _builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
    }

    /// <summary>Builds the stable value representation from reverse materialization order.</summary>
    [Benchmark]
    public NavigationMap BuildCanonicalMap() => _builder.Build();
}

/// <summary>
/// Measures connection sort and exact corridor validation independent of input order.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Phase1", "NavigationMap", "ConnectionCanonicalization")]
public class NavigationConnectionCanonicalizationBenchmarks
{
    private const int ConnectionCount = 1_000;
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    private NavigationMapBuilder _forward;
    private NavigationMapBuilder _reverse;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        configuration.TryNormalize(out NormalizedGridConfiguration binding);
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex destination = new(1, 0, 0);
        Vector3d sourceAnchor = GetFootAnchor(binding, source);
        Vector3d destinationAnchor = GetFootAnchor(binding, destination);

        _forward = CreateBuilder(binding, source, destination);
        _reverse = CreateBuilder(binding, source, destination);
        for (int i = 0; i < ConnectionCount; i++)
        {
            _forward.AddConnection(CreateConnection(i, source, destination, sourceAnchor, destinationAnchor));
        }
        for (int i = ConnectionCount - 1; i >= 0; i--)
        {
            _reverse.AddConnection(CreateConnection(i, source, destination, sourceAnchor, destinationAnchor));
        }

        NavigationMap forward = _forward.Build();
        NavigationMap reverse = _reverse.Build();
        if (!forward.Equals(reverse) || forward.GetHashCode() != reverse.GetHashCode())
            throw new InvalidOperationException("Preflight: connection materialization order changed canonical output.");
    }

    [Benchmark(Baseline = true)]
    public NavigationMap BuildForwardMaterialization() => _forward.Build();

    [Benchmark]
    public NavigationMap BuildReverseMaterialization() => _reverse.Build();

    private static NavigationMapBuilder CreateBuilder(
        NormalizedGridConfiguration binding,
        VoxelIndex source,
        VoxelIndex destination) =>
        new NavigationMapBuilder("benchmark", binding)
            .AddCell(source, Cell)
            .AddCell(destination, Cell);

    private static NavigationConnection CreateConnection(
        int id,
        VoxelIndex source,
        VoxelIndex destination,
        Vector3d entry,
        Vector3d exit) =>
        new(
            id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            source,
            new NavigationCellAddress("benchmark", destination),
            entry,
            exit,
            Fixed64.Zero,
            Fixed64.One,
            isLowerBoundCertified: true);

    private static Vector3d GetFootAnchor(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism);
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }
}
