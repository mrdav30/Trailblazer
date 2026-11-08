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
internal struct NavigationNativeSurfaceEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationMapInstance? _instance;
    private readonly int _mapOrdinal;
    private readonly VoxelIndex _sourceIndex;
    private readonly bool _structural;
    private int _directionIndex;

    internal NavigationNativeSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        int mapOrdinal,
        NavigationMapInstance instance,
        VoxelIndex sourceIndex,
        bool structural = false)
    {
        _graph = graph;
        _instance = instance;
        _mapOrdinal = mapOrdinal;
        _sourceIndex = sourceIndex;
        _structural = structural;
        _directionIndex = 0;
        Current = default;
    }

    internal NavigationGraphEdge Current { get; private set; }

    internal bool MoveNext()
    {
        if (_graph == null || _instance == null)
            return false;

        GridTopologyKind topology = _instance.Map.GridBinding.Configuration.TopologyKind;
        int directionCount = NavigationMap.GetNativeSurfaceDirectionCount(topology);
        while (_directionIndex < directionCount)
        {
            int directionIndex = _directionIndex++;
            VoxelIndex offset = NavigationMap.GetNativeSurfaceOffset(topology, directionIndex);
            var targetIndex = new VoxelIndex(
                _sourceIndex.x + offset.x,
                _sourceIndex.y + offset.y,
                _sourceIndex.z + offset.z);
            if (!_instance.Map.GridBinding.IsValidIndex(targetIndex)
                || !_graph.TryGetNodeRef(_mapOrdinal, targetIndex, out NavigationNodeRef target)
                || (_structural
                    ? !_instance.TryGetEffectiveCell(target.CellSlot, out _)
                    : !_graph.TryGetNodeState(target, out NavigationNodeState state)
                        || !state.IsPresent))
            {
                continue;
            }

            Current = new NavigationGraphEdge(
                target,
                NavigationGraphEdgeKind.Native,
                _instance.Map.GetNativePortalTemplate(directionIndex),
                directionIndex);
            return true;
        }

        Current = default;
        return false;
    }

    internal NavigationSurfaceEdgeAdvanceStatus AdvanceOne(
        MaintenanceWorkMeter meter,
        ref int edgeStepRemaining)
    {
        if (_graph == null || _instance == null)
            return NavigationSurfaceEdgeAdvanceStatus.Complete;
        GridTopologyKind topology = _instance.Map.GridBinding.Configuration.TopologyKind;
        int directionCount = NavigationMap.GetNativeSurfaceDirectionCount(topology);
        if (_directionIndex >= directionCount)
        {
            Current = default;
            return NavigationSurfaceEdgeAdvanceStatus.Complete;
        }
        if (edgeStepRemaining == 0 || !meter.TryConsumeSurfaceComponentEdges(1))
            return NavigationSurfaceEdgeAdvanceStatus.Blocked;
        edgeStepRemaining--;
        int directionIndex = _directionIndex++;
        VoxelIndex offset = NavigationMap.GetNativeSurfaceOffset(topology, directionIndex);
        var targetIndex = new VoxelIndex(
            _sourceIndex.x + offset.x,
            _sourceIndex.y + offset.y,
            _sourceIndex.z + offset.z);
        if (!_instance.Map.GridBinding.IsValidIndex(targetIndex)
            || !_graph.TryGetNodeRef(_mapOrdinal, targetIndex, out NavigationNodeRef target)
            || (_structural
                ? !_instance.TryGetEffectiveCell(target.CellSlot, out _)
                : !_graph.TryGetNodeState(target, out NavigationNodeState state)
                    || !state.IsPresent))
        {
            return NavigationSurfaceEdgeAdvanceStatus.Pending;
        }
        Current = new NavigationGraphEdge(
            target,
            NavigationGraphEdgeKind.Native,
            _instance.Map.GetNativePortalTemplate(directionIndex),
            directionIndex);
        return NavigationSurfaceEdgeAdvanceStatus.Edge;
    }
}
