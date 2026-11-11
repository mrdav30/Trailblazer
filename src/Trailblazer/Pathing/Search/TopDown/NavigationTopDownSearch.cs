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
internal sealed partial class NavigationTopDownSearch
{
    private const int CrossMapEdgeOrdinal = 6;

    private readonly GridWorld _world;
    private readonly NavigationPlanarWork _proof;
    private readonly NavigationMapInstance _map;
    private readonly NavigationPlanarBinding _binding;
    private readonly NavigationTopDownRouteRequest _request;
    private readonly NavigationAreaPolicy _policy;
    private readonly NavigationWorkMeter _meter;
    private readonly TraversalMedia _media;
    private readonly int _mapOrdinal;
    private int _layerCount;
    private int _direction;
    private bool _incoming;
    private VoxelIndex _current;
    private NavigationMapInstance _currentMap = null!;
    private int _currentMapOrdinal;
    private GridTopologyKind _topology;
    private int _nextCrossLayer;
    private NavigationMapInstance? _crossMap;
    private GridPlanarLayer _crossLayer;
    private bool _crossLayerBound;

    internal NavigationTopDownSearch(GridWorld world, NavigationWorldGraph graph, NavigationPlanarWork proof,
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
    }

    internal NavigationWorldGraph Graph { get; }

    internal NavigationGuideStatus BindSource()
    {
        if (!_map.Map.ContainsGridIndex(_request.Start.Index))
            return NavigationGuideStatus.InvalidStart;
        _map.Map.GridBinding.TryGetCellPrism(_request.Start.Index, out GridCellPrism source);
        Vector3d point = new(source.Center.X, _binding.EmbeddingY, source.Center.Z);
        return _proof.BindLayers(_world, Graph, _map, _binding, point, _meter, out _layerCount);
    }

    internal NavigationGuideStatus ResolveStart(out NavigationResolvedEndpoint endpoint) =>
        Resolve(_map, _mapOrdinal, _request.Start, NavigationGuideStatus.InvalidStart, out endpoint);

    internal NavigationGuideStatus ResolveEnd(out NavigationResolvedEndpoint endpoint)
    {
        endpoint = default;
        if (!NavigationPlanarWork.TryGetMap(Graph, _request.End.MapId, out var map, out var binding))
            return NavigationGuideStatus.NoMap;
        if (binding.EmbeddingY != _binding.EmbeddingY)
            return NavigationGuideStatus.NoPath;
        return Resolve(map, Graph.FindMapOrdinal(map.MapId), _request.End, NavigationGuideStatus.InvalidEnd,
            out endpoint);
    }

    private NavigationGuideStatus Resolve(NavigationMapInstance map, int mapOrdinal,
        NavigationCellAddress address, NavigationGuideStatus blocked,
        out NavigationResolvedEndpoint endpoint)
    {
        endpoint = default;
        if (!_meter.TryConsumeEndpointCandidates(1))
            return NavigationGuideStatus.BudgetExceeded;
        if (!TryGetNode(map, mapOrdinal, address.Index, out var node))
            return blocked;
        map.Map.GridBinding.TryGetCellPrism(address.Index, out GridCellPrism prism);
        Vector2d center = prism.Center.ToVector2d();
        NavigationGuideStatus status = Trace(map, address.Index, map, address.Index, center, center, blocked);
        if (status == NavigationGuideStatus.Success)
            endpoint = new NavigationResolvedEndpoint(node, address, _media, _request.Medium,
                new Vector3d(center.X, _binding.EmbeddingY, center.Y), Fixed64.Zero);
        return status;
    }

    internal void BeginEdges(NavigationMediumStateRef current, bool incoming)
    {
        _currentMapOrdinal = current.Node.MapOrdinal;
        _currentMap = Graph.GetInstance(_currentMapOrdinal);
        _currentMap.TryGetSlotIndex(current.Node.CellSlot, out _current);
        _topology = _currentMap.Map.GridBinding.Configuration.TopologyKind;
        _direction = 0;
        _incoming = incoming;
        _nextCrossLayer = _layerCount <= 1 ? _layerCount : 0;
        _crossMap = null;
        BeginTransitionEdges(current);
    }

