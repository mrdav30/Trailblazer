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

    /// <summary>
    /// Creates complete immutable navigation query intent.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a nested value is invalid or incompatible with the algorithm.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the algorithm is unknown.</exception>
    public PathQuery(
        NavigationEndpoint start,
        NavigationEndpoint end,
        NavigationAgentProfile agent,
        NavigationAreaPolicyKey areaPolicy,
        TraversalIntent traversal,
        PathAlgorithm algorithm,
        NavigationWorkBudget budget,
        bool allowTransitions,
        FlowFieldQueryOptions flowField = default)
    {
        start.Validate(nameof(start));
        end.Validate(nameof(end));
        agent.Validate(nameof(agent));
        areaPolicy.Validate(nameof(areaPolicy));
        traversal.Validate(nameof(traversal));
        budget.Validate(nameof(budget));
        flowField.Validate(nameof(flowField));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            algorithm is not PathAlgorithm.AStar and not PathAlgorithm.FlowField,
            (int)algorithm,
            nameof(algorithm),
            "Path algorithm is unknown.");
        SwiftThrowHelper.ThrowIfArgument(
            algorithm == PathAlgorithm.AStar && flowField != default,
            nameof(flowField),
            "A* queries cannot carry flow-field-specific options.");
        SwiftThrowHelper.ThrowIfArgument(
            (traversal.TargetMedia & ~agent.AllowedMedia) != 0,
            nameof(traversal),
            "Target media must be a subset of the agent's allowed media.");

        Start = start;
        End = end;
        Agent = agent;
        AreaPolicy = areaPolicy;
        Traversal = traversal;
        Algorithm = algorithm;
        Budget = budget;
        AllowTransitions = allowTransitions;
        FlowField = flowField;
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
        FlowField);

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
        && FlowField == other.FlowField;

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
        return SwiftHashTools.CombineHashCodes(hash, FlowField.GetHashCode());
    }

    /// <summary>Returns whether two queries have exactly equal intent.</summary>
    public static bool operator ==(PathQuery left, PathQuery right) => left.Equals(right);

    /// <summary>Returns whether two queries differ.</summary>
    public static bool operator !=(PathQuery left, PathQuery right) => !left.Equals(right);
}
