using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationSideViewTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void GuideRetainsAnExactGroundedJoinAndChargesBothSegments(PathAlgorithm algorithm, bool reverse)
    {
        using var context = Scene(out _);
        Publish(context, PathTestFactory.BuildSideViewValley(Grid).Build());
        var profile = Profile(Fixed64.Half, radius: Fixed64.FromFraction(15, 64));
        var from = Cell(reverse ? 1 : 0);
        var to = Cell(reverse ? 0 : 1);
        var flat = new Vector2d(Fixed64.Zero, Fixed64.FromFraction(15, 64));
        var slope = new Vector2d(Fixed64.FromFraction(55, 64), Fixed64.FromFraction(36, 64));
        var corner = new Vector2d(Fixed64.FromFraction(27, 64), flat.Y);
        Anchor(context, from, profile, out var start).Should().Be(NavigationGuideStatus.Success);
        start.Should().Be(reverse ? slope : flat);
        Anchor(context, to, profile, out var end).Should().Be(NavigationGuideStatus.Success);
        end.Should().Be(reverse ? flat : slope);
        Leg(context, from, to, profile).Should().Be(NavigationGuideStatus.Success);

        RequestSideView(context, from, to, profile, algorithm, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired;
        guide.StepCount.Should().Be(3);
        guide.TotalCost.Should().Be(Fixed64.FromFraction(62, 64) + (reverse ? Fixed64.Zero : Fixed64.Half));
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(Cell(0));
        center.Should().Be(corner);
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out address, out center).Should().Be(NavigationGuideStatus.Success);
        address.Should().Be(to);
        center.Should().Be(end);
    }

    [Theory]
    [InlineData("roof")]
    [InlineData("crest")]
    [InlineData("half-axis")]
    [InlineData("rounded-intersection")]
    public void ClearEndpointsDoNotCertifyAnUnsupportedOrObstructedJoin(string obstruction)
    {
        using var context = Scene(out _);
        var builder = PathTestFactory.BuildSideViewValley(Grid, crest: obstruction == "crest");
        if (obstruction == "roof")
        {
            var roof = Cell(0, 2).Index;
            var height = Fixed64.FromFraction(5, 8);
            builder.AddCell(roof, Air).AddPlanarSupport(roof, new(Fixed64.FromFraction(3, 8), height),
                new(Fixed64.Half, height));
        }
        Publish(context, builder.Build());
        var axis = Fixed64.Half + (obstruction == "half-axis" ? Fixed64.MinIncrement : Fixed64.Zero);
        var radius = Fixed64.FromFraction(15, 64) + (obstruction == "rounded-intersection" ? Fixed64.FromRaw(5) : Fixed64.Zero);
        var profile = Profile(axis, radius: radius);
        Anchor(context, Cell(0), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(1), profile, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, Cell(0), Cell(1), profile).Should().Be(NavigationGuideStatus.NoPath);
        Leg(context, Cell(1), Cell(0), profile).Should().Be(NavigationGuideStatus.NoPath);
    }

    [Fact]
    public void JoinRejectsIntersectingLinesOutsideTheFiniteTracksAndUnrepresentableTrackEnds()
    {
        var left = new NavigationPlanarSupport(default, Vector2d.Zero, Vector2d.Right, Vector2d.Forward);
        var right = new NavigationPlanarSupport(default, Vector2d.Right, new Vector2d(Fixed64.Two, Fixed64.One),
            new Vector2d(-Fixed64.One, Fixed64.One).Normalized);
        NavigationPlanarWork.TryGetSideViewCorner(left, right, new Vector2d(Fixed64.Half, Fixed64.Quarter),
            new Vector2d(Fixed64.One + Fixed64.Half, Fixed64.One), out _).Should().BeFalse();

        left = new NavigationPlanarSupport(default, Vector2d.Zero, new Vector2d(Fixed64.One, Fixed64.Quarter),
            new Vector2d(-Fixed64.Quarter, Fixed64.One).Normalized);
        right = new NavigationPlanarSupport(default, left.Right, new Vector2d(Fixed64.Two, Fixed64.One),
            new Vector2d(-Fixed64.FromFraction(3, 4), Fixed64.One).Normalized);
        NavigationPlanarWork.TryGetSideViewCorner(left, right, new Vector2d(Fixed64.Half, Fixed64.MaxValue),
            new Vector2d(Fixed64.One + Fixed64.Half, Fixed64.One), out _).Should().BeFalse();
    }

    [Fact]
    public void JoinPinsBothAdmittedSupportOwnersAgainstCoincidentCoverage()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var left = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(0, 1, 1));
        var right = new GridConfiguration(new Vector3d(1, 0, -1), new Vector3d(1, 1, 1));
        var alias = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(1, 1, 1));
        context.World.TryAddGrid(alias, out _).Should().BeTrue();
        context.World.TryAddGrid(left, out _).Should().BeTrue();
        context.World.TryAddGrid(right, out _).Should().BeTrue();
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var denied = new NavigationCell(TraversalMedia.Solid, TraversalCapability.Fly, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        Publish(context, SideView("alias", alias).SetDefaultCell(Solid).AddCell(new VoxelIndex(1, 1, 1), denied).Build());
        var local = new VoxelIndex(0, 1, 1);
        var seam = new Vector2d(Fixed64.Half, -Fixed64.Quarter);
        Publish(context, SideView("left", left).SetDefaultCell(Solid)
            .AddPlanarSupport(local, new(-Fixed64.Half, seam.Y), seam).Build(), 3);
        Publish(context, SideView("right", right).SetDefaultCell(Solid)
            .AddPlanarSupport(local, seam, new(Fixed64.One + Fixed64.Half, Fixed64.Half)).Build(), 4);
        var from = new NavigationCellAddress("left", local);
        var to = new NavigationCellAddress("right", local);
        var profile = Profile(Fixed64.Half, radius: Fixed64.FromFraction(15, 64));
        Anchor(context, from, profile, out _).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, to, profile, out _).Should().Be(NavigationGuideStatus.Success);
        Leg(context, from, to, profile).Should().Be(NavigationGuideStatus.Success);
        Leg(context, to, from, profile).Should().Be(NavigationGuideStatus.Success);
    }
}
