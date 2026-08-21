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
        => TryGetNodeState(node, TraversalMedium.Solid, out state);

    internal bool TryGetNodeState(
        NavigationNodeRef node,
        TraversalMedium medium,
        out NavigationNodeState state)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out VoxelIndex index)
            || !NavigationCell.IsKnownMedium(medium)
            || IsSurfaceAddressClosed(
                new NavigationCellAddress(instance!.MapId, index),
                medium)
            || !TryGetRawNodeState(node, instance, index, out state))
        {
            state = default;
            return false;
        }

        return true;
    }

    internal bool TryGetRawNodeState(
        NavigationNodeRef node,
        out NavigationNodeState state)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out VoxelIndex index)
            || !TryGetRawNodeState(node, instance!, index, out state))
        {
            state = default;
            return false;
        }

        return true;
    }

    private static bool TryGetRawNodeState(
        NavigationNodeRef node,
        NavigationMapInstance instance,
        VoxelIndex index,
        out NavigationNodeState state)
    {
        if (!instance.TryGetEffectiveCell(node.CellSlot, out NavigationCell cell)
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

    internal int GetCompleteDirectionCount(NavigationNodeRef node)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out _))
            return 0;
        return instance!.Map.GridBinding.Configuration.TopologyKind
            == GridTopologyKind.HexPrism
                ? HexDirectionUtility.Offsets.Length
                : RectangularDirectionUtility.Offsets.Length;
    }

    internal bool TryGetStructuralCompleteMediumNeighbor(
        NavigationMediumStateRef source,
        int directionOrdinal,
        out NavigationMediumStateRef neighbor,
        out bool isPrimary)
    {
        if (!TryGetCompleteNeighbor(
                source.Node,
                directionOrdinal,
                out NavigationNodeRef neighborNode,
                out isPrimary)
            || !TryGetNodeAddress(neighborNode, out NavigationCellAddress address))
        {
            neighbor = default;
            return false;
        }
        return TryGetStructuralMediumStateRef(address, source.Medium, out neighbor);
    }

    internal bool TryGetCompleteNeighbor(
        NavigationNodeRef source,
        int directionOrdinal,
        out NavigationNodeRef neighbor,
        out bool isPrimary)
    {
        if (!TryGetNodeLocation(
                source,
                out NavigationMapInstance? instance,
                out VoxelIndex index))
        {
            neighbor = default;
            isPrimary = false;
            return false;
        }

        bool hex = instance!.Map.GridBinding.Configuration.TopologyKind
            == GridTopologyKind.HexPrism;
        int count = hex
            ? HexDirectionUtility.Offsets.Length
            : RectangularDirectionUtility.Offsets.Length;
        if ((uint)directionOrdinal >= (uint)count)
        {
            neighbor = default;
            isPrimary = false;
            return false;
        }

        VoxelIndex offset;
        if (hex)
        {
            HexDirection direction = HexDirectionUtility.All[directionOrdinal];
            offset = HexDirectionUtility.GetOffset(direction);
            isPrimary = HexDirectionUtility.IsPlanar(direction)
                || HexDirectionUtility.IsVertical(direction);
        }
        else
        {
            RectangularDirection direction = RectangularDirectionUtility.All[
                directionOrdinal];
            (int x, int y, int z) rectangular = RectangularDirectionUtility.Offsets[
                directionOrdinal];
            offset = new VoxelIndex(rectangular.x, rectangular.y, rectangular.z);
            isPrimary = RectangularDirectionUtility.IsPerpendicularNeighbor(direction);
        }

        var address = new NavigationCellAddress(
            instance.MapId,
            new VoxelIndex(index.x + offset.x, index.y + offset.y, index.z + offset.z));
        return TryGetNodeRef(address, out neighbor);
    }

    internal bool TryGetMediumStateRef(
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationMediumStateRef state)
    {
        if (!NavigationCell.IsKnownMedium(medium)
            || !TryGetNodeRef(address, out NavigationNodeRef node)
            || IsSurfaceAddressClosed(address, medium)
            || !TryGetNodeLocation(node, out NavigationMapInstance? instance, out _)
            || !instance!.TryGetEffectiveCell(node.CellSlot, out NavigationCell cell)
            || !instance.TryGetPhysicalState(node.CellSlot, out bool isPresent, out _)
            || !isPresent
            || !cell.SupportsMedium(medium))
        {
            state = default;
            return false;
        }

        state = new NavigationMediumStateRef(node, medium);
        return true;
    }

    internal bool TryGetStructuralMediumStateRef(
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationMediumStateRef state)
    {
        if (!NavigationCell.IsKnownMedium(medium)
            || !TryGetNodeRef(address, out NavigationNodeRef node)
            || !_instances.Get(node.MapOrdinal).TryGetEffectiveCell(
                node.CellSlot,
                out NavigationCell cell)
            || !cell.SupportsMedium(medium))
        {
            state = default;
            return false;
        }

        state = new NavigationMediumStateRef(node, medium);
        return true;
    }

    internal int GetPrimaryDirectionCount(NavigationNodeRef node)
    {
        if (!TryGetNodeLocation(node, out NavigationMapInstance? instance, out _))
            return 0;
        return instance!.Map.GridBinding.Configuration.TopologyKind
            == GridTopologyKind.HexPrism
                ? HexDirectionUtility.Primary.Length
                : RectangularDirectionUtility.Primary.Length;
    }

    internal bool TryGetStructuralPrimaryMediumNeighbor(
        NavigationMediumStateRef source,
        int directionOrdinal,
        out NavigationMediumStateRef neighbor)
    {
        if (!TryGetPrimaryNeighbor(
                source.Node,
                directionOrdinal,
                out NavigationNodeRef neighborNode)
            || !TryGetNodeAddress(neighborNode, out NavigationCellAddress address))
        {
            neighbor = default;
            return false;
        }
        return TryGetStructuralMediumStateRef(address, source.Medium, out neighbor);
    }

    internal bool TryGetPrimaryNeighbor(
        NavigationNodeRef source,
        int directionOrdinal,
        out NavigationNodeRef neighbor)
    {
        if (!TryGetNodeLocation(
                source,
                out NavigationMapInstance? instance,
                out VoxelIndex index))
        {
            neighbor = default;
            return false;
        }
        bool hex = instance!.Map.GridBinding.Configuration.TopologyKind
            == GridTopologyKind.HexPrism;
        int count = hex
            ? HexDirectionUtility.Primary.Length
            : RectangularDirectionUtility.Primary.Length;
        if ((uint)directionOrdinal >= (uint)count)
        {
            neighbor = default;
            return false;
        }
        VoxelIndex offset;
        if (hex)
        {
            offset = HexDirectionUtility.GetOffset(
                HexDirectionUtility.Primary[directionOrdinal]);
        }
        else
        {
            (int x, int y, int z) rectangular = RectangularDirectionUtility.Offsets[
                (int)RectangularDirectionUtility.Primary[directionOrdinal]];
            offset = new VoxelIndex(rectangular.x, rectangular.y, rectangular.z);
        }
        var address = new NavigationCellAddress(
            instance.MapId,
            new VoxelIndex(index.x + offset.x, index.y + offset.y, index.z + offset.z));
        return TryGetNodeRef(address, out neighbor);
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

    internal NavigationNativeSurfaceEdgeEnumerator EnumerateStructuralNativeSurfaceEdges(
        NavigationNodeRef source)
    {
        if (!TryGetNodeLocation(
                source,
                out NavigationMapInstance? instance,
                out VoxelIndex sourceIndex)
            || !instance!.TryGetEffectiveCell(source.CellSlot, out _))
        {
            return default;
        }
        return new NavigationNativeSurfaceEdgeEnumerator(
            this,
            source.MapOrdinal,
            instance,
            sourceIndex,
            structural: true);
    }

    internal NavigationSurfaceEdgeEnumerator EnumerateSurfaceEdges(
        NavigationNodeRef source) => new(
            this,
            source,
            incoming: false,
            includeNative: true,
            includeAutomaticSeams: true);

    internal NavigationIncomingSurfaceEdgeEnumerator EnumerateIncomingSurfaceEdges(
        NavigationNodeRef destination) => new(this, destination);

    internal NavigationSurfaceEdgeEnumerator EnumerateStructuralSurfaceEdges(
        NavigationNodeRef source) => new(
            this,
            source,
            incoming: false,
            includeNative: true,
            includeAutomaticSeams: true,
            structural: true);

    internal NavigationIncomingSurfaceEdgeEnumerator EnumerateIncomingStructuralSurfaceEdges(
        NavigationNodeRef destination) => new(this, destination, structural: true);

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
