//=======================================================================
// NavigationRayWork.Direct.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Utility;

namespace Trailblazer.Pathing;

/// <content>Advances direct navigation ray traversal and cell mapping.</content>
internal sealed partial class NavigationRayWork
{
    /// <summary>May reject direct travel after a freshly validated complete separating slab.</summary>
    internal NavigationRayStatus AdvanceDirect(NavigationImmediateRayWorkspace immediate)
    {
        if (!_begun)
            return Status;

        NavigationWorkMeter meter = immediate.WorkMeter;
        try
        {
            // Capture before generation qualification; obstacle mutation is not excluded by the
            // tracer's world read lock. Never adopt a later stamp for an empty-slab proof.
            ulong before = _request.World.ChangeSequence;
            _worldChangeSequence = before;
            NavigationWorldGraph graph = _request.ExpectedGraph;
            int outputLimit = Math.Min(_workspace.TraceIntervalCapacity, meter.RemainingTraceIntervals);
            if (Medium != TraversalMedium.Solid
                || _request.EndpointAllowance != NavigationRayEndpointAllowance.None
                || _request.ChainConstraint.Kind != NavigationRayChainConstraintKind.Unrestricted
                || graph.MapCount != 1
                || graph.AutomaticSeams.PairCount != 0
                || !ReferenceEquals(graph, _request.Store.Current))
            {
                return Advance(meter);
            }

            NavigationMapInstance instance = graph.GetInstance(0);
            if (!graph.TryGetCoveredAddressGeneration(instance.MapId, out GridCoveredAddressGeneration generation)
                || instance.GridIdentity.WorldSpawnToken != _request.World.SpawnToken
                || graph.ExplicitConnections.GetSourceOwnerCount(instance.MapId) != 0
                || meter.RemainingLookupProbes < 1L + outputLimit
                || _workspace.Dependencies.Pages.Length < Math.Min(outputLimit, instance.PhysicalPageCount)
                || _workspace.Dependencies.Components.Length < Math.Min(outputLimit,
                    graph.SurfaceComponents.GetSolidComponentRecordCount(instance.MapId)))
            {
                return Advance(meter);
            }

            // Mapping debits lookup probes during observation. All geometric ceilings remain those
            // of this original attempt, and discovery charges are applied exactly once afterward.
            int gridLimit = Math.Min(_workspace.MapCapacity, meter.RemainingLookupProbes);
            int addressLimit = Math.Min(_workspace.CoveredAddressCapacity, meter.RemainingCoveredVoxelIntervals);
            long candidateWorkLimit = meter.RemainingGridCandidateWork;
            _activeMappingMemo = immediate.MappingMemo.TryPrepare(_request)
                ? immediate.MappingMemo : null;
            immediate.CertificateArmed = true;
            if (!GridTracer.TryTraceRectangularSlabsInto(
                    _request.World, _request.Start, _request.End, generation,
                    _workspace.TraceIntervals, _workspace.TraceScratch,
                    immediate.SourceOrdinals, immediate.ObserveSlab,
                    gridLimit, addressLimit, outputLimit, candidateWorkLimit,
                    out GridTraceIntervalReport report))
            {
                _activeMappingMemo = null;
                return Advance(meter);
            }

            ulong after = _request.World.ChangeSequence;
            meter.TryConsumeLookupProbes(report.GridCandidateCount);
            meter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount);
            meter.TryConsumeTraceIntervals(report.Status == GridTraceIntervalStatus.OutputLimitExceeded
                ? outputLimit : report.IntervalCount);
            _worldChangeSequence = after;
            if (before != after)
                return Finish(NavigationRayStatus.Stale);
            if (report.Status == GridTraceIntervalStatus.StoppedAfterCompleteSlab)
                return Finish(NavigationRayStatus.CertifiedObstruction);

            NavigationRayStatus traceStatus = ResolveTraceStatus(before, after, report.Status,
                gridLimit, _workspace.MapCapacity, addressLimit, _workspace.CoveredAddressCapacity,
                outputLimit, _workspace.TraceIntervalCapacity);
            if (traceStatus != NavigationRayStatus.Pending)
                return Finish(traceStatus);
            if (immediate.DeferredStatus != NavigationRayStatus.Pending)
                return Finish(immediate.DeferredStatus);

            ReorderDirectRecords(immediate.SourceOrdinals);
            GuideSampleWorkMeter unused = default;
            return EvaluateChain(meter, ref unused, useGuideMeter: false);
        }
        catch
        {
            Reset();
            throw;
        }
        finally
        {
            _activeMappingMemo = null;
            immediate.ClearDirect();
        }
    }

    internal bool ObserveDirectSlab(NavigationImmediateRayWorkspace immediate, GridTraceSlab slab)
    {
        immediate.UsedOrdinalCount = slab.IntervalStart + slab.IntervalCount;
        bool admitted = false;
        GuideSampleWorkMeter unused = default;
        for (int ordinal = slab.IntervalStart;
            ordinal < immediate.UsedOrdinalCount && immediate.DeferredStatus == NavigationRayStatus.Pending;
            ordinal++)
        {
            immediate.DeferredStatus = MapOne(ordinal, _workspace.TraceIntervals[ordinal],
                immediate.WorkMeter, ref unused, useGuideMeter: false);
            admitted |= _workspace.ChainRecords[ordinal].State == NavigationRayChainRecordState.Unreached;
        }

        immediate.CertificateArmed &= immediate.DeferredStatus == NavigationRayStatus.Pending;
        bool current = CheckDirectFence(immediate);
        if (!current || admitted || !slab.SeparatesEndpoints)
        {
            return true;
        }

        // Only a complete strict zero-admission slab pays accumulated dependency validation.
        if (!AreDependenciesCurrent())
        {
            immediate.DeferredStatus = NavigationRayStatus.Stale;
            immediate.CertificateArmed = false;
            return true;
        }
        return !CheckDirectFence(immediate);
    }

    private bool CheckDirectFence(NavigationImmediateRayWorkspace immediate)
    {
        // GridForge holds the world read lock for every observer: the qualified world/grid
        // lifetime cannot change here. Obstacle sequence and graph publication still can.
        if (_request.World.ChangeSequence != _worldChangeSequence)
        {
            immediate.DeferredStatus = NavigationRayStatus.Stale;
            immediate.CertificateArmed = false;
        }
        if (!ReferenceEquals(_request.ExpectedGraph, _request.Store.Current))
            immediate.CertificateArmed = false;
        return immediate.CertificateArmed;
    }

    private void ReorderDirectRecords(int[] sourceOrdinals)
    {
        // The canonical output tag says which emission-order record belongs at each destination.
        // Consume each permutation cycle in place; no remapping, replayed debit or second array.
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        for (int destination = 0; destination < _workspace.TraceIntervals.Count; destination++)
        {
            NavigationRayChainRecord saved = records[destination];
            int current = destination;
            while (sourceOrdinals[current] != current)
            {
                int source = sourceOrdinals[current];
                sourceOrdinals[current] = current;
                if (source == destination)
                {
                    records[current] = saved;
                    break;
                }
                records[current] = records[source];
                current = source;
            }
        }
    }
}
