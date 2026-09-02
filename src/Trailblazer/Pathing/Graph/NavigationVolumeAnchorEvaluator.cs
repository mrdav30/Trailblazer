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
        bool hasAddress = _graph.TryGetNodeAddress(
            node,
            out NavigationCellAddress address);
        bool hasState = _graph.TryGetRawNodeState(node, out NavigationNodeState state);
        System.Diagnostics.Debug.Assert(hasAddress && hasState,
            "Volume anchor evaluation receives a node owned by its immutable graph.");
        if (!state.TryGetCenteredVolumeFootAnchor(
                _profile.Shape.Height,
                out footAnchor))
        {
            return NavigationVolumeAnchorStatus.CostOverflow;
        }

        return Trace(
            address,
            address,
            footAnchor,
            footAnchor,
            requestedMedia,
            meter,
            dependencies,
            out qualifyingMedia);
    }

    internal NavigationVolumeAnchorStatus EvaluateSegment(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        Vector3d sourceFootAnchor,
        Vector3d targetFootAnchor,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies)
    {
        bool hasSourceAddress = _graph.TryGetNodeAddress(
            source.Node,
            out NavigationCellAddress sourceAddress);
        bool hasTargetAddress = _graph.TryGetNodeAddress(
            target.Node,
            out NavigationCellAddress targetAddress);
        System.Diagnostics.Debug.Assert(
            source.Medium == target.Medium && hasSourceAddress && hasTargetAddress);

        return Trace(
            sourceAddress,
            targetAddress,
            sourceFootAnchor,
            targetFootAnchor,
            NavigationCell.ToMedia(source.Medium),
            meter,
            dependencies,
            out _);
    }

    private NavigationVolumeAnchorStatus Trace(
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        Vector3d sourceFootAnchor,
        Vector3d targetFootAnchor,
        TraversalMedia requestedMedia,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        out TraversalMedia qualifyingMedia)
    {
        qualifyingMedia = TraversalMedia.None;
        ulong before = _world.ChangeSequence;
        _graph.TryGetMap(sourceAddress.MapId, out NavigationMapInstance? sourceInstance);
        _graph.TryGetMap(targetAddress.MapId, out NavigationMapInstance? targetInstance);
        System.Diagnostics.Debug.Assert(sourceInstance != null && targetInstance != null);

        NavigationGridGenerationIdentity sourceIdentity = sourceInstance!.GridIdentity;
        NavigationGridGenerationIdentity targetIdentity = targetInstance!.GridIdentity;
        var sourceCell = new WorldVoxelIndex(
            sourceIdentity.WorldSpawnToken,
            sourceIdentity.GridIndex,
            sourceIdentity.GridSpawnToken,
            sourceAddress.Index);
        var targetCell = new WorldVoxelIndex(
            targetIdentity.WorldSpawnToken,
            targetIdentity.GridIndex,
            targetIdentity.GridSpawnToken,
            targetAddress.Index);
        int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
        int addressLimit = Math.Min(
            _workspace.CoveredAddressCapacity,
            meter.RemainingCoveredVoxelIntervals);
        long candidateWorkLimit = meter.RemainingGridCandidateWork;
        meter.RecordVolumeUnionCheck();
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            sourceCell,
            targetCell,
            sourceFootAnchor,
            targetFootAnchor,
            _profile.Shape.Radius,
            _profile.Shape.Height,
            _workspace.BodyTraceCells,
            _workspace.BodyTraceScratch,
            gridLimit,
            addressLimit,
            _workspace.CoveredAddressCapacity,
            candidateWorkLimit);
        bool consumedLookups = meter.TryConsumeLookupProbes(report.GridCandidateCount);
        bool consumedAddresses =
            meter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount);
        System.Diagnostics.Debug.Assert(consumedLookups && consumedAddresses);
        System.Diagnostics.Debug.Assert(
            report.Status != GridNavigationBodyTraceStatus.OutputLimitExceeded,
            "the address ceiling never exceeds the shared body-trace output capacity");
        NavigationVolumeAnchorStatus traceStatus = ResolveTraceStatus(
            before,
            _world.ChangeSequence,
            report.Status,
            gridLimit,
            _workspace.MapCapacity,
            addressLimit,
            _workspace.CoveredAddressCapacity);
        if (traceStatus is not NavigationVolumeAnchorStatus.Success
            and not NavigationVolumeAnchorStatus.Unavailable)
        {
            return traceStatus;
        }

        qualifyingMedia = requestedMedia & TraversalMedia.AnyVolume;
        for (int i = 0; i < _workspace.BodyTraceCells.Count; i++)
        {
            GridNavigationBodyTraceCell traceCell = _workspace.BodyTraceCells[i];
            if (!_graph.TryGetMapId(traceCell.ConfigurationKey, out string mapId))
                return NavigationVolumeAnchorStatus.Stale;
            bool hasTraceInstance = _graph.TryGetMap(
                mapId,
                out NavigationMapInstance traceInstance);
            System.Diagnostics.Debug.Assert(
                hasTraceInstance,
                "the immutable configuration index and map directory are built and replaced together");
            if (!IsTraceGenerationCurrent(
                    traceInstance.GridIdentity.Matches(
                        traceCell.Cell.WorldSpawnToken,
                        traceCell.Cell.GridIndex,
                        traceCell.Cell.GridSpawnToken),
                    traceInstance.GridLastChangeSequence,
                    traceCell.GridLastChangeSequence))
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
        return ResolveFinalStatus(before, _world.ChangeSequence, result);
    }

    internal static bool IsTraceGenerationCurrent(
        bool identityMatches,
        ulong instanceLastChangeSequence,
        ulong traceLastChangeSequence) =>
        identityMatches && instanceLastChangeSequence == traceLastChangeSequence;

    internal static NavigationVolumeAnchorStatus ResolveTraceStatus(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        GridNavigationBodyTraceStatus status,
        int gridLimit,
        int mapCapacity,
        int addressLimit,
        int coveredAddressCapacity)
    {
        if (worldSequenceBefore != worldSequenceAfter)
            return NavigationVolumeAnchorStatus.Stale;
        return status switch
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
                gridLimit < mapCapacity
                    ? NavigationVolumeAnchorStatus.BudgetExceeded
                    : NavigationVolumeAnchorStatus.CapacityExceeded,
            GridNavigationBodyTraceStatus.AddressLimitExceeded =>
                addressLimit < coveredAddressCapacity
                    ? NavigationVolumeAnchorStatus.BudgetExceeded
                    : NavigationVolumeAnchorStatus.CapacityExceeded,
            _ => NavigationVolumeAnchorStatus.BudgetExceeded
        };
    }

    internal static NavigationVolumeAnchorStatus ResolveFinalStatus(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        NavigationVolumeAnchorStatus result) =>
        worldSequenceBefore == worldSequenceAfter
            ? result
            : NavigationVolumeAnchorStatus.Stale;

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
