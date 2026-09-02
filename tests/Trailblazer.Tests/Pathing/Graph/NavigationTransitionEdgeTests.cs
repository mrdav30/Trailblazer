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
            actionCost: (Fixed64)5);
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
            actionCost: Fixed64.MaxValue);
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
    public void DefinitionEvaluator_ShouldDistinguishStaleCapabilityAndDependencyStates()
    {
        var definition = new TraversalTransitionDefinition(
            "climb",
            TraversalTransitionType.Climb,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("b-target", default),
            TraversalMedium.Gas,
            requiredCapabilities: TraversalCapability.Climb,
            actionCost: (Fixed64)5);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            TraversalMedia.Solid,
            TraversalMedia.Gas,
            new[] { definition },
            System.Array.Empty<TraversalTransitionRule>());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        NavigationTransitionPage.Enumerator candidates =
            lease.Graph.EnumerateOutgoingTransitionCandidates(source);
        candidates.MoveNext().Should().BeTrue();
        NavigationPublishedTransition published = candidates.Current;
        candidates.MoveNext().Should().BeFalse();
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var evaluatorWithoutClimb = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);

        var staleDependencies = new NavigationDependencyWorkspace(2, 0);
        evaluatorWithoutClimb.EvaluateDefinition(
                new NavigationMediumStateRef(source.Node, TraversalMedium.Gas),
                published,
                meter,
                staleDependencies,
                out NavigationMediumStateRef staleTarget,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Stale);
        staleTarget.IsValid.Should().BeFalse();
        staleDependencies.PageCount.Should().Be(0);

        var capabilityDependencies = new NavigationDependencyWorkspace(2, 0);
        evaluatorWithoutClimb.EvaluateDefinition(
                source,
                published,
                meter,
                capabilityDependencies,
                out NavigationMediumStateRef capabilityTarget,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Impassable);
        capabilityTarget.Medium.Should().Be(TraversalMedium.Gas);
        capabilityDependencies.PageCount.Should().Be(2,
            "both endpoint pages must invalidate a capability rejection");
        capabilityDependencies.Pages[0].Should().Be(
            new GraphPageDependencyAddress("a-source", 0));
        capabilityDependencies.Pages[1].Should().Be(
            new GraphPageDependencyAddress("b-target", 0));

        var evaluatorWithClimb = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(capabilities: TraversalCapability.Climb),
            Policy,
            workspace);
        var emptyDependencies = new NavigationDependencyWorkspace(0, 0);
        evaluatorWithClimb.EvaluateDefinition(
                source,
                published,
                meter,
                emptyDependencies,
                out NavigationMediumStateRef emptyCapacityTarget,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.CapacityExceeded);
        emptyCapacityTarget.Medium.Should().Be(TraversalMedium.Gas,
            "the target resolves before endpoint dependency ownership is attempted");
        emptyDependencies.PageCount.Should().Be(0);

        var constrainedDependencies = new NavigationDependencyWorkspace(1, 0);
        evaluatorWithClimb.EvaluateDefinition(
                source,
                published,
                meter,
                constrainedDependencies,
                out _,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.CapacityExceeded);
        constrainedDependencies.PageCount.Should().Be(1);
        constrainedDependencies.Pages[0].Should().Be(
            new GraphPageDependencyAddress("a-source", 0));

        var completeDependencies = new NavigationDependencyWorkspace(2, 0);
        evaluatorWithClimb.EvaluateDefinition(
                source,
                published,
                meter,
                completeDependencies,
                out NavigationMediumStateRef target,
                out NavigationTransitionEdgeEvidence evidence)
            .Should().Be(NavigationTraversalEvaluationStatus.Passable);
        target.Should().Be(capabilityTarget);
        evidence.Cost.Should().Be((Fixed64)5);
        completeDependencies.PageCount.Should().Be(2);
    }

    [Fact]
    public void DefinitionEvaluator_ShouldRejectReplayFromAnotherPublishedSourceNode()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var otherIndex = new VoxelIndex(1, 0, 0);
        var definition = new TraversalTransitionDefinition(
            "source-owned",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", otherIndex),
            TraversalMedium.Solid);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            otherIndex,
            Cell(TraversalMedia.Solid),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationPublishedTransition published = GetPublishedDefinition(
            lease.Graph,
            sourceIndex,
            out _);
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", otherIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef otherSource)
            .Should().BeTrue();
        var dependencies = new NavigationDependencyWorkspace(2, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Solid),
            Policy,
            new NavigationRayWorkspace(1, 8, 8, 16, 0));

        evaluator.EvaluateDefinition(
                otherSource,
                published,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out NavigationMediumStateRef target,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Stale);

        target.IsValid.Should().BeFalse();
        dependencies.PageCount.Should().Be(0,
            "a source-owned candidate must be rejected before acquiring dependencies");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefinitionEvaluator_ShouldRejectEitherImpassablePublishedEndpoint(
        bool blockSource)
    {
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(2, 0, 0);
        var definition = new TraversalTransitionDefinition(
            "endpoint-admission",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas);
        TraversalCapability sourceRequirement = blockSource
            ? TraversalCapability.Climb
            : TraversalCapability.None;
        TraversalCapability targetRequirement = blockSource
            ? TraversalCapability.None
            : TraversalCapability.Climb;
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid, requiredCapabilities: sourceRequirement),
            targetIndex,
            Cell(TraversalMedia.Gas, requiredCapabilities: targetRequirement),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationPublishedTransition published = GetPublishedDefinition(
            lease.Graph,
            sourceIndex,
            out NavigationMediumStateRef source);
        lease.Graph.TryGetMediumStateRef(
                definition.Destination,
                TraversalMedium.Gas,
                out NavigationMediumStateRef expectedTarget)
            .Should().BeTrue();
        var dependencies = new NavigationDependencyWorkspace(2, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            new NavigationRayWorkspace(1, 8, 8, 16, 0));

        evaluator.EvaluateDefinition(
                source,
                published,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out NavigationMediumStateRef target,
                out NavigationTransitionEdgeEvidence evidence)
            .Should().Be(NavigationTraversalEvaluationStatus.Impassable,
                blockSource
                    ? "the source cell requires a capability the agent does not own"
                    : "the destination cell requires a capability the agent does not own");

        target.Should().Be(expectedTarget);
        evidence.Should().Be(default(NavigationTransitionEdgeEvidence));
        dependencies.PageCount.Should().Be(1,
            "both same-page endpoints are retained before deterministic admission fails");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuleEvaluator_ShouldRejectEitherImpassablePublishedEndpoint(
        bool blockSource)
    {
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(1, 0, 0);
        var rule = new TraversalTransitionRule(
            "endpoint-admission",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Solid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        TraversalCapability sourceRequirement = blockSource
            ? TraversalCapability.Climb
            : TraversalCapability.None;
        TraversalCapability targetRequirement = blockSource
            ? TraversalCapability.None
            : TraversalCapability.Climb;
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid, requiredCapabilities: sourceRequirement),
            targetIndex,
            Cell(TraversalMedia.Gas, requiredCapabilities: targetRequirement),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", targetIndex),
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var dependencies = new NavigationDependencyWorkspace(2, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            new NavigationRayWorkspace(1, 8, 8, 16, 0));

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out NavigationTransitionEdgeEvidence evidence)
            .Should().Be(NavigationTraversalEvaluationStatus.Impassable,
                blockSource
                    ? "the source cell requires a capability the agent does not own"
                    : "the destination cell requires a capability the agent does not own");

        evidence.Should().Be(default(NavigationTransitionEdgeEvidence));
        dependencies.PageCount.Should().Be(1,
            "both same-page endpoints are retained before deterministic admission fails");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TransitionEndpointAnchorAdmission_ShouldRejectEitherUnrepresentableVolumeAnchor(
        bool definitionIdentity,
        bool failSource)
    {
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(1, 0, 0);
        TraversalMedium sourceMedium = failSource
            ? TraversalMedium.Gas
            : TraversalMedium.Solid;
        TraversalMedium targetMedium = failSource
            ? TraversalMedium.Solid
            : TraversalMedium.Gas;
        TraversalMedia sourceMedia = failSource
            ? TraversalMedia.Gas
            : TraversalMedia.Solid;
        TraversalMedia targetMedia = failSource
            ? TraversalMedia.Solid
            : TraversalMedia.Gas;
        Fixed64 vertical = Fixed64.MinValue + Fixed64.One;
        GridConfiguration configuration = new(
            new Vector3d(Fixed64.Zero, vertical, Fixed64.Zero),
            new Vector3d(Fixed64.One, vertical, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        NavigationCell sourceCell = new(
            sourceMedia,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.MaxValue);
        NavigationCell targetCell = new(
            targetMedia,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.MaxValue);
        var definition = new TraversalTransitionDefinition(
            "anchor-admission",
            TraversalTransitionType.Jump,
            sourceIndex,
            sourceMedium,
            new NavigationCellAddress("map", targetIndex),
            targetMedium);
        var rule = new TraversalTransitionRule(
            "anchor-admission",
            TraversalTransitionType.Jump,
            sourceMedium,
            targetMedium,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            Fixed64.Zero,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            configuration,
            sourceIndex,
            sourceCell,
            targetIndex,
            targetCell,
            definitionIdentity ? new[] { definition } : System.Array.Empty<TraversalTransitionDefinition>(),
            definitionIdentity ? System.Array.Empty<TraversalTransitionRule>() : new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                sourceMedium,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", targetIndex),
                targetMedium,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Gas,
            TraversalCapability.None);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            profile,
            Policy,
            new NavigationRayWorkspace(1, 8, 8, 16, 0));
        var dependencies = new NavigationDependencyWorkspace(2, 0);
        NavigationTraversalEvaluationStatus status;
        NavigationTransitionEdgeEvidence evidence;
        if (definitionIdentity)
        {
            NavigationTransitionPage.Enumerator candidates =
                lease.Graph.EnumerateOutgoingTransitionCandidates(source);
            candidates.MoveNext().Should().BeTrue();
            status = evaluator.EvaluateDefinition(
                source,
                candidates.Current,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out _,
                out evidence);
        }
        else
        {
            status = evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out evidence);
        }

        status.Should().Be(NavigationTraversalEvaluationStatus.Impassable);
        evidence.Should().Be(default(NavigationTransitionEdgeEvidence));
        dependencies.PageCount.Should().Be(1,
            "both same-page endpoints are retained before anchor admission fails");
    }

    [Theory]
    [InlineData(
        0,
        8,
        64,
        64,
        (int)NavigationTraversalEvaluationStatus.BudgetExceeded,
        (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded)]
    [InlineData(
        64,
        8,
        0,
        64,
        (int)NavigationTraversalEvaluationStatus.CapacityExceeded,
        (int)NavigationTraversalEdgeAdvanceStatus.CapacityExceeded)]
    public void DefinitionTargetVolumeLeg_ShouldPreserveBudgetVersusWorkspaceCapacity(
        int lookupBudget,
        int coveredBudget,
        int mapCapacity,
        int addressCapacity,
        int expectedStatus,
        int expectedDispatcherStatus)
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(2, 0, 0);
        TransitionConfiguration().TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(targetIndex, out GridCellPrism targetPrism)
            .Should().BeTrue();
        var definition = new TraversalTransitionDefinition(
            "bounded-target-leg",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas,
            destinationPointOverride: targetPrism.Center,
            hasDestinationPointOverride: true);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationPublishedTransition published = GetPublishedDefinition(
            lease.Graph,
            sourceIndex,
            out NavigationMediumStateRef source);
        var workspace = new NavigationRayWorkspace(
            mapCapacity,
            8,
            8,
            addressCapacity,
            0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);

        evaluator.EvaluateDefinition(
                source,
                published,
                new NavigationWorkMeter(VolumeBudget(lookupBudget, coveredBudget)),
                new NavigationDependencyWorkspace(8, 0),
                out _,
                out _)
            .Should().Be((NavigationTraversalEvaluationStatus)expectedStatus,
                "the failed destination action leg must retain the exact limiting resource");

        workspace.Reset();
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var dispatcherMeter = new NavigationWorkMeter(
            VolumeBudget(lookupBudget, coveredBudget));
        int remaining = 64;
        int connectionRemaining = int.MaxValue;
        NavigationTraversalEdgeAdvanceStatus dispatcherStatus;
        do
        {
            dispatcherStatus = dispatcher.AdvanceOne(
                dispatcherMeter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (dispatcherStatus == NavigationTraversalEdgeAdvanceStatus.Pending);

        dispatcherStatus.Should().Be(
            (NavigationTraversalEdgeAdvanceStatus)expectedDispatcherStatus);
        dispatcher.CurrentTarget.IsValid.Should().BeFalse();
        dispatcherMeter.TransitionPairs.Should().Be(1);

        workspace.Reset();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", targetIndex),
                TraversalMedium.Gas,
                out NavigationMediumStateRef destination)
            .Should().BeTrue();
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            destination,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true);
        var incomingMeter = new NavigationWorkMeter(
            VolumeBudget(lookupBudget, coveredBudget));
        remaining = 64;
        do
        {
            dispatcherStatus = incoming.AdvanceOne(
                incomingMeter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (dispatcherStatus == NavigationTraversalEdgeAdvanceStatus.Pending);

        dispatcherStatus.Should().Be(
            (NavigationTraversalEdgeAdvanceStatus)expectedDispatcherStatus,
            "incoming replay must preserve the same target-leg limiting resource");
        incoming.CurrentPredecessor.IsValid.Should().BeFalse();
        incomingMeter.TransitionPairs.Should().Be(1);
    }

    [Fact]
    public void DefinitionTargetVolumeLeg_ShouldPreserveBodyTraceArithmeticOverflow()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(2, 0, 0);
        var definition = new TraversalTransitionDefinition(
            "overflowing-volume-leg",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas);
        var permissiveSolid = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.One);
        var permissiveGas = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.MaxValue,
            Fixed64.One);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            permissiveSolid,
            targetIndex,
            permissiveGas,
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationPublishedTransition published = GetPublishedDefinition(
            lease.Graph,
            sourceIndex,
            out NavigationMediumStateRef source);
        var workspace = new NavigationRayWorkspace(1, 8, 8, 64, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(radius: Fixed64.MaxValue),
            Policy,
            workspace);

        NavigationTraversalEvaluationStatus status = evaluator.EvaluateDefinition(
            source,
            published,
            new NavigationWorkMeter(VolumeBudget(64, 64)),
            workspace.Dependencies,
            out _,
            out _);

        status.Should().Be(NavigationTraversalEvaluationStatus.CostOverflow,
            "an unrepresentable destination volume body is arithmetic failure, not blockage");
        workspace.Dependencies.PageCount.Should().BePositive();
    }

    [Fact]
    public void RuleEvaluator_ShouldEnforcePageCapacityBeforeCostAndCheckOverflowExactly()
    {
        var address = new NavigationCellAddress("map", default);
        var rule = new TraversalTransitionRule(
            "takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Solid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.None,
            Fixed64.MaxValue,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas, enterCost: Fixed64.One),
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(address, TraversalMedium.Solid, out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(address, TraversalMedium.Gas, out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);
        var meter = new NavigationWorkMeter(Budget());

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                meter,
                new NavigationDependencyWorkspace(0, 0),
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.CapacityExceeded);
        evaluator.EvaluateRule(
                source,
                target,
                rule,
                meter,
                new NavigationDependencyWorkspace(1, 0),
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.CostOverflow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TransitionCostPolicy_ShouldPreserveEveryCheckedStageAndExactSum(int stage)
    {
        Vector3d sourceAnchor = Vector3d.Zero;
        Vector3d sourceAction = Vector3d.Zero;
        Vector3d targetAction = Vector3d.Zero;
        Vector3d targetAnchor = Vector3d.Zero;
        Fixed64 actionCost = Fixed64.Zero;
        Fixed64 targetEnterCost = Fixed64.Zero;
        Fixed64 additionalEnterCost = Fixed64.Zero;
        var minimum = new Vector3d(
            Fixed64.MinValue,
            Fixed64.MinValue,
            Fixed64.MinValue);
        var maximum = new Vector3d(
            Fixed64.MaxValue,
            Fixed64.MaxValue,
            Fixed64.MaxValue);

        switch (stage)
        {
            case 0:
                sourceAnchor = minimum;
                sourceAction = maximum;
                break;
            case 1:
                sourceAction = Vector3d.Right;
                actionCost = Fixed64.MaxValue;
                break;
            case 2:
                targetAction = minimum;
                targetAnchor = maximum;
                break;
            case 3:
                actionCost = Fixed64.MaxValue;
                targetAnchor = Vector3d.Right;
                break;
            case 4:
                actionCost = Fixed64.MaxValue;
                targetEnterCost = Fixed64.One;
                break;
            case 5:
                actionCost = Fixed64.MaxValue;
                additionalEnterCost = Fixed64.One;
                break;
            default:
                sourceAction = Vector3d.Right;
                actionCost = (Fixed64)2;
                targetAnchor = Vector3d.Right;
                targetEnterCost = (Fixed64)3;
                additionalEnterCost = (Fixed64)4;
                break;
        }

        bool succeeded = NavigationTransitionEdgeEvaluator.TryGetCost(
            sourceAnchor,
            sourceAction,
            actionCost,
            targetAction,
            targetAnchor,
            targetEnterCost,
            additionalEnterCost,
            out Fixed64 total);

        succeeded.Should().Be(stage == 6);
        if (succeeded)
            total.Should().Be((Fixed64)11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefinitionEvaluator_ShouldReportTheExactRemainingCheckedAccumulationOverflow(
        int overflowStage)
    {
        GridConfiguration configuration = TransitionConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(default, out GridCellPrism prism)
            .Should().BeTrue();
        var anchor = new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
        Fixed64 quarter = Fixed64.FromFraction(1, 4);
        Vector3d sourceAction = overflowStage == 0
            ? anchor + new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero)
            : anchor;
        Vector3d targetAction = overflowStage == 1
            ? anchor - new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero)
            : anchor;
        var definition = new TraversalTransitionDefinition(
            $"definition-overflow-{overflowStage}",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", default),
            TraversalMedium.Solid,
            actionCost: Fixed64.MaxValue,
            sourcePointOverride: sourceAction,
            hasSourcePointOverride: true,
            destinationPointOverride: targetAction,
            hasDestinationPointOverride: true);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Solid),
            default,
            Cell(TraversalMedia.Solid),
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationPublishedTransition published = GetPublishedDefinition(
            lease.Graph,
            default,
            out NavigationMediumStateRef source);
        NavigationAreaPolicy policy = overflowStage == 2
            ? new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("definition-overflow", 1),
                new[] { new NavigationAreaRule(true, Fixed64.One) })
            : Policy;
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Solid),
            policy,
            workspace);

        evaluator.EvaluateDefinition(
                source,
                published,
                new NavigationWorkMeter(Budget()),
                workspace.Dependencies,
                out _,
                out _)
            .Should().Be(
                NavigationTraversalEvaluationStatus.CostOverflow,
                overflowStage switch
                {
                    0 => "the positive source leg must overflow when the maximum action cost is added",
                    1 => "the positive target leg must overflow after a maximum representable action total",
                    _ => "the policy surcharge must overflow after a maximum representable transition total"
                });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RuleEvaluator_ShouldReportTheExactRemainingCheckedAccumulationOverflow(
        int overflowStage)
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(1, 0, 0);
        GridConfiguration configuration = TransitionConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        binding.TryGetCellPrism(sourceIndex, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(targetIndex, out GridCellPrism targetPrism)
            .Should().BeTrue();
        var sourceAnchor = new Vector3d(
            sourcePrism.Center.X,
            sourcePrism.Center.Y - Fixed64.Half,
            sourcePrism.Center.Z);
        var targetAnchor = new Vector3d(
            targetPrism.Center.X,
            targetPrism.Center.Y - Fixed64.Half,
            targetPrism.Center.Z);
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
        Fixed64 actionCost = Fixed64.MaxValue;
        if (overflowStage > 0)
        {
            Fixed64.TrySubtract(actionCost, sourceDistance, out actionCost)
                .Should().BeTrue();
        }
        if (overflowStage > 1)
        {
            Fixed64.TrySubtract(actionCost, targetDistance, out actionCost)
                .Should().BeTrue();
        }
        var rule = new TraversalTransitionRule(
            $"rule-overflow-{overflowStage}",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            actionCost,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Liquid),
            targetIndex,
            Cell(
                TraversalMedia.Gas,
                enterCost: overflowStage == 2 ? Fixed64.One : Fixed64.Zero),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
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
        NavigationAreaPolicy policy = overflowStage == 3
            ? new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("rule-overflow", 1),
                new[] { new NavigationAreaRule(true, Fixed64.One) })
            : Policy;
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            policy,
            workspace);

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(Budget()),
                workspace.Dependencies,
                out _)
            .Should().Be(
                NavigationTraversalEvaluationStatus.CostOverflow,
                overflowStage switch
                {
                    0 => "the positive source leg must overflow when the maximum action cost is added",
                    1 => "the positive target leg must overflow after a maximum representable action total",
                    2 => "the target enter cost must overflow after a maximum representable transition total",
                    _ => "the policy surcharge must overflow after a maximum representable transition total"
                });
    }

    [Theory]
    [InlineData(0, 1, (int)TraversalMedium.Solid, (int)TraversalMedium.Gas,
        (int)TraversalCapability.None)]
    [InlineData(0, 0, (int)TraversalMedium.Gas, (int)TraversalMedium.Gas,
        (int)TraversalCapability.None)]
    [InlineData(0, 0, (int)TraversalMedium.Solid, (int)TraversalMedium.Solid,
        (int)TraversalCapability.None)]
    [InlineData(0, 0, (int)TraversalMedium.Solid, (int)TraversalMedium.Gas,
        (int)TraversalCapability.Climb)]
    public void SameCellRule_ShouldRejectTopologyMediumAndCapabilityMismatchesBeforeDependencies(
        int sourceX,
        int targetX,
        int sourceMediumValue,
        int targetMediumValue,
        int requiredCapabilitiesValue)
    {
        var sourceIndex = new VoxelIndex(sourceX, 0, 0);
        var targetIndex = new VoxelIndex(targetX, 0, 0);
        var sourceMedium = (TraversalMedium)sourceMediumValue;
        var targetMedium = (TraversalMedium)targetMediumValue;
        var rule = new TraversalTransitionRule(
            "guarded",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Solid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.SameCell,
            (TraversalCapability)requiredCapabilitiesValue,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        NavigationCell allMedia = Cell(
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid);
        using TrailblazerWorldContext context = CreateContext(
            default,
            allMedia,
            new VoxelIndex(1, 0, 0),
            allMedia,
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                sourceMedium,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", targetIndex),
                targetMedium,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            new NavigationRayWorkspace(1, 8, 8, 16, 0));
        var dependencies = new NavigationDependencyWorkspace(2, 0);

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Impassable);
        dependencies.PageCount.Should().Be(0,
            "rule identity and capability rejection precedes endpoint dependency ownership");
    }

    [Theory]
    [InlineData((int)TraversalMedium.Gas, (int)TraversalMedium.Solid)]
    [InlineData((int)TraversalMedium.Solid, (int)TraversalMedium.Gas)]
    public void SameCellRule_ShouldPropagateBudgetFailureFromTheExactVolumeLeg(
        int sourceMediumValue,
        int targetMediumValue)
    {
        var sourceMedium = (TraversalMedium)sourceMediumValue;
        var targetMedium = (TraversalMedium)targetMediumValue;
        var address = new NavigationCellAddress("map", default);
        var rule = new TraversalTransitionRule(
            "bounded-takeoff",
            TraversalTransitionType.Takeoff,
            sourceMedium,
            targetMedium,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(address, sourceMedium, out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(address, targetMedium, out NavigationMediumStateRef target)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(VolumeBudget(0, 16)),
                new NavigationDependencyWorkspace(1, 0),
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.BudgetExceeded,
                "the zero-length volume placement still requires bounded physical certification");
    }

    [Fact]
    public void SameCellRule_WhenRetainedGraphTrailsRawGridMutation_ShouldReturnStale()
    {
        var address = new NavigationCellAddress("map", default);
        var rule = new TraversalTransitionRule(
            "stale-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Solid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.None,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            default,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas),
            System.Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Gas,
                out NavigationMediumStateRef target)
            .Should().BeTrue();
        context.World.ActiveGrids[0].TryRemoveVoxel(default).Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);

        evaluator.EvaluateRule(
                source,
                target,
                rule,
                new NavigationWorkMeter(VolumeBudget(64, 64)),
                workspace.Dependencies,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Stale);

        workspace.Dependencies.PageCount.Should().Be(1,
            "the transition endpoint page is retained before the stale physical trace is rejected");

        workspace.Reset();
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(VolumeBudget(64, 64));
        int edgeRemaining = 64;
        int connectionRemaining = int.MaxValue;
        NavigationTraversalEdgeAdvanceStatus dispatcherStatus;
        do
        {
            dispatcherStatus = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref edgeRemaining,
                ref connectionRemaining);
        }
        while (dispatcherStatus == NavigationTraversalEdgeAdvanceStatus.Pending);

        dispatcherStatus.Should().Be(NavigationTraversalEdgeAdvanceStatus.Stale);
        dispatcher.CurrentTarget.IsValid.Should().BeFalse();
        meter.TransitionPairs.Should().Be(1);
    }

    [Fact]
    public void DefinitionEvaluator_ShouldKeepRemovedDestinationAsImpassableCandidate()
    {
        var definition = new TraversalTransitionDefinition(
            "dormant-target",
            TraversalTransitionType.Jump,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("b-target", default),
            TraversalMedium.Gas,
            actionCost: Fixed64.One);
        using TrailblazerWorldContext context = CreateCrossMapContext(
            TraversalMedia.Solid,
            TraversalMedia.Gas,
            new[] { definition },
            System.Array.Empty<TraversalTransitionRule>());
        var remove = new NavigationMapRemoveOperation(
            "b-target",
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        while (remove.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("a-source", default),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        NavigationTransitionPage.Enumerator candidates =
            lease.Graph.EnumerateOutgoingTransitionCandidates(source);
        candidates.MoveNext().Should().BeTrue(
            "the source-owned candidate must survive for deterministic target reactivation");
        NavigationPublishedTransition published = candidates.Current;
        var workspace = new NavigationRayWorkspace(2, 8, 8, 16, 0);
        var dependencies = new NavigationDependencyWorkspace(2, 0);
        var evaluator = new NavigationTransitionEdgeEvaluator(
            context.World,
            lease.Graph,
            Profile(),
            Policy,
            workspace);

        evaluator.EvaluateDefinition(
                source,
                published,
                new NavigationWorkMeter(Budget()),
                dependencies,
                out NavigationMediumStateRef target,
                out _)
            .Should().Be(NavigationTraversalEvaluationStatus.Impassable);
        target.IsValid.Should().BeFalse();
        dependencies.PageCount.Should().Be(0,
            "there is no target page to retain until that map is republished");
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
            actionCost: Fixed64.One);
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
    public void Dispatcher_ShouldOrderSameAddressRulesByDestinationMediumBeforeType()
    {
        var index = new VoxelIndex(0, 0, 0);
        TraversalTransitionRule[] rules =
        {
            new(
                "gas-jump",
                TraversalTransitionType.Jump,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.One,
                TraversalTransitionLocomotionHints.None),
            new(
                "liquid-climb",
                TraversalTransitionType.Climb,
                TraversalMedium.Solid,
                TraversalMedium.Liquid,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.One,
                TraversalTransitionLocomotionHints.None)
        };
        TraversalMedia media = TraversalMedia.Solid
            | TraversalMedia.Gas
            | TraversalMedia.Liquid;
        using TrailblazerWorldContext context = CreateContext(
            index,
            Cell(media),
            index,
            Cell(media),
            System.Array.Empty<TraversalTransitionDefinition>(),
            rules);
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
            Profile(media),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        var meter = new NavigationWorkMeter(Budget());
        int remaining = 64;
        int connectionRemaining = int.MaxValue;
        var emittedMedia = new SwiftList<TraversalMedium>(2);

        while (true)
        {
            NavigationTraversalEdgeAdvanceStatus status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
                break;
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Edge);
            if (status == NavigationTraversalEdgeAdvanceStatus.Edge)
                emittedMedia.Add(dispatcher.CurrentTarget.Medium);
        }

        emittedMedia.Should().Equal(TraversalMedium.Gas, TraversalMedium.Liquid);
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
            actionCost: Fixed64.One);
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
            actionCost: Fixed64.One);
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
            actionCost: Fixed64.One);
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
            actionCost: Fixed64.One,
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
        long transitionUnionChecks = -1;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            int priorPairs = meter.TransitionPairs;
            long priorUnionChecks = meter.VolumeUnionChecks;
            status = dispatcher.AdvanceOne(meter, workspace.Dependencies, ref remaining, ref connectionRemaining);
            if (meter.TransitionPairs != priorPairs)
                transitionUnionChecks = meter.VolumeUnionChecks - priorUnionChecks;
        }
        while (status != NavigationTraversalEdgeAdvanceStatus.Complete
            && (status != NavigationTraversalEdgeAdvanceStatus.Edge
                || dispatcher.CurrentKind != NavigationTraversalEdgeKind.Transition));

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        dispatcher.CurrentTarget.Should().Be(source);
        dispatcher.CurrentTransitionSourceAction.Should().Be(sourceAction);
        dispatcher.CurrentTransitionDestinationAction.Should().Be(destinationAction);
        transitionUnionChecks.Should().Be(2,
            "each large-body action leg owns one canonical union trace");
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
        int candidatesBeforeFirstSeam = 1 + graph.GetPrimaryDirectionCount(source.Node);
        var sliceMeter = new NavigationWorkMeter(Budget());
        NavigationTraversalEdgeAdvanceStatus status =
            NavigationTraversalEdgeAdvanceStatus.Pending;
        while (sliceMeter.TransitionCandidates < candidatesBeforeFirstSeam)
        {
            int oneStep = 1;
            int connectionRemaining = int.MaxValue;
            status = dispatcher.AdvanceOne(
                sliceMeter,
                workspace.Dependencies,
                ref oneStep,
                ref connectionRemaining);
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Blocked);
        }
        int noSteps = 0;
        int sliceConnections = int.MaxValue;
        dispatcher.AdvanceOne(
                sliceMeter,
                workspace.Dependencies,
                ref noSteps,
                ref sliceConnections)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked,
                "automatic seam lookahead cannot consume a caller-owned zero-step slice");
        sliceMeter.TransitionCandidates.Should().Be(candidatesBeforeFirstSeam,
            "the unconsumed first seam remains pending for the next slice");

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
        const int exactCandidates = 40;
        var meter = new NavigationWorkMeter(Budget(exactCandidates));
        status = NavigationTraversalEdgeAdvanceStatus.Pending;

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
    public void IncomingDefinition_ShouldPreserveHostSliceAndTransitionBudgetOwnership()
    {
        VoxelIndex index = default;
        var address = new NavigationCellAddress("map", index);
        var definition = new TraversalTransitionDefinition(
            "same-cell-action",
            TraversalTransitionType.Jump,
            index,
            TraversalMedium.Solid,
            address,
            TraversalMedium.Gas,
            actionCost: Fixed64.One);
        NavigationCell cell = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        using TrailblazerWorldContext context = CreateContext(
            index,
            cell,
            index,
            cell,
            definition);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                address,
                TraversalMedium.Gas,
                out NavigationMediumStateRef destination)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            destination,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true);
        int edgeSteps = 0;
        int connectionSteps = int.MaxValue;

        incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref edgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked);
        meter.TransitionCandidates.Should().Be(0,
            "host slicing must stop before the published definition is charged");

        workspace.Reset();
        meter = new NavigationWorkMeter(Budget(maxTransitionCandidates: 0));
        incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            destination,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true);
        edgeSteps = 1;

        incoming.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref edgeSteps,
                ref connectionSteps)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        edgeSteps.Should().Be(1,
            "failed transition metering cannot consume the caller-owned edge slice");
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
        int candidatesBeforeFirstSeam = 1 + graph.GetPrimaryDirectionCount(target.Node);
        var sliceMeter = new NavigationWorkMeter(Budget());
        NavigationTraversalEdgeAdvanceStatus status =
            NavigationTraversalEdgeAdvanceStatus.Pending;
        while (sliceMeter.TransitionCandidates < candidatesBeforeFirstSeam)
        {
            int oneStep = 1;
            int connectionRemaining = int.MaxValue;
            status = incoming.AdvanceOne(
                sliceMeter,
                workspace.Dependencies,
                ref oneStep,
                ref connectionRemaining);
            status.Should().BeOneOf(
                NavigationTraversalEdgeAdvanceStatus.Pending,
                NavigationTraversalEdgeAdvanceStatus.Blocked);
        }
        int noSteps = 0;
        int sliceConnections = int.MaxValue;
        incoming.AdvanceOne(
                sliceMeter,
                workspace.Dependencies,
                ref noSteps,
                ref sliceConnections)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Blocked,
                "incoming automatic seam lookahead cannot consume a zero-step host slice");
        sliceMeter.TransitionCandidates.Should().Be(candidatesBeforeFirstSeam,
            "the unconsumed incoming seam remains pending for the next slice");

        workspace.Reset();
        incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            graph,
            target,
            Profile(TraversalMedia.Liquid | TraversalMedia.Gas),
            Policy,
            workspace,
            allowTransitions: true);
        const int exactCandidates = 163;
        var meter = new NavigationWorkMeter(Budget(exactCandidates));
        status = NavigationTraversalEdgeAdvanceStatus.Pending;

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
            actionCost: Fixed64.One);
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
            actionCost: Fixed64.Zero);
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
    public void TransitionDispatchers_ShouldRequireTheEndpointPageBeforeScanning()
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
                out NavigationMediumStateRef state)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget());
        var outgoingDependencies = new NavigationDependencyWorkspace(0, 0);
        var outgoing = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            state,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        outgoing.AdvanceOne(
                meter,
                outgoingDependencies,
                ref remaining,
                ref connectionRemaining)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.CapacityExceeded);
        outgoingDependencies.HasTransitionDependency.Should().BeTrue();
        outgoingDependencies.PageCount.Should().Be(0);
        meter.EvaluatedEdges.Should().Be(0);
        meter.TransitionCandidates.Should().Be(0);
        meter.TransitionPairs.Should().Be(0);

        var incomingDependencies = new NavigationDependencyWorkspace(0, 0);
        var incoming = new NavigationIncomingTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            state,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: true);

        incoming.AdvanceOne(
                meter,
                incomingDependencies,
                ref remaining,
                ref connectionRemaining)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.CapacityExceeded);
        incomingDependencies.HasTransitionDependency.Should().BeTrue();
        incomingDependencies.PageCount.Should().Be(0);
        meter.EvaluatedEdges.Should().Be(0);
        meter.TransitionCandidates.Should().Be(0);
        meter.TransitionPairs.Should().Be(0);
    }

    [Fact]
    public void TransitionDispatcher_ShouldStopBeforeEvaluatingWhenPairBudgetIsEmpty()
    {
        var index = new VoxelIndex(0, 0, 0);
        var rule = new TraversalTransitionRule(
            "bounded-takeoff",
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
                new NavigationCellAddress("map", index),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget(maxTransitionPairs: 0));
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
            status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.BudgetExceeded);
        dispatcher.CurrentTarget.IsValid.Should().BeFalse();
        meter.TransitionCandidates.Should().BeGreaterThan(0,
            "candidate discovery precedes the independently bounded pair evaluation");
        meter.TransitionPairs.Should().Be(0);
    }

    [Theory]
    [InlineData(0, (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded)]
    [InlineData(1, (int)NavigationTraversalEdgeAdvanceStatus.Blocked)]
    public void TransitionDispatcher_ShouldClassifyAPendingPairAgainstBothLimits(
        int transitionPairBudget,
        int expectedBlockedStatus)
    {
        var index = new VoxelIndex(0, 0, 0);
        var rule = new TraversalTransitionRule(
            "resumable-takeoff",
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
                new NavigationCellAddress("map", index),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget(maxTransitionPairs: transitionPairBudget));
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(),
            Policy,
            workspace,
            allowTransitions: true,
            emittedSurfaceOrdinal: -1);
        int connectionRemaining = int.MaxValue;
        int remaining = 64;
        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending
            && meter.TransitionCandidates == 0);
        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.Pending);
        meter.TransitionPairs.Should().Be(0);

        remaining = 0;
        dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining)
            .Should().Be((NavigationTraversalEdgeAdvanceStatus)expectedBlockedStatus);
        meter.TransitionPairs.Should().Be(0,
            "neither host slicing nor an exhausted pair budget can consume retained work");

        if (transitionPairBudget == 0)
            return;
        remaining = 1;
        dispatcher.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining)
            .Should().Be(NavigationTraversalEdgeAdvanceStatus.Edge);
        meter.TransitionPairs.Should().Be(1);
    }

    [Theory]
    [InlineData(0, (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded)]
    [InlineData(1, (int)NavigationTraversalEdgeAdvanceStatus.Blocked)]
    public void TransitionCandidateDebit_ShouldDistinguishBudgetFromHostStepLimit(
        int candidateBudget,
        int expectedStatus)
    {
        var meter = new NavigationWorkMeter(Budget(maxTransitionCandidates: candidateBudget));
        int remaining = 0;

        NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
                meter,
                ref remaining,
                out NavigationTraversalEdgeAdvanceStatus blocked)
            .Should().BeFalse();

        blocked.Should().Be((NavigationTraversalEdgeAdvanceStatus)expectedStatus);
        remaining.Should().Be(0);
        meter.TransitionCandidates.Should().Be(0);
    }

    [Theory]
    [InlineData(false, 1, (int)NavigationTraversalEdgeAdvanceStatus.Blocked)]
    [InlineData(false, 0, (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded)]
    [InlineData(true, 1, (int)NavigationTraversalEdgeAdvanceStatus.Blocked)]
    [InlineData(true, 0, (int)NavigationTraversalEdgeAdvanceStatus.BudgetExceeded)]
    public void SurfaceEnumeration_ShouldDistinguishBudgetFromHostStepLimit(
        bool incoming,
        int evaluatedEdgeBudget,
        int expectedStatus)
    {
        var targetIndex = new VoxelIndex(1, 0, 0);
        using TrailblazerWorldContext context = CreateContext(
            default,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Solid),
            System.Array.Empty<TraversalTransitionDefinition>(),
            System.Array.Empty<TraversalTransitionRule>());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", incoming ? targetIndex : default),
                TraversalMedium.Solid,
                out NavigationMediumStateRef state)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var meter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: evaluatedEdgeBudget));
        int remaining = 0;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        if (incoming)
        {
            var edges = new NavigationIncomingTraversalEdgeEnumerator(
                context.World,
                lease.Graph,
                state,
                Profile(),
                Policy,
                workspace,
                allowTransitions: false);
            status = edges.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        else
        {
            var edges = new NavigationTraversalEdgeEnumerator(
                context.World,
                lease.Graph,
                state,
                Profile(),
                Policy,
                workspace,
                allowTransitions: false,
                emittedSurfaceOrdinal: -1);
            status = edges.AdvanceOne(
                meter,
                workspace.Dependencies,
                ref remaining,
                ref connectionRemaining);
        }

        status.Should().Be((NavigationTraversalEdgeAdvanceStatus)expectedStatus);
        remaining.Should().Be(0);
        meter.EvaluatedEdges.Should().Be(0);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void SurfaceCandidateGuideDebit_ShouldHonorExactCursorLegAllowance(
        int cursorLegAllowance,
        bool expected)
    {
        var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 0,
            maxCursorLegScans: cursorLegAllowance,
            maxCursorRebases: 0,
            maxPortalChecks: 0,
            maxPrismChecks: 0,
            maxTraceIntervals: 0,
            maxLocalRecoveryAttempts: 0));
        int remaining = 1;

        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter: null,
                maintenanceMeter: null,
                guideMeter: ref meter,
                useGuideMeter: true,
                edgeStepRemaining: ref remaining)
            .Should().Be(expected);

        remaining.Should().Be(expected ? 0 : 1,
            "failed guide metering must preserve the caller-owned edge slice");
        meter.GetCursorLegScanAllowance().Should().Be(0);
    }

    [Fact]
    public void SurfaceCandidateDebit_ShouldHonorEachValidMeterSourceAndHostSlice()
    {
        GuideSampleWorkMeter guideMeter = default;
        int remaining = 0;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter: null,
                maintenanceMeter: null,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeTrue("unmetered structural enumeration has no host-owned slice");
        remaining.Should().Be(0);

        var queryMeter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: 1));
        remaining = 1;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter,
                maintenanceMeter: null,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeTrue();
        queryMeter.EvaluatedEdges.Should().Be(1);
        remaining.Should().Be(0);

        queryMeter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: 0));
        remaining = 1;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter,
                maintenanceMeter: null,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeFalse();
        queryMeter.EvaluatedEdges.Should().Be(0);
        remaining.Should().Be(1);

        var maintenanceMeter = new MaintenanceWorkMeter(
            new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1, 1));
        remaining = 1;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter: null,
                maintenanceMeter,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeTrue();
        maintenanceMeter.SurfaceComponentEdges.Should().Be(1);
        remaining.Should().Be(0);

        remaining = 1;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter: null,
                maintenanceMeter,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeFalse();
        maintenanceMeter.SurfaceComponentEdges.Should().Be(1);
        remaining.Should().Be(1);

        queryMeter = new NavigationWorkMeter(Budget(maxEvaluatedEdges: 1));
        remaining = 0;
        NavigationSurfaceEdgeEnumerator.TryConsumeCandidate(
                queryMeter,
                maintenanceMeter: null,
                guideMeter: ref guideMeter,
                useGuideMeter: false,
                edgeStepRemaining: ref remaining)
            .Should().BeFalse();
        queryMeter.EvaluatedEdges.Should().Be(0,
            "an exhausted host slice is checked before the selected work meter");
    }

    [Fact]
    public void SurfaceDispatcher_ShouldRequireTheTargetPageBeforeRouteEvaluation()
    {
        var sourceIndex = new VoxelIndex(0, 0, 0);
        var targetIndex = new VoxelIndex(1, 0, 0);
        using TrailblazerWorldContext context = CreateContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Solid),
            System.Array.Empty<TraversalTransitionDefinition>(),
            System.Array.Empty<TraversalTransitionRule>());
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationMediumStateRef source)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(1, 8, 8, 16, 0);
        var dependencies = new NavigationDependencyWorkspace(0, 0);
        var meter = new NavigationWorkMeter(Budget());
        var dispatcher = new NavigationTraversalEdgeEnumerator(
            context.World,
            lease.Graph,
            source,
            Profile(TraversalMedia.Solid),
            Policy,
            workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        int remaining = 64;
        int connectionRemaining = int.MaxValue;

        NavigationTraversalEdgeAdvanceStatus status;
        do
        {
            status = dispatcher.AdvanceOne(
                meter,
                dependencies,
                ref remaining,
                ref connectionRemaining);
        }
        while (status == NavigationTraversalEdgeAdvanceStatus.Pending);

        status.Should().Be(NavigationTraversalEdgeAdvanceStatus.CapacityExceeded);
        dispatcher.CurrentTarget.IsValid.Should().BeFalse();
        dependencies.PageCount.Should().Be(0);
        meter.EvaluatedEdges.Should().Be(1,
            "the discovered edge is charged before its semantic dependency is retained");
        remaining.Should().Be(63);
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
        TraversalTransitionRule[] rules) => CreateContext(
        TransitionConfiguration(),
        sourceIndex,
        source,
        targetIndex,
        target,
        transitions,
        rules);

    private static TrailblazerWorldContext CreateContext(
        GridConfiguration configuration,
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
                actionCost: (Fixed64)2,
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
        Fixed64 enterCost = default,
        TraversalCapability requiredCapabilities = TraversalCapability.None) => new(
        media,
        requiredCapabilities,
        default,
        enterCost,
        (Fixed64)4,
        (Fixed64)4);

    private static GridConfiguration TransitionConfiguration() => new(
        Vector3d.Zero,
        new Vector3d(8, 2, 4),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)4),
        storageKind: GridStorageKind.Sparse);

    private static NavigationPublishedTransition GetPublishedDefinition(
        NavigationWorldGraph graph,
        VoxelIndex sourceIndex,
        out NavigationMediumStateRef source)
    {
        graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out source)
            .Should().BeTrue();
        NavigationTransitionPage.Enumerator candidates =
            graph.EnumerateOutgoingTransitionCandidates(source);
        candidates.MoveNext().Should().BeTrue();
        NavigationPublishedTransition transition = candidates.Current;
        candidates.MoveNext().Should().BeFalse();
        return transition;
    }

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

    private static NavigationWorkBudget Budget(
        int maxTransitionCandidates = 64,
        int maxTransitionPairs = 64,
        int maxEvaluatedEdges = 64) => new(
        maxLookupProbes: 64,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges,
        maxConnectionLegs: 64,
        maxTransitionCandidates: maxTransitionCandidates,
        maxTransitionPairs,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 64,
        maxSimplificationRays: 0);

    private static NavigationWorkBudget VolumeBudget(
        int maxLookupProbes,
        int maxCoveredVoxelIntervals) => new(
        maxLookupProbes,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: 64,
        maxConnectionLegs: 64,
        maxTransitionCandidates: 64,
        maxTransitionPairs: 64,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals,
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
