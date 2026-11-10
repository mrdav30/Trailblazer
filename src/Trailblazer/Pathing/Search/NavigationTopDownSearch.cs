//=======================================================================
// NavigationTopDownSearch.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Admits canonical planar endpoints and supplies swept native edges to either search.</summary>
internal sealed class NavigationTopDownSearch
{
    private readonly GridWorld _world;
    private readonly NavigationTopDownWork _proof;
    private readonly NavigationMapInstance _map;
    private readonly NavigationPlanarBinding _binding;
    private readonly NavigationTopDownRouteRequest _request;
    private readonly NavigationAreaPolicy _policy;
    private readonly NavigationWorkMeter _meter;
    private readonly TraversalMedia _media;
    private readonly int _mapOrdinal;
    private readonly GridTopologyKind _topology;
    private int _layerCount;
    private int _direction;
    private bool _incoming;
    private VoxelIndex _current;

    internal NavigationTopDownSearch(GridWorld world, NavigationWorldGraph graph, NavigationTopDownWork proof,
        NavigationMapInstance map, NavigationPlanarBinding binding, in NavigationTopDownRouteRequest request,
        NavigationAreaPolicy policy, NavigationWorkMeter meter)
    {
        _world = world;
        Graph = graph;
        _proof = proof;
        _map = map;
        _binding = binding;
        _request = request;
        _policy = policy;
        _meter = meter;
        _media = NavigationCell.ToMedia(request.Medium);
        _mapOrdinal = graph.FindMapOrdinal(map.MapId);
        _topology = map.Map.GridBinding.Configuration.TopologyKind;
    }

    internal NavigationWorldGraph Graph { get; }

    internal NavigationGuideStatus Admit(ulong sequence, NavigationResolvedPathQuery query)
    {
        if (!_map.Map.ContainsGridIndex(_request.Start.Index))
            return NavigationGuideStatus.InvalidStart;
        _map.Map.GridBinding.TryGetCellPrism(_request.Start.Index, out GridCellPrism source);
        Vector3d point = new(source.Center.X, _binding.EmbeddingY, source.Center.Z);
        NavigationGuideStatus status = _proof.BindLayers(_world, Graph, _map, _binding, point, _meter, out _layerCount);
        if (status != NavigationGuideStatus.Success)
            return status;
        status = Resolve(_request.Start, NavigationGuideStatus.InvalidStart, out var start);
        if (status != NavigationGuideStatus.Success)
            return status;
        status = Resolve(_request.End, NavigationGuideStatus.InvalidEnd, out var end);
        if (status == NavigationGuideStatus.Success)
            query.BindTopDown(this, start, end, _policy, _meter, sequence);
        return status;
    }

    private NavigationGuideStatus Resolve(NavigationCellAddress address, NavigationGuideStatus blocked,
        out NavigationResolvedEndpoint endpoint)
    {
        endpoint = default;
        if (!_meter.TryConsumeEndpointCandidates(1))
            return NavigationGuideStatus.BudgetExceeded;
        if (!TryGetNode(address.Index, out var node))
            return blocked;
        _map.Map.GridBinding.TryGetCellPrism(address.Index, out GridCellPrism prism);
        Vector2d center = prism.Center.ToVector2d();
        NavigationGuideStatus status = Trace(address.Index, address.Index, center, center, blocked);
        if (status == NavigationGuideStatus.Success)
            endpoint = new NavigationResolvedEndpoint(node, address, _media, _request.Medium,
                new Vector3d(center.X, _binding.EmbeddingY, center.Y), Fixed64.Zero);
        return status;
    }

    internal void BeginEdges(NavigationMediumStateRef current, bool incoming)
    {
        _map.TryGetSlotIndex(current.Node.CellSlot, out _current);
        _direction = 0;
        _incoming = incoming;
    }

    internal NavigationTraversalEdgeAdvanceStatus AdvanceEdge(out NavigationMediumStateRef neighbor,
        out Fixed64 cost, out int ordinal)
    {
        neighbor = default;
        cost = default;
        ordinal = _direction;
        if (_direction == NavigationMap.GetNativeSurfaceDirectionCount(_topology))
            return NavigationTraversalEdgeAdvanceStatus.Complete;
        if (!_meter.TryConsumeLookupProbes(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        VoxelIndex offset = NavigationMap.GetNativeSurfaceOffset(_topology, _direction++);
        int sign = _incoming ? -1 : 1;
        var adjacent = new VoxelIndex(_current.x + sign * offset.x, _current.y, _current.z + sign * offset.z);
        if (!TryGetNode(adjacent, out NavigationNodeRef node))
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        if (!_meter.TryConsumeEvaluatedEdges(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        VoxelIndex from = _incoming ? adjacent : _current;
        VoxelIndex to = _incoming ? _current : adjacent;
        _map.Map.GridBinding.TryGetCellPrism(from, out GridCellPrism source);
        _map.Map.GridBinding.TryGetCellPrism(to, out GridCellPrism destination);
        Vector2d start = source.Center.ToVector2d();
        Vector2d end = destination.Center.ToVector2d();
        NavigationGuideStatus status = Trace(from, to, start, end, NavigationGuideStatus.NoPath);
        if (status != NavigationGuideStatus.Success)
        {
            return status switch
            {
                NavigationGuideStatus.NoPath => NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationGuideStatus.CapacityExceeded => NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
                NavigationGuideStatus.CostOverflow => NavigationTraversalEdgeAdvanceStatus.CostOverflow,
                _ => NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
            };
        }

        _map.TryGetSlot(to, out int slot);
        _map.TryGetEffectiveCell(slot, out NavigationCell cell);
        NavigationAreaRule rule = _policy.GetRequiredRule(cell.Area);
        if (!NavigationVolumeEdgeEvaluator.TryGetCost(new Vector3d(start.X, Fixed64.Zero, start.Y),
                new Vector3d(end.X, Fixed64.Zero, end.Y), cell.EnterCost, rule.AdditionalEnterCost, out cost))
            return NavigationTraversalEdgeAdvanceStatus.CostOverflow;

        neighbor = new NavigationMediumStateRef(node, _request.Medium);
        return NavigationTraversalEdgeAdvanceStatus.Edge;
    }

    internal bool TryGetNode(VoxelIndex index, out NavigationNodeRef node)
    {
        if (_map.Map.ContainsGridIndex(index) && _map.TryGetSlot(index, out int slot))
        {
            node = new NavigationNodeRef(_mapOrdinal, slot);
            return true;
        }
        node = default;
        return false;
    }

    private NavigationGuideStatus Trace(VoxelIndex from, VoxelIndex to, Vector2d start, Vector2d end,
        NavigationGuideStatus blocked) => _proof.TraceCircle(_world, Graph, _layerCount, _binding.EmbeddingY,
        NavigationTopDownWork.Pin(_map, from), NavigationTopDownWork.Pin(_map, to), start, end,
        _request.Radius, _media, _request.Capabilities, _policy, _meter, blocked);
}
