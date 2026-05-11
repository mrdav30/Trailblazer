using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class PathingWorldStateIsolationTests : IDisposable
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
    public void ContextPathing_ShouldRegisterSameChartNameInSeparateWorlds()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        NavigationChart chartA = BuildSinglePointChart("SharedChartName", NavigationChartCell.Solid);
        NavigationChart chartB = BuildSinglePointChart("SharedChartName", NavigationChartCell.Liquid);

        contextA.Pathing.Register(chartA).Should().BeTrue();
        contextB.Pathing.Register(chartB).Should().BeTrue();

        contextA.Pathing.IsChartInitialized(chartA).Should().BeTrue();
        contextB.Pathing.IsChartInitialized(chartB).Should().BeTrue();
        contextA.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cellA).Should().BeTrue();
        contextB.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cellB).Should().BeTrue();
        cellA.HasSolid.Should().BeTrue();
        cellB.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
    }

    [Fact]
    public void ContextPathing_ShouldKeepResolvedVoxelStateWorldLocalForEquivalentVoxelIndexes()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Pathing.Register(BuildSinglePointChart("WorldAOwner", NavigationChartCell.Solid)).Should().BeTrue();
        contextB.Pathing.Register(BuildSinglePointChart("WorldBOwner", NavigationChartCell.Liquid)).Should().BeTrue();

        contextA.Pathing.TryGetEffectiveChartOwner(Vector3d.Zero, out string? ownerA).Should().BeTrue();
        contextB.Pathing.TryGetEffectiveChartOwner(Vector3d.Zero, out string? ownerB).Should().BeTrue();

        ownerA.Should().Be("WorldAOwner");
        ownerB.Should().Be("WorldBOwner");
    }

    [Fact]
    public void ContextPathing_UnloadChart_ShouldAffectOnlyOwningContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Pathing.Register(BuildSinglePointChart("SharedChartName", NavigationChartCell.Solid)).Should().BeTrue();
        contextB.Pathing.Register(BuildSinglePointChart("SharedChartName", NavigationChartCell.Liquid)).Should().BeTrue();

        contextA.Pathing.UnloadChart("SharedChartName");

        contextA.Pathing.IsChartRegistered("SharedChartName").Should().BeFalse();
        contextA.Pathing.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
        contextB.Pathing.IsChartInitialized("SharedChartName").Should().BeTrue();
        contextB.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cellB).Should().BeTrue();
        cellB.SupportsMedium(TraversalMedium.Liquid).Should().BeTrue();
    }

    [Fact]
    public void ContextPathing_GridReset_ShouldClearOnlyOwningContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Pathing.Register(BuildSinglePointChart("WorldAResetChart", NavigationChartCell.Solid)).Should().BeTrue();
        contextB.Pathing.Register(BuildSinglePointChart("WorldBResetChart", NavigationChartCell.Solid)).Should().BeTrue();

        contextA.World.Reset();

        contextA.Pathing.IsChartRegistered("WorldAResetChart").Should().BeFalse();
        contextA.Pathing.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
        contextB.Pathing.IsChartInitialized("WorldBResetChart").Should().BeTrue();
        contextB.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cellB).Should().BeTrue();
        cellB.HasSolid.Should().BeTrue();
    }

    [Fact]
    public void ContextPathing_GridChangeRebuild_ShouldOperateOnlyOnOwningContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        NavigationChart chartA = BuildSinglePointChart("WorldAChangedGridChart", NavigationChartCell.Solid);
        NavigationChart chartB = BuildSinglePointChart("WorldBChangedGridChart", NavigationChartCell.Solid);

        contextA.Pathing.Register(chartA).Should().BeTrue();
        contextB.Pathing.Register(chartB).Should().BeTrue();

        contextA.Pathing.HandleGridChanged(new GridEventInfo(
            contextA.World.SpawnToken,
            0,
            101,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            2));
        contextA.Pathing.FlushPendingGridChanges();

        contextA.Pathing.GetExternalGridBridgeDiagnosticsSnapshot().RebuildPassesExecuted.Should().Be(1);
        contextB.Pathing.GetExternalGridBridgeDiagnosticsSnapshot().RebuildPassesExecuted.Should().Be(0);
        contextA.Pathing.IsChartInitialized(chartA).Should().BeTrue();
        contextB.Pathing.IsChartInitialized(chartB).Should().BeTrue();
    }

    [Fact]
    public void ContextPathing_ChartMutation_ShouldAffectOnlyOwningContext()
    {
        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();

        contextA.Pathing.Register(BuildSinglePointChart("SharedMutableChart", NavigationChartCell.Solid)).Should().BeTrue();
        contextB.Pathing.Register(BuildSinglePointChart("SharedMutableChart", NavigationChartCell.Solid)).Should().BeTrue();

        contextA.Pathing.TryUpdateChartCell("SharedMutableChart", Vector3d.Zero, NavigationChartCell.Empty)
            .Should().BeTrue();

        contextA.Pathing.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
        contextB.Pathing.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cellB).Should().BeTrue();
        cellB.HasSolid.Should().BeTrue();
    }

    [Fact]
    public void StaticWorldRegister_ShouldRejectDirectMultiWorldRegistration()
    {
        using GridWorld world = CreateWorldWithGrid();
        NavigationChart chart = BuildSinglePointChart("DirectWorldRegister", NavigationChartCell.Solid);

        Action act = () => PathManager.Register(world, chart);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*context.Pathing.Register*");
    }

    private static TrailblazerWorldContext CreateContextWithGrid()
    {
        return TrailblazerWorldContext.Attach(CreateWorldWithGrid(), takeOwnership: true);
    }

    private static GridWorld CreateWorldWithGrid()
    {
        var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        return world;
    }

    private static NavigationChart BuildSinglePointChart(string name, NavigationChartCell cell)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1]
        {
            {
                { cell }
            }
        };

        return NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
    }
}
