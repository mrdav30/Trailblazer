using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class TraversalValueObjectTests : IDisposable
{
    public TraversalValueObjectTests()
    {
        TrailblazerWorldManager.Setup();
        TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParsedTraversalCell_ShouldOnlyExposeTransitionMedia_WhenMarked()
    {
        TraversalLegendEntry entry = new(
            NavigationChartCell.SolidGas,
            TraversalMedia.Solid | TraversalMedia.Gas);

        ParsedTraversalCell marked = new(entry, hasTransitionMarker: true);
        ParsedTraversalCell unmarked = new(entry, hasTransitionMarker: false);

        marked.CanGenerateTransition.Should().BeTrue();
        marked.TransitionMedia.Should().Be(TraversalMedia.Solid | TraversalMedia.Gas);
        unmarked.CanGenerateTransition.Should().BeFalse();
        unmarked.TransitionMedia.Should().Be(TraversalMedia.None);
    }

    [Fact]
    public void TraversalBuildResult_ShouldFallbackToChartNameAndEmptyTransitions()
    {
        NavigationChart chart = NavigationChart.From3D(
            "BuildResultChart",
            new bool[1, 1, 1] { { { true } } },
            Vector3d.Zero,
            Fixed64.One);

        TraversalBuildResult result = new(chart, generatedTransitions: null!, generatedTransitionIdPrefix: " ");

        result.Chart.Should().BeSameAs(chart);
        result.GeneratedTransitions.Should().BeEmpty();
        result.GeneratedTransitionIdPrefix.Should().Be(chart.Name);
    }

    [Fact]
    public void TraversalBuildResult_ShouldHonorExplicitGeneratedTransitionPrefix()
    {
        NavigationChart chart = NavigationChart.From3D(
            "BuildResultExplicitPrefix",
            new bool[1, 1, 1] { { { true } } },
            Vector3d.Zero,
            Fixed64.One);
        TraversalTransition[] transitions =
        new[]
        {
            new TraversalTransition(
                id: "build-transition",
                type: TraversalTransitionType.Jump,
                source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
                destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))
        };

        TraversalBuildResult result = new(chart, transitions, generatedTransitionIdPrefix: "explicit-prefix");

        result.GeneratedTransitions.Should().BeSameAs(transitions);
        result.GeneratedTransitionIdPrefix.Should().Be("explicit-prefix");
    }

    [Fact]
    public void TraversalBuildResult_ShouldThrowWhenChartIsNull()
    {
        Action act = () => new TraversalBuildResult(null!, Array.Empty<TraversalTransition>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("chart");
    }

    [Fact]
    public void TraversalLegend_ShouldCreateBuiltInEntries_AndNormalizeLookups()
    {
        TraversalLegend legend = TraversalLegend.CreateBuiltIn();

        legend.TryGetEntry("SG", out TraversalLegendEntry solidGas).Should().BeTrue();
        solidGas.ChartCell.Should().Be(NavigationChartCell.SolidGas);
        solidGas.TransitionMedia.Should().Be(TraversalMedia.Solid | TraversalMedia.Gas);

        legend.TryGetEntry(" SL ", out TraversalLegendEntry solidLiquid).Should().BeTrue();
        solidLiquid.ChartCell.Should().Be(NavigationChartCell.SolidLiquid);

        legend.TryGetEntry(".", out TraversalLegendEntry skipped).Should().BeTrue();
        skipped.ChartCell.Should().Be(NavigationChartCell.Empty);
        skipped.HasTransitionMedia.Should().BeFalse();
    }

    [Fact]
    public void TraversalLegend_ShouldRejectDuplicates_AndMarkerTokens()
    {
        TraversalLegend legend = new();

        legend.Register("  token  ", TraversalLegendEntry.Gas()).Should().BeTrue();
        legend.Register("token", TraversalLegendEntry.Liquid()).Should().BeFalse();
        legend.Register(null!, TraversalLegendEntry.SkipCell()).Should().BeTrue();
        legend.TryGetEntry(null!, out TraversalLegendEntry skipped).Should().BeTrue();
        skipped.ChartCell.Should().Be(NavigationChartCell.Empty);

        Action act = () => legend.Register("L!", TraversalLegendEntry.Liquid());
        act.Should().Throw<ArgumentException>().WithParameterName("token");
    }

    [Fact]
    public void HybridRoutePlan_ShouldFallbackToEmptyArrays_WhenConstructedWithNullArrays()
    {
        HybridRoutePlan plan = new(null!, null!, totalPathCost: 12);

        plan.Steps.Should().BeEmpty();
        plan.DirectedTransitions.Should().BeEmpty();
        plan.TotalPathCost.Should().Be(12);
    }

    [Fact]
    public void HybridRoutePlan_ShouldPreserveProvidedArrays()
    {
        HybridRouteStep[] steps = new[]
        {
            HybridRouteStep.Waypoint(new Vector3d(1, 0, 0), additionalCost: 3)
        };
        TraversalTransition[] transitions = new[]
        {
            new TraversalTransition(
                id: "hybrid-transition",
                type: TraversalTransitionType.Jump,
                source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
                destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))
        };

        HybridRoutePlan plan = new(steps, transitions, totalPathCost: 7);

        plan.Steps.Should().BeSameAs(steps);
        plan.DirectedTransitions.Should().BeSameAs(transitions);
        plan.TotalPathCost.Should().Be(7);
    }

    [Fact]
    public void TraversalTransition_ShouldStoreProperties_WhenConstructedWithValidId()
    {
        TraversalTransition transition = new(
            id: "transition-id",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 5,
            isBidirectional: true,
            requestsClimbIntent: true,
            preserveClimbIntentOnFollowup: true);

        transition.Id.Should().Be("transition-id");
        transition.Type.Should().Be(TraversalTransitionType.Landing);
        transition.Source.Medium.Should().Be(TraversalMedium.Gas);
        transition.Destination.Medium.Should().Be(TraversalMedium.Solid);
        transition.PathCostModifier.Should().Be(5);
        transition.IsBidirectional.Should().BeTrue();
        transition.RequestsClimbIntent.Should().BeTrue();
        transition.PreserveClimbIntentOnFollowup.Should().BeTrue();
    }

    [Fact]
    public void TraversalTransition_ShouldRejectNullOrWhitespaceIds()
    {
        AssertRejectsInvalidTransitionId(null);
        AssertRejectsInvalidTransitionId(string.Empty);
        AssertRejectsInvalidTransitionId("   ");
    }

    private static void AssertRejectsInvalidTransitionId(string? id)
    {
        Action act = () => new TraversalTransition(
            id: id!,
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }
}
