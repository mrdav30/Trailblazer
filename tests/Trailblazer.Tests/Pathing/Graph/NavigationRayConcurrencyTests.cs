using System.Reflection;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationRayConcurrencyTests
{
    [Theory]
    [InlineData(0, (int)NavigationRayStatus.Success)]
    [InlineData(1, (int)NavigationRayStatus.Stale)]
    [InlineData(2, (int)NavigationRayStatus.Stale)]
    public void OrderedRay_ShouldLinearizeAfterGeometryAndBeforePublication(
        int mutationKind,
        int expectedStatus)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: 1_024,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 1_024,
            maxConnectionLegs: 1_024,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 16,
            maxCoveredVoxelIntervals: 16,
            maxSimplificationRays: 0));
        work.Begin(request);
        InvokePhase(work, "Trace", meter).Should().Be(NavigationRayStatus.Pending);
        InvokePhase(work, "MapIntervals", meter).Should().Be(NavigationRayStatus.Pending);

        if (mutationKind == 0)
        {
            fixture.Store.TryPublish(
                    fixture.Graph.WithGraphVersion(fixture.Graph.GraphVersion + 1))
                .Should().Be(NavigationCandidatePublication.Published);
        }
        else if (mutationKind == 1)
        {
            var revisedPolicy = new NavigationAreaPolicy(
                new NavigationAreaPolicyKey(
                    NavigationAStarExitTestHarness.Policy.Key.PolicyId,
                    NavigationAStarExitTestHarness.Policy.Key.Revision + 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) });
            NavigationAreaCatalog.Empty.TryPublish(
                    revisedPolicy,
                    1,
                    1,
                    1,
                    1,
                    out NavigationAreaCatalog revisedCatalog)
                .Should().Be(NavigationOperationRejection.None);
            fixture.Store.TryPublish(fixture.Graph.WithAreaCatalog(
                    revisedCatalog,
                    fixture.Graph.GraphVersion + 1))
                .Should().Be(NavigationCandidatePublication.Published);
        }
        else
        {
            fixture.Graph.TryGetMap(
                    fixture.FarOrigin.MapId,
                    out NavigationMapInstance? instance)
                .Should().BeTrue();
            instance.Should().NotBeNull();
            fixture.World.ActiveGrids[instance!.GridIdentity.GridIndex]
                .TryRemoveVoxel(new VoxelIndex(2, 0, 0))
                .Should().BeTrue();
        }

        InvokePhase(work, "EvaluateChain", meter)
            .Should().Be((NavigationRayStatus)expectedStatus);
    }

    private static NavigationRayRequest CreateRequest(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef startNode)
            .Should().BeTrue();
        NavigationCellAddress endAddress = fixture.Near.Nodes[0].Address;
        fixture.Graph.TryGetNodeRef(endAddress, out NavigationNodeRef endNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(startNode, out NavigationNodeState startState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(endNode, out NavigationNodeState endState)
            .Should().BeTrue();
        return new NavigationRayRequest(
            fixture.World,
            fixture.Store,
            fixture.Graph,
            NavigationAStarExitTestHarness.Profile(),
            NavigationAStarExitTestHarness.Policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            allowTransitions: false,
            startState.FootAnchor,
            endState.FootAnchor,
            NavigationRayEndpointAllowance.None);
    }

    private static NavigationRayStatus InvokePhase(
        NavigationRayWork work,
        string methodName,
        NavigationWorkMeter meter)
    {
        MethodInfo method = typeof(NavigationRayWork).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Should().NotBeNull();
        object?[] arguments = { meter, default(GuideSampleWorkMeter), false };
        return (NavigationRayStatus)method.Invoke(work, arguments)!;
    }
}
