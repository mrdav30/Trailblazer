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
    [InlineData("half-axis")]
    [InlineData("rounded-intersection")]
    public void ClearEndpointsDoNotCertifyAnUnsupportedOrObstructedJoin(string obstruction)
    {
        using var context = Scene(out _);
        var builder = PathTestFactory.BuildSideViewValley(Grid);
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

    [Theory]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    public void CrestAndSlopedStepCannotSupplyAnOrdinaryRoute(PathAlgorithm algorithm, bool crest)
    {
        using var context = Scene(out _, settings: PlanarSchedulingTestSettings.Create());
        var seam = new Vector2d(Fixed64.Half, Fixed64.Quarter);
        var lowerHeight = crest ? seam.Y : -Fixed64.Half;
        var builder = SideView("side", Grid).SetDefaultCell(Solid);
        AddFloor(builder, 0, lowerHeight);
        builder.AddPlanarSupport(Cell(1).Index, seam, new(Fixed64.One + Fixed64.Half, -Fixed64.Half));
        Publish(context, builder.Build());
        var profile = Profile(Fixed64.Half, up: Fixed64.One, down: Fixed64.One,
            radius: Fixed64.FromFraction(15, 64), capabilities: TraversalCapability.Jump);
        Anchor(context, Cell(0), profile, out var flat).Should().Be(NavigationGuideStatus.Success);
        Anchor(context, Cell(1), profile, out var slope).Should().Be(NavigationGuideStatus.Success);

        // Both the lifted crest chord and the horizontal crossing at the slope anchor's height cut the seam.
        var halfAxis = Vector2d.Forward * (profile.AxisLength * Fixed64.Half);
        var attempted = crest
            ? Vector2d.Lerp(new Vector2d(seam.X, flat.Y), slope, Fixed64.Quarter)
            : new Vector2d(seam.X, slope.Y);
        Vector2d.CompareDistanceSquared(attempted - halfAxis, seam, Vector2d.Zero, Vector2d.Right * profile.Radius)
            .Should().BeLessThan(0);
        (seam.Y - lowerHeight).Should().BeLessThan(profile.MaxStepUp);
        (slope.Y - flat.Y).Abs().Should().BeLessThan(profile.MaxDropDown);

        CheckDirection(Cell(0), Cell(1));
        CheckDirection(Cell(1), Cell(0));
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        void CheckDirection(NavigationCellAddress from, NavigationCellAddress to)
        {
            Leg(context, from, to, profile).Should().Be(NavigationGuideStatus.NoPath);
            var status = RequestSideView(context, from, to, profile, algorithm, out var acquired);
            using var immediate = acquired;
            status.Should().Be(NavigationGuideStatus.NoPath);
            immediate.StepCount.Should().Be(0);

            context.Guides.ScheduleSideViewGuide(from, to, profile, Policy.Key, algorithm, GuideBudget(), out var scheduled)
                .Should().Be(NavigationGuideStatus.Pending);
            using var request = TestRequire.NotNull(scheduled);
            RunScheduled(context, request).Should().Be(NavigationGuideStatus.NoPath);
            request.TryTakeSideViewGuide(out _).Should().BeFalse();
        }
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
