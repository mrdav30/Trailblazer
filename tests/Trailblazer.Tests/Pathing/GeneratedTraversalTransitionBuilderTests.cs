using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class GeneratedTraversalTransitionBuilderTests : IDisposable
{
    public GeneratedTraversalTransitionBuilderTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BuildTransitionsForPair_ShouldReturnEmpty_ForOutOfBoundsAndMissingGeneratedMedia()
    {
        NavigationChart chart = CreateChart(
            new NavigationChartCell[1, 2, 1]
            {
                { { NavigationChartCell.Solid }, { NavigationChartCell.Gas } }
            });

        GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
                chart,
                "oob",
                firstX: -1,
                firstY: 0,
                firstZ: 0,
                secondX: 0,
                secondY: 0,
                secondZ: 0)
            .Should()
            .BeEmpty();

        GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
                chart,
                "nogenerated",
                firstX: 0,
                firstY: 0,
                firstZ: 0,
                secondX: 1,
                secondY: 0,
                secondZ: 0)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildTransitionsForPair_ShouldReturnEmpty_WhenBoundaryCandidateIsAmbiguous()
    {
        NavigationChart chart = CreateChart(
            new NavigationChartCell[1, 2, 1]
            {
                {
                    { new NavigationChartCell(TraversalMedia.Solid | TraversalMedia.Gas, generatedTransitionMedia: TraversalMedia.Solid | TraversalMedia.Gas) },
                    { new NavigationChartCell(TraversalMedia.Solid | TraversalMedia.Gas, generatedTransitionMedia: TraversalMedia.Solid | TraversalMedia.Gas) }
                }
            });

        GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
                chart,
                "ambiguous",
                firstX: 0,
                firstY: 0,
                firstZ: 0,
                secondX: 1,
                secondY: 0,
                secondZ: 0)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildTransitionsForPair_ShouldCreateGasTransitionPairs_AndMatchPotentialIds()
    {
        NavigationChart chart = CreateChart(
            new NavigationChartCell[1, 2, 1]
            {
                {
                    { new NavigationChartCell(TraversalMedia.Solid, generatedTransitionMedia: TraversalMedia.Solid) },
                    { new NavigationChartCell(TraversalMedia.Gas, generatedTransitionMedia: TraversalMedia.Gas) }
                }
            });

        TraversalTransition[] transitions = GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
            chart,
            "gas",
            firstX: 0,
            firstY: 0,
            firstZ: 0,
            secondX: 1,
            secondY: 0,
            secondZ: 0);

        transitions.Should().HaveCount(2);
        transitions[0].Type.Should().Be(TraversalTransitionType.Takeoff);
        transitions[0].Source.Medium.Should().Be(TraversalMedium.Solid);
        transitions[0].Destination.Medium.Should().Be(TraversalMedium.Gas);
        transitions[1].Type.Should().Be(TraversalTransitionType.Landing);
        transitions[1].Source.Medium.Should().Be(TraversalMedium.Gas);
        transitions[1].Destination.Medium.Should().Be(TraversalMedium.Solid);

        string[] potentialIds = GeneratedTraversalTransitionBuilder.GetPotentialTransitionIdsForPair(
            "gas",
            firstX: 0,
            firstY: 0,
            firstZ: 0,
            secondX: 1,
            secondY: 0,
            secondZ: 0);

        potentialIds.Should().Contain(transitions[0].Id);
        potentialIds.Should().Contain(transitions[1].Id);
    }

    [Fact]
    public void BuildTransitionsForPair_ShouldCreateLiquidTransitionPairs()
    {
        NavigationChart chart = CreateChart(
            new NavigationChartCell[1, 2, 1]
            {
                {
                    { new NavigationChartCell(TraversalMedia.Solid, generatedTransitionMedia: TraversalMedia.Solid) },
                    { new NavigationChartCell(TraversalMedia.Liquid, generatedTransitionMedia: TraversalMedia.Liquid) }
                }
            });

        TraversalTransition[] transitions = GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
            chart,
            "liquid",
            firstX: 0,
            firstY: 0,
            firstZ: 0,
            secondX: 1,
            secondY: 0,
            secondZ: 0);

        transitions.Should().HaveCount(2);
        transitions[0].Type.Should().Be(TraversalTransitionType.SwimEntry);
        transitions[0].Source.Medium.Should().Be(TraversalMedium.Solid);
        transitions[0].Destination.Medium.Should().Be(TraversalMedium.Liquid);
        transitions[1].Type.Should().Be(TraversalTransitionType.SwimExit);
        transitions[1].Source.Medium.Should().Be(TraversalMedium.Liquid);
        transitions[1].Destination.Medium.Should().Be(TraversalMedium.Solid);
    }

    private static NavigationChart CreateChart(NavigationChartCell[,,] data)
    {
        return NavigationChart.From3D(
            "GeneratedTransitions",
            data,
            Vector3d.Zero,
            Fixed64.One);
    }
}
