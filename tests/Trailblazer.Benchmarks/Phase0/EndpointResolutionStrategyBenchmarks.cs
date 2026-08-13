using System;
using BenchmarkDotNet.Attributes;

namespace Trailblazer.Benchmarks.Phase0;

/// <summary>
/// Synthetic Phase 0 comparison of endpoint lookup strategies after the endpoint
/// footprint has already been converted to covered voxel addresses.
/// </summary>
/// <remarks>
/// The benchmark intentionally excludes GridForge footprint geometry and measures
/// only Trailblazer-side candidate discovery.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Phase0", "Endpoint", "Lookup")]
public class EndpointResolutionStrategyBenchmarks
{
    private const int BucketSize = 64;

    private int _addressVolume;
    private int _queryStart;
    private int _queryEndExclusive;
    private int[] _nodeAddresses;
    private int[] _ordinalByAddress;
    private int[] _occupiedBucketIds;
    private int[] _occupiedBucketStarts;

    /// <summary>Representative combinations of address volume, authored density, and overlap.</summary>
    [Params(
        EndpointLookupScenario.Dense4K_SingleOverlap,
        EndpointLookupScenario.Dense4K_SixteenOverlap,
        EndpointLookupScenario.Sparse64K_SingleOverlap,
        EndpointLookupScenario.Sparse64K_SixteenOverlap,
        EndpointLookupScenario.Dense1M_SixteenOverlap,
        EndpointLookupScenario.Sparse1M_SixteenOverlap)]
    public EndpointLookupScenario Scenario { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        ScenarioParameters parameters = ScenarioParameters.For(Scenario);
        _addressVolume = parameters.AddressVolume;

        int stride = 100 / parameters.AuthoredDensityPercent;
        int nodeCount = (_addressVolume + stride - 1) / stride;
        _nodeAddresses = new int[nodeCount];
        _ordinalByAddress = new int[_addressVolume];
        Array.Fill(_ordinalByAddress, -1);

        int ordinal = 0;
        for (int address = 0; address < _addressVolume; address += stride)
        {
            _nodeAddresses[ordinal] = address;
            _ordinalByAddress[address] = ordinal;
            ordinal++;
        }

        int alignedCenter = ((_addressVolume / 2) / stride) * stride;
        _queryStart = Math.Max(0, alignedCenter - (parameters.OverlapCount / 2));
        _queryEndExclusive = Math.Min(_addressVolume, _queryStart + parameters.OverlapCount);
        _queryStart = _queryEndExclusive - parameters.OverlapCount;

        BuildOccupiedBucketDirectory();

        int expected = CoveredVoxelAddressLookup();
        if (GraphScan() != expected || SpatialBucketIndexLookup() != expected)
        {
            throw new InvalidOperationException(
                "Preflight: endpoint lookup strategies did not return the same candidate count.");
        }
    }

    /// <summary>Scans every authored graph node and performs the endpoint-range test.</summary>
    [Benchmark(Baseline = true)]
    public int GraphScan()
    {
        int candidateCount = 0;
        for (int nodeIndex = 0; nodeIndex < _nodeAddresses.Length; nodeIndex++)
        {
            int address = _nodeAddresses[nodeIndex];
            if (address >= _queryStart && address < _queryEndExclusive)
                candidateCount++;
        }

        return candidateCount;
    }

    /// <summary>
    /// Uses the endpoint query's exact covered-address span with a direct ordinal table.
    /// </summary>
    [Benchmark]
    public int CoveredVoxelAddressLookup()
    {
        int candidateCount = 0;
        for (int address = _queryStart; address < _queryEndExclusive; address++)
        {
            if (_ordinalByAddress[address] >= 0)
                candidateCount++;
        }

        return candidateCount;
    }

