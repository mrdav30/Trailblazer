//=======================================================================
// NavigationVolumeAnchorEvaluator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;

namespace Trailblazer.Pathing;

internal enum NavigationVolumeAnchorStatus : byte
{
    Success,
    Unavailable,
    BudgetExceeded,
    CostOverflow,
    CapacityExceeded,
    Stale
}

/// <summary>Certifies one centered volume-body placement through GridForge's prism union.</summary>
internal readonly struct NavigationVolumeAnchorEvaluator
{
    private readonly GridWorld _world;
    private readonly NavigationWorldGraph _graph;
    private readonly NavigationAgentProfile _profile;
    private readonly NavigationAreaPolicy _areaPolicy;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationVolumeAnchorEvaluator(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        NavigationRayWorkspace workspace)
    {
        _world = world;
        _graph = graph;
        _profile = profile;
        _areaPolicy = areaPolicy;
        _workspace = workspace;
    }

    internal NavigationVolumeAnchorStatus Evaluate(
        NavigationNodeRef node,
        TraversalMedia requestedMedia,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        out Vector3d footAnchor,
        out TraversalMedia qualifyingMedia)
    {
        footAnchor = default;
        qualifyingMedia = TraversalMedia.None;
        ulong before = _world.ChangeSequence;
        if (!_graph.TryGetNodeAddress(node, out NavigationCellAddress address)
            || !_graph.TryGetNodeState(node, out NavigationNodeState state))
        {
            return NavigationVolumeAnchorStatus.Stale;
        }
        if (!state.TryGetCenteredVolumeFootAnchor(
                _profile.Shape.Height,
                out footAnchor))
        {
            return NavigationVolumeAnchorStatus.CostOverflow;
        }
        if (!_graph.TryGetMap(address.MapId, out NavigationMapInstance? instance)
            || instance == null)
        {
            return NavigationVolumeAnchorStatus.Stale;
        }

        NavigationGridGenerationIdentity identity = instance.GridIdentity;
        var cell = new WorldVoxelIndex(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            address.Index);
        int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
        int addressLimit = Math.Min(
            _workspace.CoveredAddressCapacity,
            meter.RemainingCoveredVoxelIntervals);
        long candidateWorkLimit = checked((long)gridLimit + addressLimit);
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            footAnchor,
            footAnchor,
            _profile.Shape.Radius,
            _profile.Shape.Height,
            _workspace.BodyTraceCells,
            _workspace.BodyTraceScratch,
            gridLimit,
            addressLimit,
            _workspace.CoveredAddressCapacity,
            candidateWorkLimit);
        if (!meter.TryConsumeLookupProbes(report.GridCandidateCount)
            || !meter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount))
        {
            return NavigationVolumeAnchorStatus.BudgetExceeded;
        }
        if (before != _world.ChangeSequence)
            return NavigationVolumeAnchorStatus.Stale;
        NavigationVolumeAnchorStatus traceStatus = report.Status switch
        {
            GridNavigationBodyTraceStatus.Complete =>
                NavigationVolumeAnchorStatus.Success,
            GridNavigationBodyTraceStatus.IncompletePhysicalCoverage =>
                NavigationVolumeAnchorStatus.Unavailable,
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry =>
                NavigationVolumeAnchorStatus.Unavailable,
            GridNavigationBodyTraceStatus.ArithmeticOverflow =>
                NavigationVolumeAnchorStatus.CostOverflow,
            GridNavigationBodyTraceStatus.GridCandidateLimitExceeded =>
                gridLimit < _workspace.MapCapacity
                    ? NavigationVolumeAnchorStatus.BudgetExceeded
                    : NavigationVolumeAnchorStatus.CapacityExceeded,
            GridNavigationBodyTraceStatus.AddressLimitExceeded =>
                addressLimit < _workspace.CoveredAddressCapacity
                    ? NavigationVolumeAnchorStatus.BudgetExceeded
                    : NavigationVolumeAnchorStatus.CapacityExceeded,
            GridNavigationBodyTraceStatus.OutputLimitExceeded =>
                NavigationVolumeAnchorStatus.CapacityExceeded,
            GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded =>
                NavigationVolumeAnchorStatus.CapacityExceeded,
            _ => NavigationVolumeAnchorStatus.Stale
        };
        if (traceStatus is not NavigationVolumeAnchorStatus.Success
            and not NavigationVolumeAnchorStatus.Unavailable)
        {
            return traceStatus;
        }

        qualifyingMedia = requestedMedia & TraversalMedia.AnyVolume;
        for (int i = 0; i < _workspace.BodyTraceCells.Count; i++)
        {
            GridNavigationBodyTraceCell traceCell = _workspace.BodyTraceCells[i];
            if (!_graph.TryGetMapId(traceCell.ConfigurationKey, out string mapId)
                || !_graph.TryGetMap(mapId, out NavigationMapInstance? traceInstance)
                || traceInstance == null
                || !traceInstance.GridIdentity.Matches(
                    traceCell.Cell.WorldSpawnToken,
                    traceCell.Cell.GridIndex,
                    traceCell.Cell.GridSpawnToken)
                || traceInstance.GridHighWaterSequence != traceCell.GridHighWaterSequence)
            {
                return NavigationVolumeAnchorStatus.Stale;
            }

            var traceAddress = new NavigationCellAddress(mapId, traceCell.Cell.VoxelIndex);
            if (!_graph.TryGetNodeRef(traceAddress, out NavigationNodeRef traceNode))
                return NavigationVolumeAnchorStatus.Stale;
            if (!dependencies.TryRecordPage(
                    mapId,
                    traceNode.CellSlot / NavigationSemanticPage.SlotCount))
            {
                return NavigationVolumeAnchorStatus.CapacityExceeded;
            }
            if (traceCell.Role != GridNavigationBodyTraceCellRole.RequiredCoverage)
                continue;
            if (!traceCell.IsPhysicallyPresent)
            {
                qualifyingMedia = TraversalMedia.None;
                continue;
            }

            qualifyingMedia = FilterPassableMedia(traceNode, qualifyingMedia);
        }
        NavigationVolumeAnchorStatus result = traceStatus == NavigationVolumeAnchorStatus.Unavailable
            || qualifyingMedia == TraversalMedia.None
            ? NavigationVolumeAnchorStatus.Unavailable
            : NavigationVolumeAnchorStatus.Success;
        return before == _world.ChangeSequence
            ? result
            : NavigationVolumeAnchorStatus.Stale;
    }

    private TraversalMedia FilterPassableMedia(
        NavigationNodeRef node,
        TraversalMedia media)
    {
        if ((media & TraversalMedia.Gas) != 0
            && !new TraversalEvaluator(
                    _graph,
                    _profile,
                    _areaPolicy,
                    TraversalMedium.Gas)
                .TryGetPassableNodeState(node, out _))
        {
            media &= ~TraversalMedia.Gas;
        }
        if ((media & TraversalMedia.Liquid) != 0
            && !new TraversalEvaluator(
                    _graph,
                    _profile,
                    _areaPolicy,
                    TraversalMedium.Liquid)
                .TryGetPassableNodeState(node, out _))
        {
            media &= ~TraversalMedia.Liquid;
        }
        return media;
    }

}
