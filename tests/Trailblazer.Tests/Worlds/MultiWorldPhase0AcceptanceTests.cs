using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class MultiWorldPhase0AcceptanceTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RegisteringSameChartNameInSeparateWorlds_ShouldNotCollide()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        NavigationChart chartA = BuildLineChart("SharedChartName");
        NavigationChart chartB = BuildLineChart("SharedChartName");

        contextA.Pathing.Register(chartA).Should().BeTrue();
        contextB.Pathing.Register(chartB).Should().BeTrue();
    }

    [Fact]
    public void GuideCaches_WithEquivalentCoordinates_ShouldStayWorldLocal()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        NavigationChart chartA = BuildLineChart("SharedGuideChart");
        NavigationChart chartB = BuildLineChart("SharedGuideChart");
        contextA.Pathing.Register(chartA).Should().BeTrue();
        contextB.Pathing.Register(chartB).Should().BeTrue();

        AStarPathRequest requestA = CreateAStarRequest(contextA, Vector3d.Zero, new Vector3d(2, 0, 0));
        AStarPathRequest requestB = CreateAStarRequest(contextB, Vector3d.Zero, new Vector3d(2, 0, 0));

        AStarGuide guideA = TestRequire.Created(contextA.Guides.RequestGuide(requestA, out AStarGuide? createdGuideA), createdGuideA);
        contextA.Guides.ReturnGuide(guideA);

        AStarGuide guideB = TestRequire.Created(contextB.Guides.RequestGuide(requestB, out AStarGuide? createdGuideB), createdGuideB);
        contextB.Guides.ReturnGuide(guideB);

        contextA.Guides.InvalidateCacheFor("SharedGuideChart");

        contextA.Guides.TotalAStarGuideCount.Should().Be(0);
        contextB.Guides.TotalAStarGuideCount.Should().Be(1);
    }

    [Fact]
    public void GridWorldReset_ShouldClearOnlyTheOwningContextPathingState()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        NavigationChart chartA = BuildLineChart("WorldAResetChart");
        NavigationChart chartB = BuildLineChart("WorldBResetChart");

        contextA.Pathing.Register(chartA).Should().BeTrue();
        contextB.Pathing.Register(chartB).Should().BeTrue();

        contextA.World.Reset();

        contextA.Pathing.IsChartInitialized(chartA).Should().BeFalse();
        contextB.Pathing.IsChartInitialized(chartB).Should().BeTrue();
    }

    [Fact]
    public void TrailblazerWorldContext_FrameCount_ShouldAdvanceIndependentlyPerWorld()
    {
        using TrailblazerWorldContext contextA = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext contextB = TrailblazerWorldContext.CreateOwned();

        contextA.Simulate();
        contextA.Simulate();
        contextB.Simulate();

        contextA.FrameCount.Should().Be(2);
        contextB.FrameCount.Should().Be(1);
    }

    [Fact]
    public void MovementGroups_WithSameGroupId_ShouldStayContextLocal()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        var contextASession = new MovementGroupSession { GroupId = 4 };
        var contextBSession = new MovementGroupSession { GroupId = 4 };
        var contextBProbe = new MovementGroupSession { GroupId = 4 };
        Guid contextAOwner = Guid.NewGuid();
        Guid contextBOwner = Guid.NewGuid();
        Vector3d destination = new(3, 0, 0);

        contextA.Navigation.MovementGroups.Prewarm(contextASession, contextAOwner, destination, Vector3d.Zero, Fixed64.One);
        contextB.Navigation.MovementGroups.Prewarm(contextBSession, contextBOwner, destination, Vector3d.Zero, Fixed64.One);

        contextB.Navigation.MovementGroups.IsNeighbor(contextBProbe, contextAOwner, destination, contextB.FrameCount)
            .Should()
            .BeFalse();
        contextB.Navigation.MovementGroups.IsNeighbor(contextBProbe, contextBOwner, destination, contextB.FrameCount)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void NavigatorReset_ShouldDeregisterFromItsOwnContextWorld()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        TrailblazerManager.Initialize(contextB.World);

        var navigator = new TestNavigator(contextA);
        navigator.Setup(Vector3d.Zero);
        navigator.Initialize(new TrekCondition());

        ScanSteerCount(contextA, Vector3d.Zero).Should().Be(1);
        ScanSteerCount(contextB, Vector3d.Zero).Should().Be(0);

        navigator.Reset();

        ScanSteerCount(contextA, Vector3d.Zero).Should().Be(0);
        ScanSteerCount(contextB, Vector3d.Zero).Should().Be(0);
    }

    private static TrailblazerWorldContext CreateContextWithGrid()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        return context;
    }

    private static NavigationChart BuildLineChart(string name)
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        return NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
    }

    private static AStarPathRequest CreateAStarRequest(
        TrailblazerWorldContext context,
        Vector3d source,
        Vector3d destination)
    {
        return TestRequire.NotNull(AStarPathRequest.Create(context, source, destination, Fixed64.One));
    }

    private static int ScanSteerCount(TrailblazerWorldContext context, Vector3d position)
    {
        var results = new SwiftList<ISteer>();
        var scratch = new GridScanScratch();
        GridScanManager.ScanRadiusInto<ISteer>(
            context.World,
            position,
            Fixed64.One,
            results,
            scratch);

        return results.Count;
    }
}
