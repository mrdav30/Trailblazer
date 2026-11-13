//=======================================================================
// PathQuery.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines complete immutable caller intent for one navigation query.
/// </summary>
public readonly struct PathQuery : IEquatable<PathQuery>
{
    private readonly Fixed64 _arrivalRadius;

    /// <summary>Gets the requested start endpoint.</summary>
    public NavigationEndpoint Start { get; }

    /// <summary>Gets the requested destination endpoint.</summary>
    public NavigationEndpoint End { get; }

    /// <summary>Gets the exact agent profile used to evaluate traversal.</summary>
    public NavigationAgentProfile Agent { get; }

    /// <summary>Gets the exact query-specific navigation-area policy identity.</summary>
    public NavigationAreaPolicyKey AreaPolicy { get; }

    /// <summary>Gets the exact start medium and allowed target media.</summary>
    public TraversalIntent Traversal { get; }

    /// <summary>Gets the selected search algorithm.</summary>
    public PathAlgorithm Algorithm { get; }

    /// <summary>Gets the finite work budget shared by the complete query.</summary>
    public NavigationWorkBudget Budget { get; }

    /// <summary>Gets whether explicit or generated traversal transitions may be used.</summary>
    public bool AllowTransitions { get; }

    /// <summary>Gets the options used when <see cref="Algorithm"/> is <see cref="PathAlgorithm.FlowField"/>.</summary>
    public FlowFieldQueryOptions FlowField { get; }

    /// <summary>Gets the inclusive distance from the destination to an admissible route endpoint. Null requests exact arrival.</summary>
    public Fixed64? ArrivalRadius => _arrivalRadius > Fixed64.Zero ? _arrivalRadius : null;

    /// <summary>
    /// Creates complete immutable navigation query intent.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a nested value is invalid or incompatible with the algorithm.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The algorithm is unknown or the arrival radius is negative.</exception>
    public PathQuery(
        NavigationEndpoint start,
        NavigationEndpoint end,
        NavigationAgentProfile agent,
        NavigationAreaPolicyKey areaPolicy,
        TraversalIntent traversal,
        PathAlgorithm algorithm,
        NavigationWorkBudget budget,
        bool allowTransitions,
        FlowFieldQueryOptions flowField = default,
        Fixed64? arrivalRadius = null)
    {
        agent.Validate(nameof(agent));
        areaPolicy.Validate(nameof(areaPolicy));
        traversal.Validate(nameof(traversal));
        ChronicleGuard.ThrowIfArgumentOutOfRange(
            algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField,
            (int)algorithm,
            nameof(algorithm),
            "Path algorithm is unknown.");
        ChronicleGuard.ThrowIfArgument(
            algorithm == PathAlgorithm.AStar && flowField != default,
            nameof(flowField),
            "A* queries cannot carry flow-field-specific options.");
        ChronicleGuard.ThrowIfArgument(
            (traversal.TargetMedia & ~agent.AllowedMedia) != 0,
            nameof(traversal),
            "Target media must be a subset of the agent's allowed media.");
        ChronicleGuard.ThrowIfArgumentOutOfRange(arrivalRadius < Fixed64.Zero, actualValue: null, nameof(arrivalRadius));

        Start = start;
        End = end;
        Agent = agent;
        AreaPolicy = areaPolicy;
        Traversal = traversal;
        Algorithm = algorithm;
        Budget = budget;
        AllowTransitions = allowTransitions;
        FlowField = flowField;
        _arrivalRadius = arrivalRadius.GetValueOrDefault();
    }

    internal PathQuery WithStartState(
        Vector3d position,
        TraversalMedium startMedium) => new(
        new NavigationEndpoint(
            position,
            Start.MapId,
            Start.Resolution,
            Start.MaxResolutionDistance),
        End,
        Agent,
        AreaPolicy,
        new TraversalIntent(startMedium, Traversal.TargetMedia),
        Algorithm,
        Budget,
        AllowTransitions,
        FlowField,
        ArrivalRadius);

    internal PathQuery WithAgentAndStartState(
        NavigationAgentProfile agent,
        Vector3d position,
        TraversalMedium startMedium,
        string? startMapId) => new(
        new NavigationEndpoint(
            position,
            startMapId,
            Start.Resolution,
            Start.MaxResolutionDistance),
        End,
        agent,
        AreaPolicy,
        new TraversalIntent(startMedium, Traversal.TargetMedia),
        Algorithm,
        Budget,
        AllowTransitions,
        FlowField,
        ArrivalRadius);

    /// <inheritdoc/>
    public bool Equals(PathQuery other) =>
        Start == other.Start
        && End == other.End
        && Agent == other.Agent
        && AreaPolicy == other.AreaPolicy
        && Traversal == other.Traversal
        && Algorithm == other.Algorithm
        && Budget == other.Budget
        && AllowTransitions == other.AllowTransitions
        && FlowField == other.FlowField
        && _arrivalRadius == other._arrivalRadius;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PathQuery other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(Start.GetHashCode(), End.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Agent.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AreaPolicy.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Traversal.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)Algorithm);
        hash = SwiftHashTools.CombineHashCodes(hash, Budget.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AllowTransitions ? 1 : 0);
        hash = SwiftHashTools.CombineHashCodes(hash, FlowField.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, _arrivalRadius.GetHashCode());
    }

    /// <summary>Returns whether two queries have exactly equal intent.</summary>
    public static bool operator ==(PathQuery left, PathQuery right) => left.Equals(right);

    /// <summary>Returns whether two queries differ.</summary>
    public static bool operator !=(PathQuery left, PathQuery right) => !left.Equals(right);
}
