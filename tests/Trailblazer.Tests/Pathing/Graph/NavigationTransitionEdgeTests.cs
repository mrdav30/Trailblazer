//=======================================================================
// NavigationTransitionEdgeTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationTransitionEdgeTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("transition-edge", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void Dispatcher_ShouldUseExplicitActionCostWithoutEndpointTravelDistance()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(2, 0, 0);
        var targetAddress = new NavigationCellAddress("map", targetIndex);
        var definition = new TraversalTransitionDefinition(
            "teleport",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            targetAddress,
            TraversalMedium.Gas,
            additionalCost: (Fixed64)5);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas, enterCost: (Fixed64)7),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                targetAddress,
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending
            || (status == NavigationTraversalEdgeAdvanceStatus.Edge
                && dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(target);
        dispatcher.CurrentKind.Should().Be(NavigationTraversalEdgeKind.Transition);
        dispatcher.CurrentCost.Should().Be((Fixed64)12);
        dispatcher.CurrentTransitionId.Should().Be("teleport");
        dispatcher.CurrentTransitionType.Should().Be(TraversalTransitionType.Jump);
        dispatcher.CurrentTransitionHints.Should().Be(
            TraversalTransitionLocomotionHints.None);
        lease.Graph.TryGetNodeState(
                source.Node,
                TraversalMedium.Solid,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        lease.Graph.TryGetNodeState(
                target.Node,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(
                Fixed64.One,
                out Vector3d targetAnchor)
            .Should().BeTrue();
        dispatcher.CurrentTransitionSourceAction.Should().Be(sourceState.FootAnchor);
        dispatcher.CurrentTransitionDestinationAction.Should().Be(targetAnchor);
        meter.TransitionCandidates.Should().Be(1);
        meter.TransitionPairs.Should().Be(1);
        workspace.Dependencies.HasTransitionDependency.Should().BeTrue();
    }

    [Fact]
    public void Dispatcher_ShouldReportTransitionCostOverflowWithoutEmittingEdge()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(2, 0, 0);
        var definition = new TraversalTransitionDefinition(
            "overflow",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas,
            additionalCost: Fixed64.MaxValue);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas, enterCost: Fixed64.One),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.CostOverflow);
        dispatcher.CurrentOrdinal.Should().Be(-1);
    }

    [Fact]
    public void Dispatcher_ShouldOrderDefinitionAndRulesByTypeThenTaggedIdentity()
    {
        var index = new VoxelIndex(0, 0, 0);
        var address = new NavigationCellAddress("map", index);
        var definition = new TraversalTransitionDefinition(
            "z-rule",
            TraversalTransitionType.Climb,
            index,
            TraversalMedium.Solid,
            address,
            TraversalMedium.Gas,
            additionalCost: Fixed64.One);
        TraversalTransitionRule[] rules =
        {
            new(
                "a-rule",
                TraversalTransitionType.Climb,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                (Fixed64)3,
                TraversalTransitionLocomotionHints.RequestClimb),
            new(
                "z-rule",
                TraversalTransitionType.Jump,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                (Fixed64)2,
                TraversalTransitionLocomotionHints.None)
        };
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            new[] { definition },
            rules);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 128;
        int connectionRemaining = int.MaxValue;
        var ids = new SwiftList<string>(3);
        var kinds = new SwiftList<NavigationTransitionIdentityKind>(3);
        var ordinals = new SwiftList<int>(3);

        while (true)
        {
            NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                break;
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                continue;
            ids.Add(dispatcher.CurrentTransitionId);
            kinds.Add(dispatcher.CurrentTransitionIdentityKind);
            ordinals.Add(dispatcher.CurrentOrdinal);
        }

        ids.Should().Equal("z-rule", "z-rule", "a-rule");
        kinds.Should().Equal(
            NavigationTransitionIdentityKind.Rule,
            NavigationTransitionIdentityKind.Definition,
            NavigationTransitionIdentityKind.Rule);
        ordinals.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Dispatcher_ShouldResolvePositiveFaceRuleActionPointsAndHints()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(1, 0, 0);
        var rule = new TraversalTransitionRule(
            "duck-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.Fly,
            (Fixed64)4,
            TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Liquid),
            targetIndex,
            Cell(TraversalMedia.Gas, enterCost: (Fixed64)3),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        NavigationNodeRef target = lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", targetIndex),
                out NavigationNodeRef targetNode)
            ? targetNode
            : default;
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(
                TraversalMedia.Liquid | TraversalMedia.Gas,
                TraversalCapability.Fly),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 128;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending
            || (status == NavigationTraversalEdgeAdvanceStatus.Edge
                && dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(
            new NavigationMediumStateRef(target, TraversalMedium.Gas));
        lease.Graph.TryGetNodeState(
                source.Node,
                TraversalMedium.Liquid,
                out NavigationNodeState sourceState)
            .Should().BeTrue();
        lease.Graph.TryGetNodeState(
                target,
                TraversalMedium.Gas,
                out NavigationNodeState targetState)
            .Should().BeTrue();
        sourceState.TryGetCenteredVolumeFootAnchor(
                Fixed64.One,
                out Vector3d sourceAnchor)
            .Should().BeTrue();
        targetState.TryGetCenteredVolumeFootAnchor(
                Fixed64.One,
                out Vector3d targetAnchor)
            .Should().BeTrue();
        lease.Graph.TryGetSeamPrism(
                new NavigationCellAddress("map", sourceIndex),
                out GridCellPrism sourcePrism)
            .Should().BeTrue();
        lease.Graph.TryGetSeamPrism(
                new NavigationCellAddress("map", targetIndex),
                out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                targetPrism,
                portal,
                sourceAnchor,
                targetAnchor,
                Fixed64.Half,
                Fixed64.One,
                out Fixed64 sourceParameter,
                out Fixed64 targetParameter)
            .Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                sourceAnchor,
                Vector3d.Lerp(sourceAnchor, targetAnchor, sourceParameter),
                out Fixed64 sourceDistance)
            .Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                Vector3d.Lerp(sourceAnchor, targetAnchor, targetParameter),
                targetAnchor,
                out Fixed64 targetDistance)
            .Should().BeTrue();
        dispatcher.CurrentCost.Should().Be(
            sourceDistance + (Fixed64)4 + targetDistance + (Fixed64)3);
        dispatcher.CurrentTransitionId.Should().Be("duck-takeoff");
        dispatcher.CurrentTransitionHints.Should().Be(
            TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        Vector3d expectedSourceAction = Vector3d.Lerp(
            sourceAnchor,
            targetAnchor,
            sourceParameter);
        Vector3d expectedDestinationAction = Vector3d.Lerp(
            sourceAnchor,
            targetAnchor,
            targetParameter);
        dispatcher.CurrentTransitionSourceAction.Should().Be(expectedSourceAction);
        dispatcher.CurrentTransitionDestinationAction.Should().Be(expectedDestinationAction);
        meter.CoveredVoxelIntervals.Should().Be(0,
            "a fitting heterogeneous PositiveFaceContact uses the directed portal fast path");

        workspace.Reset();
        meter.Reset(Budget());
        var grounded = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        remaining = 128;
        do
        {
            status = grounded.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete,
            "the takeoff rule requires Fly");
    }

    [Fact]
    public void Incoming_ShouldRecoverExactForwardTaggedIdentityAndOrdinal()
    {
        var index = new VoxelIndex(0, 0, 0);
        var address = new NavigationCellAddress("map", index);
        var definition = new TraversalTransitionDefinition(
            "z-rule",
            TraversalTransitionType.Climb,
            index,
            TraversalMedium.Solid,
            address,
            TraversalMedium.Gas,
            additionalCost: Fixed64.One);
        TraversalTransitionRule[] rules =
        {
            new(
                "a-rule",
                TraversalTransitionType.Climb,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                (Fixed64)3,
                TraversalTransitionLocomotionHints.RequestClimb),
            new(
                "z-rule",
                TraversalTransitionType.Jump,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                (Fixed64)2,
                TraversalTransitionLocomotionHints.None)
        };
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            new[] { definition },
            rules);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Gas,
                out NavigationMediumStateRef destination)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            destination,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true);
        var meter = new NavigationWorkMeter(Budget());
        var ids = new SwiftList<string>(3);
        var kinds = new SwiftList<NavigationTransitionIdentityKind>(3);
        var ordinals = new SwiftList<int>(3);

        for (int call = 0; call < 256 && ids.Count < 3; call++)
        {
            int remaining = 1;
            int connectionRemaining = int.MaxValue;
            NavigationTraversalEdgeAdvanceStatus status = incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
            {
                status.Should().BeOneOf(
                    NavigationTraversalEdgeAdvanceStatus.Pending,
                    NavigationTraversalEdgeAdvanceStatus.Blocked);
                continue;
            }
            ids.Add(incoming.CurrentTransitionId);
            kinds.Add(incoming.CurrentTransitionIdentityKind);
            ordinals.Add(incoming.CurrentOrdinal);
            incoming.CurrentPredecessor.Should().Be(
                new NavigationMediumStateRef(destination.Node, TraversalMedium.Solid));
        }

        ids.Should().Equal("z-rule", "z-rule", "a-rule");
        kinds.Should().Equal(
            NavigationTransitionIdentityKind.Rule,
            NavigationTransitionIdentityKind.Definition,
            NavigationTransitionIdentityKind.Rule);
        ordinals.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Incoming_ShouldRecoverForwardOrdinalAcrossBaseTransitionBoundary()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(1, 0, 0);
        var targetAddress = new NavigationCellAddress("map", targetIndex);
        var definition = new TraversalTransitionDefinition(
            "adjacent-jump",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            targetAddress,
            TraversalMedium.Solid,
            additionalCost: Fixed64.One);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Solid),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                targetAddress,
                TraversalMedium.Solid,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var forward = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int baseOrdinal = -1;
        int transitionOrdinal = -1;
        Vector3d sourceAction = default;
        Vector3d destinationAction = default;

        for (int call = 0; call < 64 && transitionOrdinal < 0; call++)
        {
            int remaining = 64;
            int connectionRemaining = int.MaxValue;
            NavigationTraversalEdgeAdvanceStatus status = forward.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                continue;
            if (forward.CurrentKind == NavigationTraversalEdgeKind.Surface)
                baseOrdinal = forward.CurrentOrdinal;
            else if (forward.CurrentKind == NavigationTraversalEdgeKind.Transition)
            {
                transitionOrdinal = forward.CurrentOrdinal;
                sourceAction = forward.CurrentTransitionSourceAction;
                destinationAction = forward.CurrentTransitionDestinationAction;
            }
        }

        baseOrdinal.Should().Be(0);
        transitionOrdinal.Should().BeGreaterThan(baseOrdinal);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            target,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true);
        int recoveredOrdinal = -1;
        for (int call = 0; call < 128 && recoveredOrdinal < 0; call++)
        {
            int remaining = 64;
            int connectionRemaining = int.MaxValue;
            NavigationTraversalEdgeAdvanceStatus status = incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Edge
                && incoming.CurrentKind == NavigationTraversalEdgeKind.Transition
                && incoming.CurrentTransitionId == "adjacent-jump")
            {
                incoming.CurrentPredecessor.Should().Be(source);
                incoming.CurrentTransitionSourceAction.Should().Be(sourceAction);
                incoming.CurrentTransitionDestinationAction.Should().Be(destinationAction);
                recoveredOrdinal = incoming.CurrentOrdinal;
            }
        }

        recoveredOrdinal.Should().Be(transitionOrdinal);
    }

    [Fact]
    public void RuleScan_ShouldResumeWithOneStepWithoutRescanningOrLosingBest()
    {
        var index = new VoxelIndex(0, 0, 0);
        var address = new NavigationCellAddress("map", index);
        TraversalTransitionRule[] rules =
        {
            new(
                "a-climb",
                TraversalTransitionType.Climb,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.One,
                TraversalTransitionLocomotionHints.None),
            new(
                "z-jump",
                TraversalTransitionType.Jump,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.One,
                TraversalTransitionLocomotionHints.None)
        };
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            System.Array.Empty<TraversalTransitionDefinition>(),
            rules);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        var ids = new SwiftList<string>(2);

        for (int call = 0; call < 32 && ids.Count < 2; call++)
        {
            int remaining = 1;
            int connectionRemaining = int.MaxValue;
            NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Edge)
                ids.Add(dispatcher.CurrentTransitionId);
            else
                status.Should().BeOneOf(
                    NavigationTraversalEdgeAdvanceStatus.Pending,
                    NavigationTraversalEdgeAdvanceStatus.Blocked);
        }

        ids.Should().Equal("z-jump", "a-climb");
        meter.TransitionPairs.Should().Be(2);
        meter.TransitionCandidates.Should().Be(8,
            "each selection debits both rule rows and their concrete SameCell contacts");
    }

    [Fact]
    public void Dispatcher_ShouldEvaluateSameMediumExplicitOverrideLegs()
    {
        using TrailblazerWorldContext context = CreateOverrideContext(
            out NavigationCellAddress address,
            out Vector3d sourceAnchor,
            out Vector3d sourceAction,
            out Vector3d targetAction);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(source);
        NavigationDistanceMath.TryCeiling(
                sourceAnchor,
                sourceAction,
                out Fixed64 sourceDistance)
            .Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                targetAction,
                sourceAnchor,
                out Fixed64 targetDistance)
            .Should().BeTrue();
        dispatcher.CurrentCost.Should().Be(
            sourceDistance + (Fixed64)2 + targetDistance + (Fixed64)3);
        dispatcher.CurrentTransitionHints.Should().Be(
            TraversalTransitionLocomotionHints.None);
        dispatcher.CurrentTransitionSourceAction.Should().Be(sourceAction);
        dispatcher.CurrentTransitionDestinationAction.Should().Be(targetAction);
    }

    [Fact]
    public void Dispatcher_ShouldCertifyDegenerateVolumeTransitionPlacement()
    {
        var address = new NavigationCellAddress("map", default);
        var definition = new TraversalTransitionDefinition(
            "hover",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Gas,
            address,
            TraversalMedium.Gas,
            additionalCost: Fixed64.One);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Gas),
            default,
            Cell(TraversalMedia.Gas),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Gas,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(
                TraversalMedia.Gas,
                radius: Fixed64.FromFraction(3, 2)),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 128;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete,
            "a zero-length volume action still requires valid body placement");
    }

    [Fact]
    public void Dispatcher_ShouldRejectPositiveFaceRuleWhenProfileCannotFitPortal()
    {
        var sourceIndex = new VoxelIndex(1, 0, 1);
        var targetIndex = new VoxelIndex(2, 0, 1);
        var rule = new TraversalTransitionRule(
            "large-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateLargeContext(
            rule,
            definition: null,
            heterogeneousMedia: true);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", targetIndex),
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 32, 32, 128, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(
                TraversalMedia.Liquid | TraversalMedia.Gas,
                radius: Fixed64.FromFraction(3, 2)),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4096;
        int connectionRemaining = int.MaxValue;
        int transitionCoveredIntervals = -1;
        bool emittedTransition = false;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            int priorPairs = meter.TransitionPairs;
            int priorCoveredIntervals = meter.CoveredVoxelIntervals;
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
            emittedTransition |= status == NavigationTraversalEdgeAdvanceStatus.Edge
                && dispatcher.CurrentKind == NavigationTraversalEdgeKind.Transition;
            if (meter.TransitionPairs != priorPairs)
            {
                transitionCoveredIntervals = meter.CoveredVoxelIntervals
                    - priorCoveredIntervals;
            }
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete,
            "a PositiveFaceContact requires the actual body profile to fit the directed portal");
        emittedTransition.Should().BeFalse();
        dispatcher.CurrentOrdinal.Should().Be(-1);
        transitionCoveredIntervals.Should().Be(0,
            "portal-profile rejection must occur before either swept-union action leg");
    }

    [Fact]
    public void Dispatcher_ShouldUseUnionForLargeSameMediumExplicitActionLegs()
    {
        var index = new VoxelIndex(1, 0, 1);
        var address = new NavigationCellAddress("map", index);
        var sourceAction = new Vector3d(
            Fixed64.FromFraction(5, 2),
            Fixed64.Zero,
            (Fixed64)2);
        var destinationAction = new Vector3d(
            Fixed64.FromFraction(3, 2),
            Fixed64.Zero,
            (Fixed64)2);
        var definition = new TraversalTransitionDefinition(
            "large-swim",
            TraversalTransitionType.Jump,
            index,
            TraversalMedium.Liquid,
            address,
            TraversalMedium.Liquid,
            additionalCost: Fixed64.One,
            sourcePointOverride: sourceAction,
            hasSourcePointOverride: true,
            destinationPointOverride: destinationAction,
            hasDestinationPointOverride: true);
        using TrailblazerWorldContext context = CreateLargeContext(
            rule: null,
            definition,
            heterogeneousMedia: false);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 32, 32, 128, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(
                TraversalMedia.Liquid,
                radius: Fixed64.FromFraction(3, 2)),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(LargeBudget());
        int remaining = 4_096;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(source);
        dispatcher.CurrentTransitionSourceAction.Should().Be(sourceAction);
        dispatcher.CurrentTransitionDestinationAction.Should().Be(destinationAction);
        meter.CoveredVoxelIntervals.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PositiveFaceRule_ShouldUseCrossMapSeamForwardAndReverse()
    {
        var rule = new TraversalTransitionRule(
            "cross-map-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            TraversalMedia.Liquid,
            TraversalMedia.Gas,
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("b-target", default),
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var forward = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        int remaining = 256;
        int connectionRemaining = int.MaxValue;
        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = forward.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || forward.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        forward.CurrentTarget.Should().Be(target);
        int ordinal = forward.CurrentOrdinal;
        Vector3d sourceAction = forward.CurrentTransitionSourceAction;
        Vector3d destinationAction = forward.CurrentTransitionDestinationAction;
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            target,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true);
        bool found = false;
        for (int call = 0; call < 256 && !found; call++)
        {
            remaining = 256;
            status = incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining, ref connectionRemaining);
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || incoming.CurrentKind != NavigationTraversalEdgeKind.Transition)
            {
                continue;
            }
            found = true;
            incoming.CurrentPredecessor.Should().Be(source);
            incoming.CurrentOrdinal.Should().Be(ordinal);
            incoming.CurrentTransitionSourceAction.Should().Be(sourceAction);
            incoming.CurrentTransitionDestinationAction.Should().Be(destinationAction);
        }
        found.Should().BeTrue();
    }

    [Fact]
    public void PositiveFaceRuleOutgoingSeams_ShouldResumeAndHonorExactCandidateLimit()
    {
        using TrailblazerWorldContext context = CreateSyntheticMultiSeamContext(
            out NavigationCellAddress[] sources,
            out NavigationCellAddress[] targets);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithSyntheticSeams(lease.Graph, sources, targets);
        graph.TryGetMediumStateRef(
                sources[0],
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 16, 16, 64, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            graph,
            source,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        const int exactCandidates = 40;
        var meter = new NavigationWorkMeter(Budget(exactCandidates));
        NavigationTraversalEdgeAdvanceStatus status = NavigationTraversalEdgeAdvanceStatus.Pending;

        for (int call = 0; call < 512 && status != NavigationTraversalEdgeAdvanceStatus.Complete; call++)
        {
            int priorCandidates = meter.TransitionCandidates;
            int remaining = 1;
            int connectionRemaining = int.MaxValue;
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
            (meter.TransitionCandidates - priorCandidates).Should().BeLessThanOrEqualTo(1,
                "one AdvanceOne may inspect at most one rule/contact candidate");
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Blocked,
                NavigationTraversalEdgeAdvanceStatus.Complete);
        }

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        meter.TransitionCandidates.Should().Be(exactCandidates,
            "four canonical passes each debit one rule, six primary contacts, and three seams");
        meter.TransitionPairs.Should().Be(3);

        workspace.Reset();
        dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            graph,
            source,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        meter = new NavigationWorkMeter(Budget(exactCandidates - 1));
        int unbounded = 512;
        int unboundedConnections = int.MaxValue;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref unbounded, ref unboundedConnections);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        meter.TransitionCandidates.Should().Be(exactCandidates - 1);
    }

    [Fact]
    public void PositiveFaceRuleIncomingSeams_ShouldResumeAndHonorExactCandidateLimit()
    {
        using TrailblazerWorldContext context = CreateSyntheticMultiSeamContext(
            out NavigationCellAddress[] sources,
            out NavigationCellAddress[] targets);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithSyntheticSeams(lease.Graph, sources, targets);
        graph.TryGetMediumStateRef(
                targets[0],
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 16, 16, 64, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            graph,
            target,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true);
        const int exactCandidates = 163;
        var meter = new NavigationWorkMeter(Budget(exactCandidates));
        NavigationTraversalEdgeAdvanceStatus status = NavigationTraversalEdgeAdvanceStatus.Pending;

        for (int call = 0; call < 1_024 && status != NavigationTraversalEdgeAdvanceStatus.Complete; call++)
        {
            int priorCandidates = meter.TransitionCandidates;
            int remaining = 1;
            int connectionRemaining = int.MaxValue;
            status = incoming.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
            (meter.TransitionCandidates - priorCandidates).Should().BeLessThanOrEqualTo(1,
                "incoming discovery and its forward replay share the same bounded rule/contact scan");
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Blocked,
                NavigationTraversalEdgeAdvanceStatus.Complete);
        }

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        meter.TransitionCandidates.Should().Be(exactCandidates,
            "every incoming and forward-replay contact plus candidate handoff is debited exactly");
        meter.TransitionPairs.Should().Be(9);

        workspace.Reset();
        incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            graph,
            target,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true);
        meter = new NavigationWorkMeter(Budget(exactCandidates - 1));
        int unbounded = 1_024;
        int unboundedConnections = int.MaxValue;
        do
        {
            status = incoming.AdvanceOne(meter, workspace.Dependencies, ref unbounded, ref unboundedConnections);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        meter.TransitionCandidates.Should().Be(exactCandidates - 1);
    }

    [Fact]
    public void DormantExplicitTransition_ShouldRecordBothEndpointPages()
    {
        var definition = new TraversalTransitionDefinition(
            "dormant",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("b-target", default),
            TraversalMedium.Gas,
            additionalCost: Fixed64.One);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            TraversalMedia.Solid,
            TraversalMedia.Gas,
            new[] { definition },
            System.Array.Empty<TraversalTransitionRule>());
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("b-target", new[]
                {
                    NavigationCellOverlayOperation.Set(
                        default,
                        Cell(TraversalMedia.Solid))
                })
            })),
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        while (overlay.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 256;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending
            || (status == NavigationTraversalEdgeAdvanceStatus.Edge
                && dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        workspace.Dependencies.PageCount.Should().Be(2,
            "reactivating either dormant endpoint must stale the negative result");
        meter.TransitionCandidates.Should().Be(1);
        meter.TransitionPairs.Should().Be(1);
    }

    [Fact]
    public void WrongMediumCrossMapRuleTarget_ShouldRecordBothEndpointPages()
    {
        var rule = new TraversalTransitionRule(
            "dormant-rule",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            TraversalMedia.Liquid,
            TraversalMedia.Solid,
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Liquid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 256;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        workspace.Dependencies.PageCount.Should().Be(2);
        meter.TransitionPairs.Should().Be(1);
    }

    [Fact]
    public void IncomingRejectedVolumeSource_ShouldRequireTheWorldStamp()
    {
        VoxelIndex index = default;
        var transition = new TraversalTransitionDefinition(
            "dormant-gas",
            TraversalTransitionType.Landing,
            index,
            TraversalMedium.Gas,
            new NavigationCellAddress("map", index),
            TraversalMedium.Solid,
            additionalCost: Fixed64.Zero);
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            transition);
        using NavigationWorldGraphLease lease =
            context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", index),
                TraversalMedium.Solid,
                out NavigationMediumStateRef destination)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            destination,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: true);
        var meter = new NavigationWorkMeter(Budget());
        NavigationTraversalEdgeAdvanceStatus status =
            NavigationTraversalEdgeAdvanceStatus.Pending;

        for (int call = 0;
            call < 256 && status != NavigationTraversalEdgeAdvanceStatus.Complete;
            call++)
        {
            int remaining = 16;
            int connectionRemaining = int.MaxValue;
            status = incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        incoming.RequiresWorldStamp.Should().BeTrue();
    }

    [Fact]
    public void EmptyTransitionScan_ShouldRecordIntentAndMergeResetExactly()
    {
        var index = new VoxelIndex(0, 0, 0);
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid),
            index,
            Cell(TraversalMedia.Solid),
            System.Array.Empty<TraversalTransitionDefinition>(),
            System.Array.Empty<TraversalTransitionRule>());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", index),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Complete);
        workspace.Dependencies.HasTransitionDependency.Should().BeTrue();
        meter.TransitionCandidates.Should().Be(0);
        meter.TransitionPairs.Should().Be(0);

        var merged = new NavigationDependencyWorkspace(8, 8);
        merged.CommitMerge(workspace.Dependencies);
        merged.HasTransitionDependency.Should().BeTrue();
        merged.Reset();
        merged.HasTransitionDependency.Should().BeFalse();
        meter.Reset(Budget());
        meter.TransitionCandidates.Should().Be(0);
        meter.TransitionPairs.Should().Be(0);
    }

    [Fact]
    public void WarmedTransitionDispatcher_ShouldAllocateZeroBytes()
    {
        var index = new VoxelIndex(0, 0, 0);
        var address = new NavigationCellAddress("map", index);
        var rule = new TraversalTransitionRule(
            "takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Solid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            index,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        int checksum = 0;
        Action enumerate = () =>
        {
            workspace.Reset();
            meter.Reset(Budget());
            var dispatcher = new NavigationTraversalEdgeEnumerator(
                context.World,
                lease.Graph,
                source,
                Profile(),
                Policy,
                workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
            int remaining = 64;
            int connectionRemaining = int.MaxValue;
            while (true)
            {
                NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                    meter,
                    workspace.Dependencies,
                    ref remaining, ref connectionRemaining);
                if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                    break;
                if (status == NavigationTraversalEdgeAdvanceStatus.Edge)
                    checksum += dispatcher.CurrentOrdinal + 1;
            }
        };
        enumerate();

        AllocationTestUtility.MeasureAllocatedBytes(enumerate).Should().Be(0);
        checksum.Should().BeGreaterThan(0);
    }

    private static TrailblazerWorldContext CreateContext(
        VoxelIndex sourceIndex,
        NavigationCell source,
        VoxelIndex targetIndex,
        NavigationCell target,
        TraversalTransitionDefinition transition) => CreateContext(
        sourceIndex,
        source,
        targetIndex,
        target,
        new[] { transition },
        System.Array.Empty<TraversalTransitionRule>());

    private static TrailblazerWorldContext CreateContext(
        VoxelIndex sourceIndex,
        NavigationCell source,
        VoxelIndex targetIndex,
        NavigationCell target,
        TraversalTransitionDefinition[] transitions,
        TraversalTransitionRule[] rules)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(8, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)2,
                    (Fixed64)4),
                storageKind: GridStorageKind.Sparse);
            context.World.TryAddGrid(
                    configuration,
                    sourceIndex == targetIndex
                        ? new[] { sourceIndex }
                        : new[] { sourceIndex, targetIndex },
                    out _)
                .Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding)
                .AddCell(sourceIndex, source);
            if (sourceIndex != targetIndex)
                builder.AddCell(targetIndex, target);
            for (int i = 0; i < transitions.Length; i++)
                builder.AddTransition(transitions[i]);
            for (int i = 0; i < rules.Length; i++)
                builder.AddTransitionRule(rules[i]);
            NavigationMap map = builder.Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateLargeContext(
        TraversalTransitionRule? rule,
        TraversalTransitionDefinition? definition,
        bool heterogeneousMedia)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(8, 2, 6),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2),
                storageKind: GridStorageKind.Sparse);
            var indices = new VoxelIndex[12];
            int count = 0;
            for (int x = 0; x < 4; x++)
            {
                for (int z = 0; z < 3; z++)
                    indices[count++] = new VoxelIndex(x, 0, z);
            }
            context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < indices.Length; i++)
            {
                TraversalMedia media = heterogeneousMedia && indices[i].x < 2
                    ? TraversalMedia.Liquid
                    : heterogeneousMedia
                        ? TraversalMedia.Gas
                        : TraversalMedia.Liquid | TraversalMedia.Gas;
                builder.AddCell(indices[i], Cell(media));
            }
            if (rule.HasValue)
                builder.AddTransitionRule(rule.Value);
            if (definition.HasValue)
                builder.AddTransition(definition.Value);
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateSyntheticMultiSeamContext(
        out NavigationCellAddress[] sources,
        out NavigationCellAddress[] targets)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        sources = new NavigationCellAddress[3];
        targets = new NavigationCellAddress[3];
        try
        {
            var indices = new VoxelIndex[6];
            for (int i = 0; i < 3; i++)
            {
                indices[i] = new VoxelIndex(i * 2, 0, 0);
                indices[i + 3] = new VoxelIndex(10 + (i * 2), 0, 0);
                sources[i] = new NavigationCellAddress("map", indices[i]);
                targets[i] = new NavigationCellAddress("map", indices[i + 3]);
            }
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(20, 1, 1),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse);
            context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            for (int i = 0; i < 3; i++)
            {
                builder.AddCell(indices[i], Cell(TraversalMedia.Liquid));
                builder.AddCell(indices[i + 3], Cell(TraversalMedia.Gas));
            }
            builder.AddTransitionRule(new TraversalTransitionRule(
                "multi-seam",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.PositiveFaceContact,
                TraversalCapability.None,
                Fixed64.One,
                TraversalTransitionLocomotionHints.None));
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationWorldGraph WithSyntheticSeams(
        NavigationWorldGraph graph,
        NavigationCellAddress[] sources,
        NavigationCellAddress[] targets)
    {
        NavigationAutomaticSeamIndex.EditSession edit = NavigationAutomaticSeamIndex.Empty.Edit(
            NavigationSeamEditToken.Create());
        var sourceRows = new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder[3];
        var targetRows = new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder[3];
        for (int i = 0; i < 3; i++)
        {
            sourceRows[i] = new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(8);
            targetRows[i] = new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(8);
        }
        for (int source = 0; source < 3; source++)
        {
            for (int target = 0; target < 3; target++)
            {
                var pair = new NavigationAutomaticSeamPair(
                    sources[source],
                    targets[target],
                    default);
                edit.SetPair(
                    new NavigationAutomaticSeamPairKey(pair.First, pair.Second),
                    new NavigationAutomaticSeamPairRecord(pair, isActive: true));
                sourceRows[source].Append(pair);
                targetRows[target].Append(pair);
            }
        }
        for (int i = 0; i < 3; i++)
        {
            edit.SetActiveRow(sources[i], sourceRows[i].Seal());
            edit.SetActiveRow(targets[i], targetRows[i].Seal());
        }
        return graph.WithAutomaticSeams(edit.Seal());
    }

    private static TrailblazerWorldContext CreateCrossMapContext(
        TraversalMedia sourceMedia,
        TraversalMedia targetMedia,
        TraversalTransitionDefinition[] definitions,
        TraversalTransitionRule[] rules)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration sourceConfiguration = new(
                new Vector3d(-1, 0, 0),
                new Vector3d(-1, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            GridConfiguration targetConfiguration = new(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
            context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
            context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
            sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
                .Should().BeTrue();
            targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
                .Should().BeTrue();
            var sourceBuilder = new NavigationMapBuilder("a-source", sourceBinding)
                .AddCell(default, Cell(sourceMedia));
            for (int i = 0; i < definitions.Length; i++)
                sourceBuilder.AddTransition(definitions[i]);
            for (int i = 0; i < rules.Length; i++)
                sourceBuilder.AddTransitionRule(rules[i]);
            NavigationMap[] maps =
            {
                sourceBuilder.Build(),
                new NavigationMapBuilder("b-target", targetBinding)
                    .AddCell(default, Cell(targetMedia))
                    .Build()
            };
            var receipts = new NavigationOperationReceipt[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                var operation = new NavigationMapCommitOperation(
                    new PreparedNavigationMap(maps[i], bakeVersion: 1),
                    OverlayReplacementPolicy.Clear,
                    operationSequence: i + 1,
                    effectiveFrame: context.FrameCount + 1);
                context.Pathing.Admit(operation).Should().BeTrue();
                receipts[i] = operation.Receipt;
            }
            for (int frame = 0;
                frame < 256 && receipts[1].Status == NavigationOperationStatus.Pending;
                frame++)
            {
                context.Simulate();
            }
            receipts.Should().OnlyContain(
                receipt => receipt.Status == NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateOverrideContext(
        out NavigationCellAddress address,
        out Vector3d sourceAnchor,
        out Vector3d sourceAction,
        out Vector3d targetAction)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        address = new NavigationCellAddress("map", default);
        sourceAnchor = default;
        sourceAction = default;
        targetAction = default;
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2));
            context.World.TryAddGrid(configuration, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            binding.TryGetCellPrism(default, out GridCellPrism prism)
                .Should().BeTrue();
            sourceAnchor = new Vector3d(
                prism.Center.X,
                prism.VerticalMin,
                prism.Center.Z);
            Fixed64 quarter = Fixed64.FromFraction(1, 4);
            sourceAction = sourceAnchor + new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero);
            targetAction = sourceAnchor - new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero);
            var transition = new TraversalTransitionDefinition(
                "same-medium-jump",
                TraversalTransitionType.Jump,
                default,
                TraversalMedium.Solid,
                address,
                TraversalMedium.Solid,
                additionalCost: (Fixed64)2,
                sourcePointOverride: sourceAction,
                hasSourcePointOverride: true,
                destinationPointOverride: targetAction,
                hasDestinationPointOverride: true);
            NavigationMap map = new NavigationMapBuilder("map", binding)
                .AddCell(default, Cell(TraversalMedia.Solid, enterCost: (Fixed64)3))
                .AddTransition(transition)
                .Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationCell Cell(
        TraversalMedia media,
        Fixed64 enterCost = default) => new(
        media,
        TraversalCapability.None,
        default,
        enterCost,
        (Fixed64)4,
        (Fixed64)4);

    private static NavigationAgentProfile Profile(
        TraversalMedia media = TraversalMedia.Solid | TraversalMedia.Gas,
        TraversalCapability capabilities = TraversalCapability.None,
        Fixed64 radius = default) => new(
        new KinematicBodyShape(
            radius == Fixed64.Zero ? Fixed64.Half : radius,
            Fixed64.One,
            Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        media,
        capabilities);

    private static NavigationWorkBudget Budget(int maxTransitionCandidates = 64) => new(
        maxLookupProbes: 64,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 64,
        maxConnectionLegs: 64,
        maxTransitionCandidates: maxTransitionCandidates,
        maxTransitionPairs: 64,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 64,
        maxSimplificationRays: 0);

    private static NavigationWorkBudget LargeBudget() => new(
        maxLookupProbes: 4_096,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 4_096,
        maxConnectionLegs: 4_096,
        maxTransitionCandidates: 4_096,
        maxTransitionPairs: 4_096,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 4_096,
        maxSimplificationRays: 0);
}