    internal NavigationTraversalEdgeAdvanceStatus AdvanceEdge(ref NavigationAcquisitionSlice remaining,
        out NavigationMediumStateRef neighbor, out Fixed64 cost, out int ordinal, out NavigationTraversalEdgeKind kind)
    {
        if (remaining.LookupRemaining == 0 || remaining.EdgeRemaining == 0)
        {
            neighbor = default;
            cost = default;
            ordinal = default;
            kind = default;
            return NavigationTraversalEdgeAdvanceStatus.Blocked;
        }
        int lookups = _meter.LookupProbes;
        int edges = _meter.EvaluatedEdges;
        var status = AdvanceCandidate(out neighbor, out cost, out ordinal, out kind);
        NavigationAcquisitionSlice.ConsumeAtomicLookups(ref remaining.LookupRemaining, _meter, lookups);
        remaining.EdgeRemaining -= _meter.EvaluatedEdges - edges;
        return status;
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceCandidate(out NavigationMediumStateRef neighbor,
        out Fixed64 cost, out int ordinal, out NavigationTraversalEdgeKind kind)
    {
        neighbor = default;
        cost = default;
        ordinal = _direction;
        kind = NavigationTraversalEdgeKind.Surface;
        if (_direction == NavigationMap.GetNativeSurfaceDirectionCount(_topology))
        {
            ordinal = CrossMapEdgeOrdinal;
            var status = AdvanceCrossMapEdge(out neighbor, out cost);
            if (status != NavigationTraversalEdgeAdvanceStatus.Complete)
                return status;
            kind = NavigationTraversalEdgeKind.Transition;
            return AdvanceTransitionEdge(out neighbor, out cost, out ordinal);
        }
        if (!_meter.TryConsumeLookupProbes(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        VoxelIndex offset = NavigationMap.GetNativeSurfaceOffset(_topology, _direction++);
        int sign = _incoming ? -1 : 1;
        var adjacent = new VoxelIndex(_current.x + sign * offset.x, _current.y, _current.z + sign * offset.z);
        if (!TryGetNode(_currentMap, _currentMapOrdinal, adjacent, out NavigationNodeRef node))
            return NavigationTraversalEdgeAdvanceStatus.Pending;

        return EvaluateEdge(_currentMap, adjacent, node, out neighbor, out cost);
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceCrossMapEdge(out NavigationMediumStateRef neighbor,
        out Fixed64 cost)
    {
        neighbor = default;
        cost = default;
        if (_crossMap == null)
        {
            if (_nextCrossLayer == _layerCount)
                return NavigationTraversalEdgeAdvanceStatus.Complete;
            if (!_meter.TryConsumeLookupProbes(1))
                return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

            _crossLayer = _proof.GetBoundLayer(_nextCrossLayer++);
            Graph.TryGetMapId(_crossLayer.Generation.ConfigurationKey, out string mapId);
            if (mapId == _currentMap.MapId)
                return NavigationTraversalEdgeAdvanceStatus.Pending;
            Graph.TryGetMap(mapId, out var candidate);
            var currentGrid = _currentMap.Map.GridBinding.Configuration;
            var targetGrid = candidate.Map.GridBinding.Configuration;
            if (!NavigationPlanarWork.AreHorizontalLatticesCompatible(currentGrid, targetGrid))
                return NavigationTraversalEdgeAdvanceStatus.Pending;

            _currentMap.Map.GridBinding.TryGetCellPrism(_current, out var prism);
            candidate.Map.GridBinding.TryGetCellPrism(new VoxelIndex(0, _crossLayer.LayerIndex, 0), out var layerPrism);
            if (_proof.BeginNeighborCandidates(_world, _crossLayer, currentGrid, targetGrid, prism.Center, layerPrism.Center.Y))
            {
                _crossMap = candidate;
                _crossLayerBound = false;
            }
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        }

        if (!_crossLayerBound)
        {
            _crossLayerBound = true;
            return _proof.BindNeighborCandidateLayer(_world, _crossLayer, _meter);
        }

        NavigationMapInstance map = _crossMap;
        NavigationTraversalEdgeAdvanceStatus status = _proof.ReadNeighborCandidate(_world, _meter,
            out var candidateAddress, out bool complete);
        if (complete)
            _crossMap = null;
        if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
            return status;

        VoxelIndex index = candidateAddress.VoxelIndex;
        if (!TryGetNode(map, Graph.FindMapOrdinal(map.MapId), index, out NavigationNodeRef node))
        {
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        }
        return EvaluateEdge(map, index, node, out neighbor, out cost);
    }

    private NavigationTraversalEdgeAdvanceStatus EvaluateEdge(NavigationMapInstance adjacentMap, VoxelIndex adjacent,
        NavigationNodeRef node, out NavigationMediumStateRef neighbor, out Fixed64 cost)
    {
        neighbor = default;
        cost = default;
        if (!_meter.TryConsumeEvaluatedEdges(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        NavigationMapInstance fromMap = _incoming ? adjacentMap : _currentMap;
        NavigationMapInstance toMap = _incoming ? _currentMap : adjacentMap;
        VoxelIndex from = _incoming ? adjacent : _current;
        VoxelIndex to = _incoming ? _current : adjacent;
        fromMap.Map.GridBinding.TryGetCellPrism(from, out GridCellPrism source);
        toMap.Map.GridBinding.TryGetCellPrism(to, out GridCellPrism destination);
        Vector2d start = source.Center.ToVector2d();
        Vector2d end = destination.Center.ToVector2d();
        if (!NavigationPlanarWork.IsOrdinaryNeighbor(_topology, start, end))
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        NavigationGuideStatus status = Trace(fromMap, from, toMap, to, start, end, NavigationGuideStatus.NoPath);
        if (status != NavigationGuideStatus.Success)
            return MapTraceFailure(status);

        toMap.TryGetSlot(to, out int slot);
        toMap.TryGetEffectiveCell(slot, out NavigationCell cell);
        NavigationAreaRule rule = _policy.GetRequiredRule(cell.Area);
        if (!NavigationVolumeEdgeEvaluator.TryGetCost(new Vector3d(start.X, Fixed64.Zero, start.Y),
                new Vector3d(end.X, Fixed64.Zero, end.Y), cell.EnterCost, rule.AdditionalEnterCost, out cost))
            return NavigationTraversalEdgeAdvanceStatus.CostOverflow;

        neighbor = new NavigationMediumStateRef(node, _request.Medium);
        return NavigationTraversalEdgeAdvanceStatus.Edge;
    }

    private static NavigationTraversalEdgeAdvanceStatus MapTraceFailure(NavigationGuideStatus status) => status switch
    {
        NavigationGuideStatus.NoPath => NavigationTraversalEdgeAdvanceStatus.Pending,
        NavigationGuideStatus.CapacityExceeded => NavigationTraversalEdgeAdvanceStatus.CapacityExceeded,
        NavigationGuideStatus.CostOverflow => NavigationTraversalEdgeAdvanceStatus.CostOverflow,
        _ => NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
    };

    internal bool TryGetNode(NavigationCellAddress address, out NavigationNodeRef node) =>
        TryGetNode(address, out _, out node);

    internal bool TryGetNode(NavigationCellAddress address, out NavigationMapInstance map, out NavigationNodeRef node)
    {
        node = default;
        return NavigationPlanarWork.TryGetMap(Graph, address.MapId, out map, out var binding)
            && binding.EmbeddingY == _binding.EmbeddingY
            && TryGetNode(map, Graph.FindMapOrdinal(map.MapId), address.Index, out node);
    }

    private static bool TryGetNode(NavigationMapInstance map, int ordinal, VoxelIndex index, out NavigationNodeRef node)
    {
        if (map.Map.ContainsGridIndex(index) && map.TryGetSlot(index, out int slot))
        {
            node = new NavigationNodeRef(ordinal, slot);
            return true;
        }
        node = default;
        return false;
    }

    private NavigationGuideStatus Trace(NavigationMapInstance fromMap, VoxelIndex from, NavigationMapInstance toMap,
        VoxelIndex to, Vector2d start, Vector2d end, NavigationGuideStatus blocked) =>
        _proof.TraceBody(_world, Graph, _layerCount, _binding,
        NavigationPlanarWork.Pin(fromMap, from), NavigationPlanarWork.Pin(toMap, to), start, end,
        _request.Radius, Fixed64.Zero, _media, _request.Capabilities, _policy, _meter, blocked);
}
