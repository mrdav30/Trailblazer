//=======================================================================
// NavigationNativeSurfaceEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Enumerates physically present native surface neighbors in canonical address order.</summary>
internal ref struct NavigationNativeSurfaceEdgeEnumerator
{
    private static readonly RectangularDirection[] RectangularDirections =
    {
        RectangularDirection.West,
        RectangularDirection.South,
        RectangularDirection.North,
        RectangularDirection.East
    };

    private static readonly HexDirection[] HexDirections =
    {
        HexDirection.QNegative,
        HexDirection.QNegativeRPositive,
        HexDirection.RNegative,
        HexDirection.RPositive,
        HexDirection.QPositiveRNegative,
        HexDirection.QPositive
    };

    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationMapInstance? _instance;
    private readonly int _mapOrdinal;
    private readonly VoxelIndex _sourceIndex;
    private int _directionIndex;

    internal NavigationNativeSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        int mapOrdinal,
        NavigationMapInstance instance,
        VoxelIndex sourceIndex)
    {
        _graph = graph;
        _instance = instance;
        _mapOrdinal = mapOrdinal;
        _sourceIndex = sourceIndex;
        _directionIndex = 0;
        Current = default;
    }

    internal NavigationGraphEdge Current { get; private set; }

    internal bool MoveNext()
    {
        if (_graph == null || _instance == null)
            return false;

        GridTopologyKind topology = _instance.Map.GridBinding.Configuration.TopologyKind;
        int directionCount = topology == GridTopologyKind.RectangularPrism
            ? RectangularDirections.Length
            : topology == GridTopologyKind.HexPrism
                ? HexDirections.Length
                : 0;
        while (_directionIndex < directionCount)
        {
            VoxelIndex offset = GetOffset(topology, _directionIndex++);
            var targetIndex = new VoxelIndex(
                _sourceIndex.x + offset.x,
                _sourceIndex.y + offset.y,
                _sourceIndex.z + offset.z);
            if (!_instance.Map.GridBinding.IsValidIndex(targetIndex)
                || !_graph.TryGetNodeRef(_mapOrdinal, targetIndex, out NavigationNodeRef target)
                || !_graph.TryGetNodeState(target, out NavigationNodeState state)
                || !state.IsPresent)
            {
                continue;
            }

            Current = new NavigationGraphEdge(target, NavigationGraphEdgeKind.Native);
            return true;
        }

        Current = default;
        return false;
    }

    private static VoxelIndex GetOffset(GridTopologyKind topology, int directionIndex)
    {
        if (topology == GridTopologyKind.HexPrism)
            return HexDirectionUtility.GetOffset(HexDirections[directionIndex]);

        (int x, int y, int z) offset =
            RectangularDirectionUtility.Offsets[(int)RectangularDirections[directionIndex]];
        return new VoxelIndex(offset.x, offset.y, offset.z);
    }
}
