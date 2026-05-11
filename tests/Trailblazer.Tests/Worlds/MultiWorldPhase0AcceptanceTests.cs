using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
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

    [Fact(Skip = Phase0SkipReason)]
    [Trait("Category", "MultiWorldPhase0Red")]
    public void GuideCaches_WithEquivalentCoordinates_ShouldStayWorldLocal()
    {
        Assert.Fail(
            "Pending Phase 4: guide caches and path requests must be owned by TrailblazerWorldContext before equivalent requests can stay world-local.");
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
}
