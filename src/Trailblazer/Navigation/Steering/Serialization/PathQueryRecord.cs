//=======================================================================
// PathQueryRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

/// <summary>Records exact graph query intent and any A* guide-local cursor.</summary>
internal sealed class PathQueryRecord : IRecordable
{
    private const int NoWaypointIndex = -1;

    public bool HasQuery;

    public Vector3d StartPosition;

    public string? StartMapId;

    public EndpointResolutionPolicy StartResolution;

    public Fixed64 StartMaxResolutionDistance;

    public Vector3d EndPosition;

    public string? EndMapId;

    public EndpointResolutionPolicy EndResolution;

    public Fixed64 EndMaxResolutionDistance;

    public NavigationAgentProfileRecord Agent = new();

    public string? AreaPolicyId;

    public long AreaPolicyRevision;

    public TraversalDomain StartDomain;

    public TraversalMedium CurrentMedium;

    public TraversalDomain TargetDomain;

    public PathAlgorithm Algorithm;

    public int MaxLookupProbes;

    public int MaxEndpointCandidates;

    public int MaxExpandedNodes;

    public int MaxEvaluatedEdges;

    public int MaxConnectionLegs;

    public int MaxTransitionCandidates;

    public int MaxTransitionPairs;

    public int MaxStagedLegAttempts;

    public int MaxTraceIntervals;

    public int MaxCoveredVoxelIntervals;

    public int MaxSimplificationRays;

    public bool AllowTransitions;

    public Fixed64 ExtraIntegrationCost;

    public int WaypointIndex = NoWaypointIndex;

    public void Capture(PathQuery? query, NavigationGuideLease? guide)
    {
        if (!query.HasValue)
            return;

        PathQuery value = query.Value;
        HasQuery = true;
        StartPosition = value.Start.Position;
        StartMapId = value.Start.MapId;
        StartResolution = value.Start.Resolution;
        StartMaxResolutionDistance = value.Start.MaxResolutionDistance;
        EndPosition = value.End.Position;
        EndMapId = value.End.MapId;
        EndResolution = value.End.Resolution;
        EndMaxResolutionDistance = value.End.MaxResolutionDistance;
        Agent.Capture(value.Agent);
        AreaPolicyId = value.AreaPolicy.PolicyId;
        AreaPolicyRevision = value.AreaPolicy.Revision;
        StartDomain = value.Traversal.StartDomain;
        CurrentMedium = value.Traversal.CurrentMedium;
        TargetDomain = value.Traversal.TargetDomain;
        Algorithm = value.Algorithm;
        MaxLookupProbes = value.Budget.MaxLookupProbes;
        MaxEndpointCandidates = value.Budget.MaxEndpointCandidates;
        MaxExpandedNodes = value.Budget.MaxExpandedNodes;
        MaxEvaluatedEdges = value.Budget.MaxEvaluatedEdges;
        MaxConnectionLegs = value.Budget.MaxConnectionLegs;
        MaxTransitionCandidates = value.Budget.MaxTransitionCandidates;
        MaxTransitionPairs = value.Budget.MaxTransitionPairs;
        MaxStagedLegAttempts = value.Budget.MaxStagedLegAttempts;
        MaxTraceIntervals = value.Budget.MaxTraceIntervals;
        MaxCoveredVoxelIntervals = value.Budget.MaxCoveredVoxelIntervals;
        MaxSimplificationRays = value.Budget.MaxSimplificationRays;
        AllowTransitions = value.AllowTransitions;
        ExtraIntegrationCost = value.FlowField.ExtraIntegrationCost;
        WaypointIndex = guide?.CurrentWaypointIndex ?? NoWaypointIndex;
    }

