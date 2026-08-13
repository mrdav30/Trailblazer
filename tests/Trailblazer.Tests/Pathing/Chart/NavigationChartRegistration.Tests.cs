using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class NavigationChartRegistrationTests : IDisposable
{
    public NavigationChartRegistrationTests()
    {
        TestWorld.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NavigationChart_ShouldNotStoreLiveRegistrationState()
    {
        typeof(NavigationChart).GetProperty("IsInitialized").Should().BeNull();
        typeof(NavigationChart).GetProperty("RegistrationOrder").Should().BeNull();
    }

    [Fact]
    public void NavigationChartRegistration_ShouldAllowOneAuthoredChartToHaveIndependentLiveState()
    {
        NavigationChart chart = BuildSinglePointChart("ReusableAuthoredChart");
        var first = new NavigationChartRegistration(chart, registrationOrder: 1, generatedTransitionIdPrefix: "first");
        var second = new NavigationChartRegistration(chart, registrationOrder: 2, generatedTransitionIdPrefix: "second");

        first.IsInitialized = true;
        first.TransitionIds.Add("first-generated");

        second.IsInitialized.Should().BeFalse();
        second.TransitionIds.Should().BeEmpty();
        first.Chart.Should().BeSameAs(chart);
        second.Chart.Should().BeSameAs(chart);
        first.RegistrationOrder.Should().Be(1);
        second.RegistrationOrder.Should().Be(2);
    }

    [Fact]
    public void PathManager_ShouldExposeInitializationStateThroughRegistration()
    {
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();

        NavigationChart chart = BuildSinglePointChart("RegistrationStateChart");

        PathManager.Register(chart, initializeChart: false).Should().BeTrue();
        PathManager.TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration)
            .Should().BeTrue();
        registration.Chart.Should().BeSameAs(chart);
        registration.IsInitialized.Should().BeFalse();
        PathManager.IsChartInitialized(chart.Name).Should().BeFalse();

        PathManager.InitializeChart(chart.Name);

        registration.IsInitialized.Should().BeTrue();
        PathManager.IsChartInitialized(chart.Name).Should().BeTrue();
    }

    [Fact]
    public void SamePriorityOverlap_ShouldUseRegistrationOrderFromLiveRegistration()
    {
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();

        NavigationChart first = BuildSinglePointChart("FirstOverlapChart");
        NavigationChart second = BuildSinglePointChart("SecondOverlapChart");

        PathManager.Register(first).Should().BeTrue();
        PathManager.Register(second).Should().BeTrue();

        PathManager.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeTrue();
        PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out string? owner).Should().BeTrue();
        owner.Should().Be(second.Name);

        PathManager.TryGetNavigationChartRegistration(first.Name, out NavigationChartRegistration firstRegistration)
            .Should().BeTrue();
        PathManager.TryGetNavigationChartRegistration(second.Name, out NavigationChartRegistration secondRegistration)
            .Should().BeTrue();
        firstRegistration.RegistrationOrder.Should().BeLessThan(secondRegistration.RegistrationOrder);
    }

    private static NavigationChart BuildSinglePointChart(string name)
    {
        bool[,,] data = new bool[1, 1, 1]
        {
            {
                { true }
            }
        };

        return NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
    }
}
