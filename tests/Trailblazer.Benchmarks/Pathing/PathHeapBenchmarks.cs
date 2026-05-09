using BenchmarkDotNet.Attributes;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Benchmarks the shared path heap metadata store without full survey setup cost.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "Heap")]
public class PathHeapBenchmarks
{
    private const int ReplayNodeCount = 4096;

    private PathHeap<StructuredHeapNode> _heap;

    private StructuredHeapNode[] _replayNodes;

    private StructuredHeapNode[] _warmNodes;

    [Params(8192, 32768)]
    public int WarmupEntryTarget { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _heap = new PathHeap<StructuredHeapNode>();
        _replayNodes = BuildOpenPlaneNodes(64);
        _warmNodes = WarmupEntryTarget == 32768
            ? BuildOpenPlaneNodes(128)
            : _replayNodes;

        for (int i = 0; i < _warmNodes.Length; i++)
            _heap.Add(_warmNodes[i], pathCost: i);

        _heap.FastClear();
    }

    [Benchmark]
    public int MetadataReplay_Structured4096()
    {
        int checksum = 0;

        for (int i = 0; i < _replayNodes.Length; i++)
            _heap.Add(_replayNodes[i], pathCost: i);

        for (int i = 0; i < _replayNodes.Length; i++)
        {
            StructuredHeapNode node = _replayNodes[i];
            if (_heap.Contains(node)
                && _heap.TryGetPathCost(node, out int pathCost))
            {
                checksum += pathCost;
            }
        }

        while (_heap.RemoveFirst(out StructuredHeapNode node))
        {
            _heap.SetClosed(node);
            checksum++;
        }

        foreach (StructuredHeapNode _ in _heap.EnumerateClosed())
            checksum++;

        _heap.FastClear();
        return checksum;
    }

    private static StructuredHeapNode[] BuildOpenPlaneNodes(int size)
    {
        var nodes = new StructuredHeapNode[size * size];
        int index = 0;
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
                nodes[index++] = new StructuredHeapNode(x, z);
        }

        return nodes;
    }

    private sealed class StructuredHeapNode
    {
        private readonly int _x;

        private readonly int _z;

        public StructuredHeapNode(int x, int z)
        {
            _x = x;
            _z = z;
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + 1;
                hash = (hash * 31) + 1;
                hash = (hash * 31) + _x;
                hash = (hash * 31) + _z;
                return hash;
            }
        }
    }
}
