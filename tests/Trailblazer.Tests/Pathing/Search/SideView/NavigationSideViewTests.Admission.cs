using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData("vertical")]
    [InlineData("reversed")]
    [InlineData("overflow")]
    [InlineData("spatial")]
    [InlineData("top-down")]
    [InlineData("layer")]
    [InlineData("index")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("duplicate")]
    public void BakeRejectsInvalidAuthoredSupport(string invalid)
    {
        var builder = invalid == "spatial" ? new NavigationMapBuilder("side", Grid)
            : invalid == "top-down" ? new NavigationMapBuilder("side", Grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One)
            : SideView("side", Grid);
        VoxelIndex index = invalid == "layer" ? new VoxelIndex(0, 0, 1)
            : invalid == "index" ? new VoxelIndex(20, 1, 1) : Cell(0).Index;
        Vector2d left = new(invalid == "left" ? -Fixed64.One : invalid == "overflow" ? Fixed64.MinValue : -Fixed64.Half,
            -Fixed64.Half);
        Vector2d right = new(invalid == "vertical" ? left.X : invalid == "reversed" ? -Fixed64.One
            : invalid == "right" ? Fixed64.One : invalid == "overflow" ? Fixed64.MaxValue : Fixed64.Half, -Fixed64.Half);
        Action build = () =>
        {
            builder.AddPlanarSupport(index, left, right);
            if (invalid == "duplicate") builder.AddPlanarSupport(index, left, right);
            builder.Build();
        };
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SupportOrderIsCanonicalAndGeometryChangesTheImmutableBake()
    {
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        NavigationMap original = builder.Build();
        var reordered = SideView("side", Grid);
        AddFloor(reordered, 1, -Fixed64.Half);
        AddFloor(reordered, 0, -Fixed64.Half);
        NavigationMap same = reordered.Build();
        original.Equals(same).Should().BeTrue();
        original.GetHashCode().Should().Be(same.GetHashCode());
        original.PlanarSupports[0].Equals((object)same.PlanarSupports[0]).Should().BeTrue();
        original.PlanarSupports[0].Equals((object)original.PlanarBinding!.Value).Should().BeFalse();
        original.PlanarSupports[0].Equals(original.PlanarSupports[1]).Should().BeFalse();

        var replacement = SideView("side", Grid);
        AddFloor(replacement, 0, -Fixed64.Half);
        AddFloor(replacement, 1, -Fixed64.Quarter);
        original.Equals(replacement.Build()).Should().BeFalse();
        AddFloor(builder, 2, -Fixed64.Half);
        original.Equals(same).Should().BeTrue();
        original.Equals(builder.Build()).Should().BeFalse();
        using var context = Scene(out _);
        Publish(context, original);
        Anchor(context, Cell(1), Profile(), out Vector2d before).Should().Be(NavigationGuideStatus.Success);
        Publish(context, replacement.Build(), 3, 2);
        Anchor(context, Cell(1), Profile(), out Vector2d after).Should().Be(NavigationGuideStatus.Success);
        after.Y.Should().Be(before.Y + Fixed64.Quarter);
    }

    [Fact]
    public void CoincidentTopDownLayerCannotSupplySideViewHeadroom()
    {
        using var context = Scene(out _);
        var foreign = new GridConfiguration(new Vector3d(-1, 1, -1), new Vector3d(4, 1, 1));
        context.World.TryAddGrid(foreign, out _).Should().BeTrue();
        Publish(context, new NavigationMapBuilder("top", foreign).SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.One)
            .SetDefaultCell(Solid).Build());
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        Publish(context, builder.Build(), 3);
        var tall = Profile(Fixed64.Half + Fixed64.Quarter);
        Anchor(context, Cell(0), tall, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, new NavigationCellAddress("top", default), tall, out _).Should().Be(NavigationGuideStatus.NoMap);
        Leg(context, Cell(0), new NavigationCellAddress("top", default), tall).Should().Be(NavigationGuideStatus.NoMap);
        Publish(context, builder.AddCell(Cell(0, 2).Index, Air).Build(), 4, 2);
        Anchor(context, Cell(0), tall, out _).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void BodyCoverageUsesCurrentHeadroomPermissionsAndSupportOwnerBlockers()
    {
        using var context = Scene(out ushort gridId);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        var guardedAir = new NavigationCell(TraversalMedia.Gas, TraversalCapability.Jump, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        Publish(context, builder.AddCell(Cell(0, 2).Index, guardedAir).Build());
        var tall = Profile(Fixed64.Half + Fixed64.Quarter);
        Anchor(context, Cell(0), tall, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        var jumping = Profile(tall.AxisLength, capabilities: TraversalCapability.Jump);
        Anchor(context, Cell(0), jumping, out _).Should().Be(NavigationGuideStatus.Success);

        var live = TestRequire.Grid(context, gridId);
        live.TryGetVoxel(Cell(0).Index, out var owner).Should().BeTrue();
        var obstacle = context.World.AllocateObstacleToken();
        live.TryAddObstacle(TestRequire.NotNull(owner), obstacle).Should().BeTrue();
        Anchor(context, Cell(0), jumping, out var stale).Should().Be(NavigationGuideStatus.Stale);
        stale.Should().Be(Vector2d.Zero);
        Advance(context);
        Anchor(context, Cell(0), jumping, out _).Should().Be(NavigationGuideStatus.InvalidStart);
        live.TryRemoveObstacle(TestRequire.NotNull(owner), obstacle).Should().BeTrue();
        Advance(context);
        Anchor(context, Cell(0), jumping, out _).Should().Be(NavigationGuideStatus.Success);

        var overlay = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(new NavigationOverlayTransaction(
            new[] { new NavigationMapOverlayDelta("side", new[] { NavigationCellOverlayOperation.Suppress(Cell(0).Index) }) })),
            3, context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        Advance(context);
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        Anchor(context, Cell(0), jumping, out _).Should().Be(NavigationGuideStatus.InvalidStart);
    }

    [Fact]
    public void AuthoredSupportCannotStandInForAPhysicallyAbsentCell()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var sparse = new GridConfiguration(Grid.BoundsMin, Grid.BoundsMax, storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(sparse, new[] { Cell(0).Index }, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var builder = SideView("side", sparse);
        AddFloor(builder, 0, -Fixed64.Half);
        AddFloor(builder, 1, -Fixed64.Half);
        Publish(context, builder.Build());
        Anchor(context, Cell(0), Profile(), out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, Cell(0), Cell(1), Profile()).Should().Be(NavigationGuideStatus.InvalidEnd);
    }

    [Fact]
    public void ProofRejectsMissingInputsAndRespectsSnapshotLeaseCapacity()
    {
        using var context = Scene(out _);
        var builder = SideView("side", Grid);
        AddFloor(builder, 0, -Fixed64.Half);
        Publish(context, builder.Build());
        Anchor(context, default, Profile(), out _).Should().Be(NavigationGuideStatus.InvalidStart);
        Anchor(context, Cell(0), default, out _).Should().Be(NavigationGuideStatus.InvalidProfile);
        Leg(context, Cell(0), default, Profile()).Should().Be(NavigationGuideStatus.InvalidEnd);
        Anchor(context, new NavigationCellAddress("missing", Cell(0).Index), Profile(), out _)
            .Should().Be(NavigationGuideStatus.NoMap);
        context.Pathing.ResolveSideViewAnchor(Cell(0), Profile(), new NavigationAreaPolicyKey("missing", 1), Budget(), out _)
            .Should().Be(NavigationGuideStatus.Stale);

        var leases = new NavigationWorldGraphLease?[context.Settings.MaxConcurrentSnapshotLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++) leases[i] = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
            Anchor(context, Cell(0), Profile(), out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            foreach (var lease in leases) lease?.Dispose();
        }
        Anchor(context, Cell(0), Profile(), out _).Should().Be(NavigationGuideStatus.Success);
    }

    private static void Advance(TrailblazerWorldContext context)
    {
        for (int i = 0; i < 16; i++) { context.Simulate(); context.LateSimulate(); }
    }
}
