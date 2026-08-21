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
internal readonly struct TraversalTransitionRule : IEquatable<TraversalTransitionRule>
{
    private const TraversalTransitionLocomotionHints KnownLocomotionHints =
        TraversalTransitionLocomotionHints.RequestClimb
        | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion;

    internal TraversalTransitionRule(
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

    internal string Id { get; }

    internal TraversalTransitionType Type { get; }

    internal TraversalMedium SourceMedium { get; }

    internal TraversalMedium DestinationMedium { get; }

    internal TraversalTransitionRuleScope Scope { get; }

    internal TraversalCapability RequiredCapabilities { get; }

    internal Fixed64 ActionCost { get; }

    internal TraversalTransitionLocomotionHints LocomotionHints { get; }

    public bool Equals(TraversalTransitionRule other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Type == other.Type
        && SourceMedium == other.SourceMedium
        && DestinationMedium == other.DestinationMedium
        && Scope == other.Scope
        && RequiredCapabilities == other.RequiredCapabilities
        && ActionCost == other.ActionCost
        && LocomotionHints == other.LocomotionHints;

    public override bool Equals(object? obj) =>
        obj is TraversalTransitionRule other && Equals(other);

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