    /// <summary>
    /// Uses a compact address bucket directory over the already sorted graph-node array.
    /// </summary>
    [Benchmark]
    public int SpatialBucketIndexLookup()
    {
        int firstBucket = _queryStart / BucketSize;
        int lastBucket = (_queryEndExclusive - 1) / BucketSize;
        int firstOccupiedBucket = LowerBound(_occupiedBucketIds, firstBucket);
        int lastOccupiedBucketExclusive = UpperBound(_occupiedBucketIds, lastBucket);
        if (firstOccupiedBucket == lastOccupiedBucketExclusive)
            return 0;

        int firstNode = _occupiedBucketStarts[firstOccupiedBucket];
        int lastNodeExclusive = _occupiedBucketStarts[lastOccupiedBucketExclusive];
        int candidateCount = 0;

        for (int nodeIndex = firstNode; nodeIndex < lastNodeExclusive; nodeIndex++)
        {
            int address = _nodeAddresses[nodeIndex];
            if (address >= _queryStart && address < _queryEndExclusive)
                candidateCount++;
        }

        return candidateCount;
    }

    private void BuildOccupiedBucketDirectory()
    {
        int occupiedBucketCount = 0;
        int previousBucket = -1;
        for (int nodeIndex = 0; nodeIndex < _nodeAddresses.Length; nodeIndex++)
        {
            int bucket = _nodeAddresses[nodeIndex] / BucketSize;
            if (bucket != previousBucket)
            {
                occupiedBucketCount++;
                previousBucket = bucket;
            }
        }

        _occupiedBucketIds = new int[occupiedBucketCount];
        _occupiedBucketStarts = new int[occupiedBucketCount + 1];
        int occupiedBucket = -1;
        previousBucket = -1;
        for (int nodeIndex = 0; nodeIndex < _nodeAddresses.Length; nodeIndex++)
        {
            int bucket = _nodeAddresses[nodeIndex] / BucketSize;
            if (bucket == previousBucket)
                continue;

            occupiedBucket++;
            _occupiedBucketIds[occupiedBucket] = bucket;
            _occupiedBucketStarts[occupiedBucket] = nodeIndex;
            previousBucket = bucket;
        }

        _occupiedBucketStarts[occupiedBucketCount] = _nodeAddresses.Length;
    }

    private static int LowerBound(int[] values, int target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (values[middle] < target)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int UpperBound(int[] values, int target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (values[middle] <= target)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private readonly struct ScenarioParameters
    {
        private ScenarioParameters(int addressVolume, int authoredDensityPercent, int overlapCount)
        {
            AddressVolume = addressVolume;
            AuthoredDensityPercent = authoredDensityPercent;
            OverlapCount = overlapCount;
        }

        public int AddressVolume { get; }

        public int AuthoredDensityPercent { get; }

        public int OverlapCount { get; }

        public static ScenarioParameters For(EndpointLookupScenario scenario)
        {
            return scenario switch
            {
                EndpointLookupScenario.Dense4K_SingleOverlap => new ScenarioParameters(4_096, 100, 1),
                EndpointLookupScenario.Dense4K_SixteenOverlap => new ScenarioParameters(4_096, 100, 16),
                EndpointLookupScenario.Sparse64K_SingleOverlap => new ScenarioParameters(65_536, 1, 1),
                EndpointLookupScenario.Sparse64K_SixteenOverlap => new ScenarioParameters(65_536, 1, 16),
                EndpointLookupScenario.Dense1M_SixteenOverlap => new ScenarioParameters(1_048_576, 100, 16),
                EndpointLookupScenario.Sparse1M_SixteenOverlap => new ScenarioParameters(1_048_576, 1, 16),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
            };
        }
    }
}

/// <summary>Named endpoint lookup workloads used by the Phase 0 strategy spike.</summary>
public enum EndpointLookupScenario
{
    Dense4K_SingleOverlap,
    Dense4K_SixteenOverlap,
    Sparse64K_SingleOverlap,
    Sparse64K_SixteenOverlap,
    Dense1M_SixteenOverlap,
    Sparse1M_SixteenOverlap
}
