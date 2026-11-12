//=======================================================================
// NavigatorPathSessionRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

/// <summary>
/// Records durable path intent while rebuilding the transient start from the loaded navigator frame.
/// </summary>
internal sealed class NavigatorPathSessionRecord : IRecordable
{
    private const int CurrentSchemaVersion = 1;

    public int SchemaVersion;

    public bool HasQuery;

    public string? StartMapId;

    public EndpointResolutionPolicy StartResolution;

    public Fixed64 StartMaxResolutionDistance;

    public Vector3d EndPosition;

    public string? EndMapId;

    public EndpointResolutionPolicy EndResolution;

    public Fixed64 EndMaxResolutionDistance;

    public string? AreaPolicyId;

    public long AreaPolicyRevision;

    public TraversalMedia TargetMedia;

    public PathAlgorithm Algorithm;

    private NavigationWorkBudgetRecord _budget;

    public bool AllowTransitions;

    public Fixed64 ExtraIntegrationCost;

    public void Capture(PathQuery? query)
    {
        SchemaVersion = CurrentSchemaVersion;
        if (!query.HasValue)
            return;

        PathQuery value = query.Value;
        HasQuery = true;
        StartMapId = value.Start.MapId;
        StartResolution = value.Start.Resolution;
        StartMaxResolutionDistance = value.Start.MaxResolutionDistance;
        EndPosition = value.End.Position;
        EndMapId = value.End.MapId;
        EndResolution = value.End.Resolution;
        EndMaxResolutionDistance = value.End.MaxResolutionDistance;
        AreaPolicyId = value.AreaPolicy.PolicyId;
        AreaPolicyRevision = value.AreaPolicy.Revision;
        TargetMedia = value.Traversal.TargetMedia;
        Algorithm = value.Algorithm;
        _budget = new NavigationWorkBudgetRecord(value.Budget);
        AllowTransitions = value.AllowTransitions;
        ExtraIntegrationCost = value.FlowField.ExtraIntegrationCost;
    }

    public bool TryCreateQuery(
        Vector3d startPosition,
        TraversalMedium startMedium,
        NavigationAgentProfile agent,
        out PathQuery? query)
    {
        query = null;
        if (SchemaVersion != CurrentSchemaVersion)
            return false;
        if (!HasQuery)
            return true;
        if (Algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField
            || TargetMedia == TraversalMedia.None
            || (TargetMedia & ~NavigationCell.KnownMedia) != 0
            || string.IsNullOrWhiteSpace(AreaPolicyId))
        {
            return false;
        }

        try
        {
            query = new PathQuery(
                new NavigationEndpoint(
                    startPosition,
                    StartMapId,
                    StartResolution,
                    StartMaxResolutionDistance),
                new NavigationEndpoint(
                    EndPosition,
                    EndMapId,
                    EndResolution,
                    EndMaxResolutionDistance),
                agent,
                new NavigationAreaPolicyKey(AreaPolicyId!, AreaPolicyRevision),
                new TraversalIntent(startMedium, TargetMedia),
                Algorithm,
                _budget.CreateBudget(),
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

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref SchemaVersion, "SchemaVersion", 0);
        RecordValues.Look(chronicler, ref HasQuery, "HasQuery", false);
        RecordValues.Look(chronicler, ref StartMapId, "StartMapId", null);
        RecordValues.Look(chronicler, ref StartResolution, "StartResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref StartMaxResolutionDistance, "StartMaxResolutionDistance", Fixed64.Zero);
        RecordValues.Look(chronicler, ref EndPosition, "EndPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref EndMapId, "EndMapId", null);
        RecordValues.Look(chronicler, ref EndResolution, "EndResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref EndMaxResolutionDistance, "EndMaxResolutionDistance", Fixed64.Zero);
        RecordValues.Look(chronicler, ref AreaPolicyId, "AreaPolicyId", null);
        RecordValues.Look(chronicler, ref AreaPolicyRevision, "AreaPolicyRevision", 0L);
        RecordValues.Look(chronicler, ref TargetMedia, "TargetMedia", TraversalMedia.None);
        RecordValues.Look(chronicler, ref Algorithm, "Algorithm", PathAlgorithm.AStar);
        _budget.Look(chronicler);
        RecordValues.Look(chronicler, ref AllowTransitions, "AllowTransitions", false);
        RecordValues.Look(chronicler, ref ExtraIntegrationCost, "ExtraIntegrationCost", Fixed64.Zero);
    }
}
