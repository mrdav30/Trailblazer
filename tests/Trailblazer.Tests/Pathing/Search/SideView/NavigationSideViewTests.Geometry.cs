using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData("compatible", NavigationGuideStatus.Success)]
    [InlineData("lattice", NavigationGuideStatus.NoPath)]
    [InlineData("embedding", NavigationGuideStatus.NoPath)]
    public void LegUsesCompatiblePublishedSupportOwnersAcrossMaps(string kind, NavigationGuideStatus expected)
    {
        using var context = Scene(out _);
        var source = SideView("side", Grid);
        AddFloor(source, 4, -Fixed64.Half);
        Publish(context, source.Build());
        var metrics = kind == "lattice" ? GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.Two) : default;
        var neighbor = new GridConfiguration(new Vector3d(5, 0, 0), new Vector3d(5, 1, 0), topologyMetrics: metrics);
        context.World.TryAddGrid(neighbor, out _).Should().BeTrue();
        int layer = kind == "embedding" ? 0 : 1;
        var index = new VoxelIndex(0, layer, 0);
        var target = new NavigationMapBuilder("neighbor", neighbor).SetPlanarLayer(NavigationMode2D.SideView, layer, (Fixed64)layer)
            .AddCell(index, Solid).AddPlanarSupport(index,
                new((Fixed64)4 + Fixed64.Half, -Fixed64.Half), new((Fixed64)5 + Fixed64.Half, -Fixed64.Half));
        Publish(context, target.Build(), 3);
        var end = new NavigationCellAddress("neighbor", index);
        Leg(context, Cell(4), end, Profile()).Should().Be(expected);
        Leg(context, end, Cell(4), Profile()).Should().Be(expected);
        Leg(context, Cell(4), Cell(4), Profile()).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void NarrowLowerSupportCannotLiftThroughTheStepSide()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid).AddCell(Cell(0).Index, Solid)
            .AddPlanarSupport(Cell(0).Index, new(Fixed64.FromFraction(1, 8), -Fixed64.Half), new(Fixed64.Half, -Fixed64.Half));
        AddFloor(builder, 1, -Fixed64.Quarter);
        Publish(context, builder.Build());
        var profile = Profile(up: Fixed64.Quarter, down: Fixed64.Quarter);
        Leg(context, Cell(0), Cell(1), profile).Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, Cell(1), Cell(0), profile).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void AShallowAuthoredSlopeDoesNotBecomeAHorizontalStep()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var wide = new GridConfiguration(Vector3d.Zero, new Vector3d(8, 1, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)8, Fixed64.One, Fixed64.One));
        context.World.TryAddGrid(wide, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var from = new NavigationCellAddress("wide", new VoxelIndex(0, 1, 0));
        var to = new NavigationCellAddress("wide", new VoxelIndex(1, 1, 0));
        var builder = SideView("wide", wide).SetDefaultCell(Solid)
            .AddPlanarSupport(from.Index, new((Fixed64)(-4), -Fixed64.Half), new((Fixed64)4, -Fixed64.Half + Fixed64.FromRaw(2)))
            .AddPlanarSupport(to.Index, new((Fixed64)4, -Fixed64.Quarter), new((Fixed64)12, -Fixed64.Quarter));
        Publish(context, builder.Build());
        var profile = Profile(Fixed64.MinIncrement, Fixed64.Quarter, Fixed64.Quarter);
        Anchor(context, from, profile, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, to, profile, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, from, to, profile).Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, to, from, profile).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void AnchorRequiresARepresentableWitnessAndSolidSemanticClearance()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        builder.AddCell(Cell(1).Index, Solid).AddPlanarSupport(Cell(1).Index,
            new(Fixed64.One, Fixed64.Zero), new(Fixed64.One + Fixed64.MinIncrement, Fixed64.FromRaw(2)));
        builder.AddPlanarSupport(Cell(2).Index, new((Fixed64)2, -Fixed64.Half), new((Fixed64)2 + Fixed64.Quarter, -Fixed64.Half));
        builder.AddCell(Cell(3).Index, Air).AddPlanarSupport(Cell(3).Index,
            new((Fixed64)3, -Fixed64.Half), new((Fixed64)3 + Fixed64.Quarter, -Fixed64.Half));
        Publish(context, builder.Build());
        Anchor(context, Cell(0), Profile(Fixed64.Two), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, Cell(1), Profile(normal: Fixed64.Quarter), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, Cell(2), Profile(), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, Cell(3), Profile(), out _).Should().Be(NavigationGuideStatus.InvalidStart);
    }

    [Fact]
    public void HeightDiscontinuityCannotJoinDifferentRectangularRows()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid).SetDefaultCell(Solid)
            .AddPlanarSupport(Cell(0).Index, new(-Fixed64.Half, -Fixed64.Quarter), new(Fixed64.Zero, -Fixed64.Quarter))
            .AddPlanarSupport(Cell(0, 2).Index, new(Fixed64.Zero, Fixed64.Half + Fixed64.Quarter),
                new(Fixed64.Half, Fixed64.Half + Fixed64.Quarter));
        Publish(context, builder.Build());
        var profile = Profile(up: Fixed64.One, down: Fixed64.One);
        Anchor(context, Cell(0), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(0, 2), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, Cell(0), Cell(0, 2), profile).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void HeightDiscontinuityDoesNotUseTheRectangularStepRuleOnAHexGrid()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        var grid = new GridConfiguration(Vector3d.Zero, new Vector3d(12, 0, 6),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var from = new NavigationCellAddress("hex", new VoxelIndex(2, 0, 2));
        var to = new NavigationCellAddress("hex", new VoxelIndex(3, 0, 2));
        var builder = new NavigationMapBuilder("hex", grid).SetPlanarLayer(NavigationMode2D.SideView, 0, Fixed64.Zero)
            .SetDefaultCell(Solid);
        builder.GridBinding.TryGetCellPrism(from.Index, out var left).Should().BeTrue();
        builder.GridBinding.TryGetCellPrism(to.Index, out var right).Should().BeTrue();
        Fixed64 seam = left.Center.X + (right.Center.X - left.Center.X) * Fixed64.Half;
        Fixed64 height = right.Center.Z + Fixed64.Quarter;
        builder.AddPlanarSupport(from.Index, left.Center.ToVector2d(), new(seam, left.Center.Z))
            .AddPlanarSupport(to.Index, new(seam, height), new(right.Center.X, height));
        Publish(context, builder.Build());
        var profile = Profile(up: Fixed64.Quarter, down: Fixed64.Quarter);
        Leg(context, from, to, profile).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData("normal-x")]
    [InlineData("normal-y")]
    [InlineData("half-axis")]
    public void AnchorRejectsAnUnrepresentableCapsuleCenter(string component)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        bool slope = component == "normal-x";
        Fixed64 x = slope ? Fixed64.MinValue + Fixed64.One : Fixed64.Zero;
        Fixed64 z = slope ? Fixed64.Zero : Fixed64.MaxValue - Fixed64.Half;
        var point = new Vector3d(x, Fixed64.Zero, z);
        var grid = new GridConfiguration(point, point);
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var address = new NavigationCellAddress("limit", default);
        var builder = new NavigationMapBuilder("limit", grid).SetPlanarLayer(NavigationMode2D.SideView, 0, Fixed64.Zero)
            .AddCell(address.Index, Solid).AddPlanarSupport(address.Index,
                new(x - Fixed64.Half, slope ? -Fixed64.Half : z),
                new(slope ? x - Fixed64.Quarter : x + Fixed64.Half, slope ? -Fixed64.Quarter : z));
        Publish(context, builder.Build());
        var profile = component == "half-axis" ? Profile(Fixed64.One) : Profile(radius: Fixed64.One);
        Anchor(context, address, profile, out Vector2d center).Should().Be(NavigationGuideStatus.CostOverflow);
        center.Should().Be(Vector2d.Zero);
    }

    [Fact]
    public void SupportHullRejectsAnUnrepresentableTranslationOrCorner()
    {
        var support = new NavigationPlanarSupport(default, Vector2d.Zero,
            new Vector2d(Fixed64.MaxValue, Fixed64.Zero), Vector2d.Forward);
        support.CheckClearance(new Vector2d(Fixed64.MaxValue, Fixed64.Zero), -Vector2d.Right,
            Fixed64.Zero, Fixed64.Quarter).Should().Be(NavigationVolumeAnchorStatus.CostOverflow);
        support.CheckClearance(Vector2d.Right, Vector2d.Zero, Fixed64.Zero, Fixed64.Quarter)
            .Should().Be(NavigationVolumeAnchorStatus.CostOverflow);
    }
}
