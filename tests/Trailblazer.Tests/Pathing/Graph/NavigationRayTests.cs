using FixedMathSharp;
using FluentAssertions;
using System.Linq;
using System.Reflection;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationRayTests
{
    [Fact]
    public void WorkMeter_ShouldDebitEachRayCategoryExactly()
    {
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: 0,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 2,
            maxCoveredVoxelIntervals: 3,
            maxSimplificationRays: 1));

        meter.TryConsumeTraceIntervals(2).Should().BeTrue();
        meter.TryConsumeTraceIntervals(1).Should().BeFalse();
        meter.TraceIntervals.Should().Be(2);
        meter.TryConsumeCoveredVoxelIntervals(3).Should().BeTrue();
        meter.TryConsumeCoveredVoxelIntervals(1).Should().BeFalse();
        meter.CoveredVoxelIntervals.Should().Be(3);
        meter.TryConsumeSimplificationRays(1).Should().BeTrue();
        meter.TryConsumeSimplificationRays(1).Should().BeFalse();
        meter.SimplificationRays.Should().Be(1);

        meter.Reset(new NavigationWorkBudget(0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1));
        meter.TraceIntervals.Should().Be(0);
        meter.CoveredVoxelIntervals.Should().Be(0);
        meter.SimplificationRays.Should().Be(0);
    }

    [Fact]
    public void Workspace_ShouldDeriveEveryBufferFromExplicitCeilings()
    {
        var workspace = new NavigationRayWorkspace(
            mapCapacity: 2,
            pageCapacity: 3,
            componentCapacity: 5,
            coveredAddressCapacity: 13,
            traceIntervalCapacity: 11);

        workspace.TraceIntervals.Capacity.Should().BeGreaterThanOrEqualTo(11);
        workspace.TraceIntervalCapacity.Should().Be(11);
        workspace.IntervalAddresses.Should().HaveCount(11);
        workspace.PredecessorOrdinals.Should().HaveCount(11);
        workspace.EdgeOrdinals.Should().HaveCount(11);
        workspace.Dependencies.Pages.Should().HaveCount(3);
        workspace.Dependencies.Components.Should().HaveCount(5);
        workspace.CoveredAddressCapacity.Should().Be(13);
        workspace.MapCapacity.Should().Be(2);
    }

    [Fact]
    public void Context_ShouldOwnOneImmediateRayWorkspace()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

        context.Pathing.ImmediateRayWorkspace.SyncRoot.Should().NotBeNull();
        context.Pathing.ImmediateRayWorkspace.Workspace.Should().NotBeNull();
    }

    [Fact]
    public void WorkspaceContracts_ShouldNotRetainCompatibilityOrForwardingSurface()
    {
        ConstructorInfo aStarConstructor = typeof(NavigationAStarWorkspace)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        ConstructorInfo flowConstructor = typeof(NavigationFlowFieldWorkspace)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

        aStarConstructor.GetParameters().Skip(4)
            .Should().OnlyContain(parameter => !parameter.HasDefaultValue);
        flowConstructor.GetParameters().Skip(4)
            .Should().OnlyContain(parameter => !parameter.HasDefaultValue);
        typeof(TrailblazerGuideService).GetProperty(
            "ImmediateRayWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "GenerationStamps",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
    }

    [Fact]
    public void RayResult_ShouldRetainOnlyConsumerFacts()
    {
        NavigationCellAddress start = default;
        NavigationCellAddress end = default;
        var result = new NavigationRayResult(
            NavigationRayStatus.Success,
            start,
            end,
            Fixed64.One,
            isSemanticCostNeutral: true);

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(start);
        result.EndAddress.Should().Be(end);
        result.TraversalCost.Should().Be(Fixed64.One);
        result.IsSemanticCostNeutral.Should().BeTrue();
    }
}
