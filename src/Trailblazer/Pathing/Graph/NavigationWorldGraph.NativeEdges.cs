//=======================================================================
// NavigationWorldGraph.NativeEdges.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationWorldGraph
{
    internal bool TryGetNodeRef(
        int mapOrdinal,
        VoxelIndex index,
        out NavigationNodeRef node)
    {
        if ((uint)mapOrdinal >= (uint)_instances.Count)
        {
            node = default;
            return false;
        }

        NavigationMapInstance instance = _instances.Get(mapOrdinal);
        if (!instance.Map.GridBinding.IsValidIndex(index)
            || !instance.TryGetSlot(index, out int slot))
        {
            node = default;
            return false;
        }

        node = new NavigationNodeRef(mapOrdinal, slot);
        return node.IsValid;
    }

    internal bool TryGetNodeRef(
        NavigationCellAddress address,
        out NavigationNodeRef node)
    {
        int mapOrdinal = FindMapOrdinal(address.MapId);
        return TryGetNodeRef(mapOrdinal, address.Index, out node);
    }

    internal bool TryGetNodeAddress(
        NavigationNodeRef node,
        out NavigationCellAddress address)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out VoxelIndex index))
        {
            address = default;
            return false;
        }

        address = new NavigationCellAddress(instance!.MapId, index);
        return true;
    }

    internal bool TryGetNodeState(NavigationNodeRef node, out NavigationNodeState state)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out VoxelIndex index)
            || IsStructuralScopeClosed(instance!.MapId)
            || !instance.TryGetEffectiveCell(node.CellSlot, out NavigationCell cell)
            || !instance.Map.GridBinding.TryGetCellPrism(index, out GridCellPrism prism))
        {
            state = default;
            return false;
        }

        instance.TryGetPhysicalState(node.CellSlot, out bool isPresent, out byte obstacleCount);
        state = new NavigationNodeState(
            cell,
            isPresent,
            obstacleCount,
            prism.Center,
            new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z));
        return true;
    }

    internal NavigationNativeSurfaceEdgeEnumerator EnumerateNativeSurfaceEdges(
        NavigationNodeRef source)
    {
        if (!TryGetNodeLocation(
                source,
                out NavigationMapInstance? instance,
                out VoxelIndex sourceIndex)
            || !TryGetNodeState(source, out NavigationNodeState sourceState)
            || !sourceState.IsPresent)
        {
            return default;
        }

        return new NavigationNativeSurfaceEdgeEnumerator(
            this,
            source.MapOrdinal,
            instance!,
            sourceIndex);
    }

    internal NavigationSurfaceEdgeEnumerator EnumerateSurfaceEdges(
        NavigationNodeRef source) => new(
            this,
            source,
            incoming: false,
            includeNative: true,
            includeAutomaticSeams: true);

    internal NavigationSurfaceEdgeEnumerator EnumerateIncomingExplicitSurfaceEdges(
        NavigationNodeRef destination) => new(
            this,
            destination,
            incoming: true,
            includeNative: false,
            includeAutomaticSeams: false);

    private bool TryGetNodeLocation(
        NavigationNodeRef node,
        out NavigationMapInstance? instance,
        out VoxelIndex index)
    {
        if (!node.IsValid || (uint)node.MapOrdinal >= (uint)_instances.Count)
        {
            instance = null;
            index = default;
            return false;
        }

        instance = _instances.Get(node.MapOrdinal);
        if (instance.TryGetSlotIndex(node.CellSlot, out index))
            return true;

        instance = null;
        index = default;
        return false;
    }
}
