//=======================================================================
// NavigationPlanarSearch.Goals.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

internal partial class NavigationPlanarSearch
{
    private NavigationMapInstance? _goalMap;
    private int _goalMapOrdinal;
    private Vector2d _goalCenter;

    internal Vector2d GoalCenter => _goalCenter;

    internal NavigationGuideStatus AdvanceGoalCandidates(NavigationResolvedEndpoint[] goals, ref int count)
    {
        if (_goalMap == null)
        {
            var status = BeginGoalCandidates();
            if (status != NavigationGuideStatus.Success)
                return status;
        }
        var read = _proof.ReadNeighborCandidate(_world, _meter, out var candidate, out bool complete);
        switch (read)
        {
            case NavigationTraversalEdgeAdvanceStatus.Stale:
                return NavigationGuideStatus.Stale;
            case NavigationTraversalEdgeAdvanceStatus.BudgetExceeded:
                return NavigationGuideStatus.BudgetExceeded;
            case NavigationTraversalEdgeAdvanceStatus.Edge:
                // The covered-address window is centered vertically on the selected prism layer.
                Debug.Assert(candidate.VoxelIndex.y == _request.End.Index.y);
                var address = new NavigationCellAddress(_goalMap!.MapId, candidate.VoxelIndex);
                var status = Resolve(_goalMap, _goalMapOrdinal, address, NavigationGuideStatus.InvalidEnd,
                    out var endpoint, (_goalCenter, _request.ArrivalRadius));
                if (status == NavigationGuideStatus.Success)
                {
                    if (count == goals.Length)
                        return NavigationGuideStatus.CapacityExceeded;
                    goals[count++] = endpoint;
                }
                else if (status != NavigationGuideStatus.InvalidEnd)
                    return status;
                break;
        }
        if (!complete)
            return NavigationGuideStatus.Pending;
        return count == 0 ? NavigationGuideStatus.InvalidEnd : NavigationGuideStatus.Success;
    }

    private NavigationGuideStatus BeginGoalCandidates()
    {
        if (!NavigationPlanarWork.TryGetMap(Graph, _request.End.MapId, out var map, out var binding, _binding.Mode))
            return NavigationGuideStatus.NoMap;
        if (binding.EmbeddingY != _binding.EmbeddingY)
            return NavigationGuideStatus.NoPath;
        var index = _request.End.Index;
        if (index.y != binding.LayerIndex || !map.Map.GridBinding.TryGetCellPrism(index, out var prism))
            return NavigationGuideStatus.InvalidEnd;

        Vector2d center = prism.Center.ToVector2d();
        Fixed64 radius = _request.ArrivalRadius;
        Vector2d extent = new(radius, radius);
        if (this is NavigationSideViewSearch sideView)
        {
            if (map.Map.TryGetPlanarSupport(index, out var support))
            {
                var status = NavigationPlanarWork.MaterializeSupportAnchor(support, sideView.Profile,
                    NavigationGuideStatus.InvalidEnd, out center);
                if (status != NavigationGuideStatus.Success)
                    return status;
            }
            // A support's midpoint lies in the owner footprint, while its capsule center can be outside.
            extent.X += sideView.Profile.Radius;
            extent.Y += sideView.Profile.Radius + sideView.Profile.HalfAxisCeiling;
        }
        bool hasGeneration = Graph.TryGetCoveredAddressGeneration(map.MapId, out var generation);
        Debug.Assert(hasGeneration);
        _proof.BeginGoalCandidates(_world, generation, center, prism.Center.Y, extent);
        var bound = _proof.BindNeighborCandidateLayer(_world, new GridPlanarLayer(generation, binding.LayerIndex), _meter);
        if (bound != NavigationTraversalEdgeAdvanceStatus.Pending)
            return bound == NavigationTraversalEdgeAdvanceStatus.Stale
                ? NavigationGuideStatus.Stale : NavigationGuideStatus.BudgetExceeded;

        _goalMap = map;
        _goalMapOrdinal = Graph.FindMapOrdinal(map.MapId);
        _goalCenter = center;
        return NavigationGuideStatus.Success;
    }
}
