//=======================================================================
// NavigationFlowFieldPayloadKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one destination-centric flow field independently of origin.</summary>
internal readonly struct NavigationFlowFieldPayloadKey :
    IEquatable<NavigationFlowFieldPayloadKey>
{
    internal NavigationFlowFieldPayloadKey(
        PathQuery query,
        NavigationCellAddress destinationAddress,
        TraversalMedium startMedium,
        TraversalMedia targetMedia)
    {
        Destination = query.End;
        DestinationAddress = destinationAddress;
        _startMedium = (byte)startMedium;
        _targetMedia = (byte)targetMedia;
        Agent = query.Agent;
        AreaPolicy = query.AreaPolicy;
        Traversal = query.Traversal;
        Budget = query.Budget;
        AllowTransitions = query.AllowTransitions;
        FlowField = query.FlowField;
        _arrivalRadius = query.ArrivalRadius.GetValueOrDefault();
    }

    internal NavigationEndpoint Destination { get; }
    internal NavigationCellAddress DestinationAddress { get; }
    internal NavigationAgentProfile Agent { get; }
    internal NavigationAreaPolicyKey AreaPolicy { get; }
    internal TraversalIntent Traversal { get; }
    internal NavigationWorkBudget Budget { get; }
    internal bool AllowTransitions { get; }
    private readonly byte _startMedium;
    private readonly byte _targetMedia;
    internal TraversalMedium StartMedium => (TraversalMedium)_startMedium;
    internal TraversalMedia TargetMedia => (TraversalMedia)_targetMedia;
    internal FlowFieldQueryOptions FlowField { get; }
    private readonly Fixed64 _arrivalRadius;
    internal bool HasGoalRegion => _arrivalRadius > Fixed64.Zero;
    internal Fixed64 ArrivalRadius => _arrivalRadius;

    public bool Equals(NavigationFlowFieldPayloadKey other) =>
        Destination == other.Destination
        && DestinationAddress == other.DestinationAddress
        && StartMedium == other.StartMedium
        && TargetMedia == other.TargetMedia
        && Agent == other.Agent
        && AreaPolicy == other.AreaPolicy
        && Traversal == other.Traversal
        && Budget == other.Budget
        && AllowTransitions == other.AllowTransitions
        && FlowField == other.FlowField
        && _arrivalRadius == other._arrivalRadius;

    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(
            Destination.GetHashCode(),
            DestinationAddress.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)StartMedium);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)TargetMedia);
        hash = SwiftHashTools.CombineHashCodes(hash, Agent.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AreaPolicy.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Traversal.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Budget.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, AllowTransitions ? 1 : 0);
        hash = SwiftHashTools.CombineHashCodes(hash, FlowField.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, _arrivalRadius.GetHashCode());
    }

}