    public bool TryCreateQuery(out PathQuery? query)
    {
        query = null;
        if (!HasQuery)
            return true;
        if (Algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField
            || StartDomain != TraversalDomain.Surface
            || TargetDomain != TraversalDomain.Surface
            || CurrentMedium is TraversalMedium.Gas or TraversalMedium.Liquid
            || (AllowTransitions && Algorithm != PathAlgorithm.FlowField)
            || string.IsNullOrWhiteSpace(AreaPolicyId)
            || !Agent.TryCreate(out NavigationAgentProfile profile))
        {
            return false;
        }

        try
        {
            query = new PathQuery(
                new NavigationEndpoint(
                    StartPosition,
                    StartMapId,
                    StartResolution,
                    StartMaxResolutionDistance),
                new NavigationEndpoint(
                    EndPosition,
                    EndMapId,
                    EndResolution,
                    EndMaxResolutionDistance),
                profile,
                new NavigationAreaPolicyKey(AreaPolicyId!, AreaPolicyRevision),
                new TraversalIntent(StartDomain, CurrentMedium, TargetDomain),
                Algorithm,
                new NavigationWorkBudget(
                    MaxLookupProbes,
                    MaxEndpointCandidates,
                    MaxExpandedNodes,
                    MaxEvaluatedEdges,
                    MaxConnectionLegs,
                    MaxTransitionCandidates,
                    MaxTransitionPairs,
                    MaxStagedLegAttempts,
                    MaxTraceIntervals,
                    MaxCoveredVoxelIntervals,
                    MaxSimplificationRays),
                AllowTransitions,
                Algorithm == PathAlgorithm.FlowField
                    ? new FlowFieldQueryOptions(ExtraIntegrationCost)
                    : default);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool TryCreateGuide(
        TrailblazerWorldContext context,
        PathQuery query,
        out NavigationGuideLease? guide)
    {
        guide = null;
        if (WaypointIndex < 0)
            return false;

        NavigationGuideStatus status = context.Guides.RequestGuide(query, out guide);
        if (status != NavigationGuideStatus.Success || !guide.HasValue)
            return false;
        NavigationGuideLease activeGuide = guide.Value;

        if ((uint)WaypointIndex >= (uint)activeGuide.WaypointCount)
        {
            activeGuide.Dispose();
            guide = null;
            return false;
        }

        for (int index = 0; index < WaypointIndex; index++)
        {
            status = activeGuide.TryAdvanceWaypoint();
            if (status == NavigationGuideStatus.Success)
                continue;

            activeGuide.Dispose();
            guide = null;
            return false;
        }

        if (activeGuide.CurrentWaypointIndex == WaypointIndex)
            return true;

        activeGuide.Dispose();
        guide = null;
        return false;
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref HasQuery, "HasQuery", false);
        RecordValues.Look(chronicler, ref StartPosition, "StartPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref StartMapId, "StartMapId", null);
        RecordValues.Look(chronicler, ref StartResolution, "StartResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref StartMaxResolutionDistance, "StartMaxResolutionDistance", Fixed64.Zero);
        RecordValues.Look(chronicler, ref EndPosition, "EndPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref EndMapId, "EndMapId", null);
        RecordValues.Look(chronicler, ref EndResolution, "EndResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref EndMaxResolutionDistance, "EndMaxResolutionDistance", Fixed64.Zero);
        RecordDeep.Look(chronicler, ref Agent, "Agent");
        RecordValues.Look(chronicler, ref AreaPolicyId, "AreaPolicyId", null);
        RecordValues.Look(chronicler, ref AreaPolicyRevision, "AreaPolicyRevision", 0L);
        RecordValues.Look(chronicler, ref StartDomain, "StartDomain", TraversalDomain.Automatic);
        RecordValues.Look(chronicler, ref CurrentMedium, "CurrentMedium", TraversalMedium.Unknown);
        RecordValues.Look(chronicler, ref TargetDomain, "TargetDomain", TraversalDomain.Automatic);
        RecordValues.Look(chronicler, ref Algorithm, "Algorithm", PathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref MaxLookupProbes, "MaxLookupProbes", 0);
        RecordValues.Look(chronicler, ref MaxEndpointCandidates, "MaxEndpointCandidates", 0);
        RecordValues.Look(chronicler, ref MaxExpandedNodes, "MaxExpandedNodes", 0);
        RecordValues.Look(chronicler, ref MaxEvaluatedEdges, "MaxEvaluatedEdges", 0);
        RecordValues.Look(chronicler, ref MaxConnectionLegs, "MaxConnectionLegs", 0);
        RecordValues.Look(chronicler, ref MaxTransitionCandidates, "MaxTransitionCandidates", 0);
        RecordValues.Look(chronicler, ref MaxTransitionPairs, "MaxTransitionPairs", 0);
        RecordValues.Look(chronicler, ref MaxStagedLegAttempts, "MaxStagedLegAttempts", 0);
        RecordValues.Look(chronicler, ref MaxTraceIntervals, "MaxTraceIntervals", 0);
        RecordValues.Look(chronicler, ref MaxCoveredVoxelIntervals, "MaxCoveredVoxelIntervals", 0);
        RecordValues.Look(chronicler, ref MaxSimplificationRays, "MaxSimplificationRays", 0);
        RecordValues.Look(chronicler, ref AllowTransitions, "AllowTransitions", false);
        RecordValues.Look(chronicler, ref ExtraIntegrationCost, "ExtraIntegrationCost", Fixed64.Zero);
        RecordValues.Look(chronicler, ref WaypointIndex, "WaypointIndex", NoWaypointIndex);
    }
}
