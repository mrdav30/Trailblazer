using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class MultiWorldPhase0AcceptanceTests : IDisposable
{
    private const string Phase0SkipReason =
        "Phase 0 red acceptance test. Unskip in the owning implementation phase after context-local state exists.";

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void RegisteringSameChartNameInSeparateWorlds_ShouldNotCollide()
    {
        using GridWorld worldA = CreateWorld();
        using GridWorld worldB = CreateWorld();
        NavigationChart chartA = BuildLineChart("SharedChartName");
        NavigationChart chartB = BuildLineChart("SharedChartName");

        PathManager.Register(worldA, chartA).Should().BeTrue();
        PathManager.Register(worldB, chartB).Should().BeTrue();
    }

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void GuideCaches_WithEquivalentCoordinates_ShouldStayWorldLocal()
    {
        using GridWorld worldA = CreateWorld();
        using GridWorld worldB = CreateWorld();

        PathManager.Register(worldA, BuildLineChart("WorldAEquivalentRoute")).Should().BeTrue();
        AStarPathRequest requestA = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        AStarGuide guideA = TestRequire.Created(
            PathGuideFactory.RequestGuide(requestA, out AStarGuide? createdGuideA),
            createdGuideA);
        PathGuideFactory.ReturnGuide(guideA);

        PathManager.Register(worldB, BuildLineChart("WorldBEquivalentRoute")).Should().BeTrue();
        AStarPathRequest requestB = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        AStarGuide guideB = TestRequire.Created(
            PathGuideFactory.RequestGuide(requestB, out AStarGuide? createdGuideB),
            createdGuideB);
        PathGuideFactory.ReturnGuide(guideB);

        PathGuideFactory.TotalAStarGuideCount.Should().Be(
            2,
            "equivalent requests in different worlds must not reuse a process-wide cached guide");

        Type? guideServiceType = typeof(TrailblazerManager).Assembly.GetType("Trailblazer.Pathing.TrailblazerGuideService");
        guideServiceType.Should().NotBeNull(
            "Phase 4 moves reusable guide caches behind the owning TrailblazerWorldContext");
    }

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void GridWorldReset_ShouldClearOnlyTheOwningContextPathingState()
    {
        using GridWorld worldA = CreateWorld();
        using GridWorld worldB = CreateWorld();
        NavigationChart chartA = BuildLineChart("WorldAResetChart");
        NavigationChart chartB = BuildLineChart("WorldBResetChart");

        PathManager.Register(worldA, chartA).Should().BeTrue();
        PathManager.Register(worldB, chartB).Should().BeTrue();

        worldA.Reset();

        chartA.IsInitialized.Should().BeFalse();
        chartB.IsInitialized.Should().BeTrue();
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

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void MovementGroups_WithSameGroupId_ShouldStayContextLocal()
    {
        Assert.Fail(
            "Pending Phase 6: movement group state must be owned by TrailblazerWorldContext, not process-wide MovementGroupCoordinator state.");
    }

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void NavigatorReset_ShouldDeregisterFromItsOwnContextWorld()
    {
        Assert.Fail(
            "Pending Phase 6: Navigator must store its owning TrailblazerWorldContext and deregister through that world during reset.");
    }

    private static GridWorld CreateWorld()
    {
        var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        return world;
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
}
