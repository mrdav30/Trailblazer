using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class ContextOwnedPathingServiceCoverageTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PathingService_ShouldDelegateChartLifecycleQueriesAndMutations()
    {
        using TrailblazerWorldContext context = PathTestFactory.CreateContextWithGrid();
        NavigationChart chart = PathTestFactory.BuildSinglePointMap("ContextPathingDelegates", Vector3d.Zero);

        context.Pathing.AllCharts.Should().BeEmpty();

        context.Pathing.Register(chart, initializeChart: false).Should().BeTrue();

        context.Pathing.AllCharts.Should().ContainSingle(registered => registered.Name == chart.Name);
        context.Pathing.IsChartRegistered(chart.Name).Should().BeTrue();
        context.Pathing.TryGetNavigationChart(chart.Name, out NavigationChart resolvedChart).Should().BeTrue();
        resolvedChart.Should().BeSameAs(chart);
        context.Pathing.TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration).Should().BeTrue();
        registration.Chart.Should().BeSameAs(chart);
        context.Pathing.IsChartInitialized(chart.Name).Should().BeFalse();
        context.Pathing.IsChartInitialized(chart).Should().BeFalse();

        context.Pathing.InitializeChart(chart.Name);
        context.Pathing.InitializeAllCharts();

        context.Pathing.IsChartInitialized(chart.Name).Should().BeTrue();
        context.Pathing.IsChartInitialized(chart).Should().BeTrue();

        Voxel voxel = PathTestFactory.RequireVoxel(context, Vector3d.Zero);
        context.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell worldCell).Should().BeTrue();
        worldCell.HasSolid.Should().BeTrue();
        context.Pathing.TryGetEffectiveCell(voxel.WorldIndex, out NavigationChartCell indexedCell).Should().BeTrue();
        indexedCell.HasSolid.Should().BeTrue();
        context.Pathing.TryGetEffectiveChartOwner(Vector3d.Zero, out string? worldOwner).Should().BeTrue();
        worldOwner.Should().Be(chart.Name);
        context.Pathing.TryGetEffectiveChartOwner(voxel.WorldIndex, out string? indexedOwner).Should().BeTrue();
        indexedOwner.Should().Be(chart.Name);

        context.Pathing.TryUpdateChartCell(chart.Name, 1, 1, 1, NavigationChartCell.Empty).Should().BeTrue();
        context.Pathing.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
        context.Pathing.TryUpdateChartCell(chart.Name, Vector3d.Zero, NavigationChartCell.Solid).Should().BeTrue();

        var updates = new[] { new NavigationChartCellUpdate(1, 1, 1, NavigationChartCell.Empty) };
        context.Pathing.ApplyChartUpdates(chart.Name, updates).Should().Be(1);
        context.Pathing.TryGetEffectiveChartOwner(Vector3d.Zero, out _).Should().BeFalse();

        context.Pathing.Reset();

        context.Pathing.IsChartRegistered(chart.Name).Should().BeFalse();
        context.Pathing.AllCharts.Should().BeEmpty();
    }

    [Fact]
    public void PathingService_ShouldUnloadDeferredAndInitializedChartsByOverloads()
    {
        using TrailblazerWorldContext context = PathTestFactory.CreateContextWithGrid();
        NavigationChart deferred = PathTestFactory.BuildSinglePointMap("ContextPathingDeferredUnload", Vector3d.Zero);
        NavigationChart initialized = PathTestFactory.BuildSinglePointMap("ContextPathingInitializedUnload", new Vector3d(2, 0, 0));
        NavigationChart missing = PathTestFactory.BuildSinglePointMap("ContextPathingMissingUnload", new Vector3d(4, 0, 0));

        context.Pathing.Register(deferred, initializeChart: false).Should().BeTrue();
        context.Pathing.Register(initialized).Should().BeTrue();

        context.Pathing.UnloadChart(missing);
        context.Pathing.IsChartRegistered(deferred.Name).Should().BeTrue();

        context.Pathing.UnloadChart(deferred);
        context.Pathing.IsChartRegistered(deferred.Name).Should().BeFalse();

        context.Pathing.UnloadChart(initialized.Name);
        context.Pathing.IsChartRegistered(initialized.Name).Should().BeFalse();
    }

    [Fact]
    public void NavigationService_ShouldExposeContextAndBindNavigators()
    {
        using TrailblazerWorldContext context = PathTestFactory.CreateContextWithGrid();
        var navigator = new TestNavigator();

        context.Navigation.Context.Should().BeSameAs(context);
        context.Navigation.Invoking(service => service.Bind(null!))
            .Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("navigator");

        context.Navigation.Bind(navigator);
        navigator.Context.Should().BeSameAs(context);
    }
}
