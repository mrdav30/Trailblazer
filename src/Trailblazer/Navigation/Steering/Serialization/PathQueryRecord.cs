//=======================================================================
// PathQueryRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Navigation;

namespace Trailblazer.Pathing;

/// <summary>Records exact durable graph-query intent.</summary>
public sealed class PathQueryRecord : IRecordable
{
    private const int CurrentSchemaVersion = 2;

    private int SchemaVersion;

    private Vector3d StartPosition;

    private string? StartMapId;

    private EndpointResolutionPolicy StartResolution;

    private Fixed64 StartMaxResolutionDistance;

    private Vector3d EndPosition;

    private string? EndMapId;

    private EndpointResolutionPolicy EndResolution;

    private Fixed64 EndMaxResolutionDistance;

    private Fixed64 ArrivalRadius;

    private NavigationAgentProfileRecord Agent = new();

    private string? AreaPolicyId;

    private long AreaPolicyRevision;

    private TraversalMedium StartMedium;

    private TraversalMedia TargetMedia;

    private PathAlgorithm Algorithm;

    private NavigationWorkBudgetRecord _budget;

    private bool AllowTransitions;

    private Fixed64 ExtraIntegrationCost;

    /// <summary>Creates an empty shell for a populate-existing-instance Chronicler load.</summary>
    public PathQueryRecord()
    {
    }

    /// <summary>Creates a record containing one exact immutable query.</summary>
    public PathQueryRecord(PathQuery query) => Capture(query);

    /// <summary>Gets the exact query after construction or a successful load.</summary>
    public PathQuery? Query { get; private set; }

    private void Capture(PathQuery query)
    {
        SchemaVersion = CurrentSchemaVersion;
        PathQuery value = query;
        Query = value;
        StartPosition = value.Start.Position;
        StartMapId = value.Start.MapId;
        StartResolution = value.Start.Resolution;
        StartMaxResolutionDistance = value.Start.MaxResolutionDistance;
        EndPosition = value.End.Position;
        EndMapId = value.End.MapId;
        EndResolution = value.End.Resolution;
        EndMaxResolutionDistance = value.End.MaxResolutionDistance;
        ArrivalRadius = value.ArrivalRadius.GetValueOrDefault();
        Agent.Capture(value.Agent);
        AreaPolicyId = value.AreaPolicy.PolicyId;
        AreaPolicyRevision = value.AreaPolicy.Revision;
        StartMedium = value.Traversal.StartMedium;
        TargetMedia = value.Traversal.TargetMedia;
        Algorithm = value.Algorithm;
        _budget = new NavigationWorkBudgetRecord(value.Budget);
        AllowTransitions = value.AllowTransitions;
        ExtraIntegrationCost = value.FlowField.ExtraIntegrationCost;
    }

    private bool TryCreateQuery(out PathQuery? query)
    {
        query = null;
        if (SchemaVersion != CurrentSchemaVersion
            || Algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField
            || !TraversalTransitionDefinition.IsKnownMedium(StartMedium)
            || TargetMedia == TraversalMedia.None
            || (TargetMedia & ~NavigationCell.KnownMedia) != 0
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
                new TraversalIntent(StartMedium, TargetMedia),
                Algorithm,
                _budget.CreateBudget(),
                AllowTransitions,
                Algorithm == PathAlgorithm.FlowField
                    ? new FlowFieldQueryOptions(ExtraIntegrationCost)
                    : default,
                ArrivalRadius);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        bool isLoading = chronicler.Mode == SerializationMode.Loading;
        if (!isLoading)
        {
            PathQuery? query = Query;
            ChronicleGuard.ThrowIfTrue(
                !query.HasValue,
                message: "PathQueryRecord must contain a query before it can be serialized.");

            Capture(query.Value);
        }

        RecordValues.Look(chronicler, ref SchemaVersion, "SchemaVersion", 0);
        RecordValues.Look(chronicler, ref StartPosition, "StartPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref StartMapId, "StartMapId", null);
        RecordValues.Look(chronicler, ref StartResolution, "StartResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref StartMaxResolutionDistance, "StartMaxResolutionDistance", Fixed64.Zero);
        RecordValues.Look(chronicler, ref EndPosition, "EndPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref EndMapId, "EndMapId", null);
        RecordValues.Look(chronicler, ref EndResolution, "EndResolution", EndpointResolutionPolicy.Strict);
        RecordValues.Look(chronicler, ref EndMaxResolutionDistance, "EndMaxResolutionDistance", Fixed64.Zero);
        RecordValues.Look(chronicler, ref ArrivalRadius, "ArrivalRadius", Fixed64.Zero);
        RecordDeep.Look(chronicler, ref Agent, "Agent");
        RecordValues.Look(chronicler, ref AreaPolicyId, "AreaPolicyId", null);
        RecordValues.Look(chronicler, ref AreaPolicyRevision, "AreaPolicyRevision", 0L);
        RecordValues.Look(chronicler, ref StartMedium, "StartMedium", TraversalMedium.Unknown);
        RecordValues.Look(chronicler, ref TargetMedia, "TargetMedia", TraversalMedia.None);
        RecordValues.Look(chronicler, ref Algorithm, "Algorithm", PathAlgorithm.AStar);
        _budget.Look(chronicler);
        RecordValues.Look(chronicler, ref AllowTransitions, "AllowTransitions", false);
        RecordValues.Look(chronicler, ref ExtraIntegrationCost, "ExtraIntegrationCost", Fixed64.Zero);

        if (isLoading)
        {
            ChronicleGuard.ThrowIfTrue(
                !TryCreateQuery(out PathQuery? query) || !query.HasValue,
                message: "Serialized PathQuery is missing, invalid, or unsupported.");

            Query = query.Value;
        }
    }
}
