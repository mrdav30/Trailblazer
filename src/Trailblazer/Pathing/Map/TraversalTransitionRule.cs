//=======================================================================
// TraversalTransitionRule.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable bounded procedural transition authoring rule.</summary>
public readonly struct TraversalTransitionRule : IEquatable<TraversalTransitionRule>
{
    internal const TraversalTransitionLocomotionHints KnownLocomotionHints =
        TraversalTransitionLocomotionHints.RequestClimb
        | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion;

    /// <summary>Creates one complete bounded procedural transition rule.</summary>
    public TraversalTransitionRule(
        string id,
        TraversalTransitionType type,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium,
        TraversalTransitionRuleScope scope,
        TraversalCapability requiredCapabilities,
        Fixed64 actionCost,
        TraversalTransitionLocomotionHints locomotionHints)
    {
        Id = id;
        Type = type;
        SourceMedium = sourceMedium;
        DestinationMedium = destinationMedium;
        Scope = scope;
        RequiredCapabilities = requiredCapabilities;
        ActionCost = actionCost;
        LocomotionHints = locomotionHints;
        Validate();
    }

    /// <summary>Gets the stable globally unique rule identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the semantic action type.</summary>
    public TraversalTransitionType Type { get; }

    /// <summary>Gets the exact source medium.</summary>
    public TraversalMedium SourceMedium { get; }

    /// <summary>Gets the exact destination medium.</summary>
    public TraversalMedium DestinationMedium { get; }

    /// <summary>Gets the bounded procedural scope.</summary>
    public TraversalTransitionRuleScope Scope { get; }

    /// <summary>Gets the capabilities required to use the action.</summary>
    public TraversalCapability RequiredCapabilities { get; }

    /// <summary>Gets the non-negative semantic action cost.</summary>
    public Fixed64 ActionCost { get; }

    /// <summary>Gets the authored built-in locomotion hints.</summary>
    public TraversalTransitionLocomotionHints LocomotionHints { get; }

    /// <inheritdoc />
    public bool Equals(TraversalTransitionRule other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Type == other.Type
        && SourceMedium == other.SourceMedium
        && DestinationMedium == other.DestinationMedium
        && Scope == other.Scope
        && RequiredCapabilities == other.RequiredCapabilities
        && ActionCost == other.ActionCost
        && LocomotionHints == other.LocomotionHints;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TraversalTransitionRule other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        int idHash = Id == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(Id);
        int hash = SwiftHashTools.CombineHashCodes(idHash, (int)Type);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)SourceMedium);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)DestinationMedium);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)Scope);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)RequiredCapabilities);
        hash = SwiftHashTools.CombineHashCodes(hash, ActionCost.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, (int)LocomotionHints);
    }

    internal void Validate()
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(Id),
            nameof(Id),
            "Rule ID cannot be null, empty, or whitespace.");
        SwiftThrowHelper.ThrowIfArgument(
            !TraversalTransitionDefinition.IsKnownType(Type),
            nameof(Type),
            "Transition type is unknown.");
        SwiftThrowHelper.ThrowIfArgument(
            !TraversalTransitionDefinition.IsKnownMedium(SourceMedium),
            nameof(SourceMedium),
            "Source medium must identify one known traversal medium.");
        SwiftThrowHelper.ThrowIfArgument(
            !TraversalTransitionDefinition.IsKnownMedium(DestinationMedium),
            nameof(DestinationMedium),
            "Destination medium must identify one known traversal medium.");
        SwiftThrowHelper.ThrowIfArgument(
            Scope is < TraversalTransitionRuleScope.SameCell
                or > TraversalTransitionRuleScope.PositiveFaceContact,
            nameof(Scope),
            "Rule scope is unknown.");
        SwiftThrowHelper.ThrowIfArgument(
            (RequiredCapabilities & ~NavigationCell.KnownCapabilities) != 0,
            nameof(RequiredCapabilities),
            "Required capabilities contain an unknown bit.");
        SwiftThrowHelper.ThrowIfArgument(
            ActionCost < Fixed64.Zero,
            nameof(ActionCost),
            "Action cost must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            (LocomotionHints & ~KnownLocomotionHints) != 0,
            nameof(LocomotionHints),
            "Locomotion hints contain an unknown bit.");
    }
}

/// <summary>Provides complete deterministic canonical ordering for transition rules.</summary>
internal sealed class TraversalTransitionRuleComparer : IComparer<TraversalTransitionRule>
{
    internal static readonly TraversalTransitionRuleComparer Instance = new();

    public int Compare(TraversalTransitionRule left, TraversalTransitionRule right)
    {
        int comparison = string.CompareOrdinal(left.Id, right.Id);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Type).CompareTo((int)right.Type);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.SourceMedium).CompareTo((int)right.SourceMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.DestinationMedium).CompareTo((int)right.DestinationMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Scope).CompareTo((int)right.Scope);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.RequiredCapabilities).CompareTo((int)right.RequiredCapabilities);
        if (comparison != 0)
            return comparison;
        comparison = left.ActionCost < right.ActionCost
            ? -1
            : left.ActionCost > right.ActionCost ? 1 : 0;
        return comparison != 0
            ? comparison
            : ((int)left.LocomotionHints).CompareTo((int)right.LocomotionHints);
    }
}
