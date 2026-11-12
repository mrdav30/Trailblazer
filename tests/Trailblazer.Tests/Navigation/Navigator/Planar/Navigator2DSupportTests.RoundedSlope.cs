using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RoundedSlopesStayInTheCanonicalStripAndReplayExactly(bool steep, bool descending)
    {
        Run().Should().Equal(Run());

        List<(Vector2d Position, Vector2d Velocity)> Run()
        {
            using var context = TallSlopeScene(steep, ceiling: false, out var profile);
            var source = TallAddress(descending ? 1 : 0);
            var destination = TallAddress(descending ? 0 : 1);
            context.Pathing.ResolveSideViewAnchor(source, profile, SideViewPolicy, SideViewBudget, out var start)
                .Should().Be(NavigationGuideStatus.Success);
            context.Pathing.ResolveSideViewAnchor(destination, profile, SideViewPolicy, SideViewBudget, out var end)
                .Should().Be(NavigationGuideStatus.Success);
            // Choose an even rounded displacement with a nonzero line residual for the exact midpoint clip.
            Fixed64 speed = Fixed64.FromRaw(((Fixed64)3).m_rawValue + (steep == descending ? -16 : 64));
            using var host = new Host(context, profile,
                new NavigationMotorSettings2D(speed, Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
                new NavigationPose2D(start, Fixed64.Zero));
            host.Sample(Ground);
            host.RefreshSupport();
            host.ConfigureAcquisitionMode(descending ? NavigationAcquisitionMode.Scheduled : NavigationAcquisitionMode.Immediate);
            host.ApplySideViewGuidedTrekRequest(source, destination, SideViewPolicy, PathAlgorithm.FlowField, SideViewBudget)
                .Should().Be(descending ? NavigationGuideStatus.Pending : NavigationGuideStatus.Success);
            bool clipped = false;
            bool rounded = false;
            var replay = new List<(Vector2d, Vector2d)>();
            for (int frame = 0; frame < 96 && !host.HasArrived; frame++)
            {
                context.Simulate();
                var before = host.Pose.Center;
                host.PrepareFrameMotion();
                var target = host.Request.Target.Center;
                var delta = target - before;
                Vector2d.CompareDistanceSquared(before, target, Vector2d.Zero,
                    new Vector2d(speed / 16, Fixed64.Zero)).Should().BeLessThanOrEqualTo(0);
                long x = target.X.m_rawValue - start.X.m_rawValue;
                long z = target.Y.m_rawValue - start.Y.m_rawValue;
                long strip = steep ? 3 * z - 4 * x : 4 * z - 3 * x;
                strip.Should().BeInRange(0, 8);
                rounded |= strip != 0;
                if (!clipped && strip != 0 && target != before
                    && (delta.X.m_rawValue & 1) == 0 && (delta.Y.m_rawValue & 1) == 0)
                {
                    target = before + delta * Fixed64.Half;
                    clipped = true;
                }
                host.Accept(target, Ground);
                host.CommitFrameMotion();
                context.LateSimulate();
                replay.Add((host.Pose.Center, host.Velocity));
            }
            host.HasArrived.Should().BeTrue();
            host.Pose.Center.Should().Be(end);
            clipped.Should().BeTrue();
            rounded.Should().BeTrue();
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
            return replay;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundedTargetCannotPenetrateAHeadroomSurfaceTangentToTheCanonicalSweep(bool split)
    {
        using var context = TallSlopeScene(steep: false, ceiling: true, out var profile, split);
        var source = TallAddress(split ? 1 : 0);
        var end = TallAddress(split ? 0 : 1);
        context.Pathing.ResolveSideViewAnchor(source, profile, SideViewPolicy, SideViewBudget, out var start)
            .Should().Be(NavigationGuideStatus.Success);
        context.Pathing.ValidateSideViewLeg(source, end, profile, SideViewPolicy, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Success);
        if (split)
        {
            using var snapshot = TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph());
            snapshot.Graph.TryGetMap("headroom", out var headroom).Should().BeTrue();
            headroom.Map.TryGetPlanarSupport(new VoxelIndex(1, 1, 0), out var surface).Should().BeTrue();
            surface.CheckClearance(start, start - Vector2d.Right * Fixed64.MinIncrement, profile.AxisLength,
                profile.Radius).Should().Be(NavigationVolumeAnchorStatus.Unavailable);
        }
        using var host = new Host(context, profile,
            new NavigationMotorSettings2D(Fixed64.FromRaw(16), Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
            new NavigationPose2D(start, Fixed64.Zero));
        host.Sample(Ground);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(source, end, SideViewPolicy, PathAlgorithm.AStar, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        Step(context, host, default);
        host.Pose.Center.Should().Be(start);
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.NoPath);
        host.HasArrived.Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneRawDownhillUsesCardinalSubstepsWithinTheOpenStrip(bool steep)
    {
        using var context = TallSlopeScene(steep, ceiling: false, out var profile, reverseGrade: steep);
        var source = TallAddress(steep ? 0 : 1);
        var end = TallAddress(steep ? 1 : 0);
        context.Pathing.ResolveSideViewAnchor(source, profile, SideViewPolicy, SideViewBudget, out var start)
            .Should().Be(NavigationGuideStatus.Success);
        using var host = new Host(context, profile,
            new NavigationMotorSettings2D(Fixed64.FromRaw(16), Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
            new NavigationPose2D(start, Fixed64.Zero));
        host.Sample(Ground);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(source, end, SideViewPolicy, PathAlgorithm.AStar, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        for (int i = 0; i < 8; i++)
        {
            var before = host.Pose.Center;
            Step(context, host, default);
            var delta = host.Pose.Center - before;
            (delta.X.Abs() + delta.Y.Abs()).Should().Be(Fixed64.MinIncrement);
            long x = host.Pose.Center.X.m_rawValue - start.X.m_rawValue;
            long z = host.Pose.Center.Y.m_rawValue - start.Y.m_rawValue;
            (steep ? 3 * z + 4 * x : 4 * z - 3 * x).Should().BeInRange(0, 8);
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.Success);
        }
    }

    [Fact]
    public void RoundedProofUsesBoundedWorkAndRequiresARouteContactAtEachEndpoint()
    {
        using var context = TallSlopeScene(steep: false, ceiling: false, out var profile);
        var from = TallAddress(0);
        var to = TallAddress(1);
        context.Pathing.ResolveSideViewAnchor(from, profile, SideViewPolicy, SideViewBudget, out var start)
            .Should().Be(NavigationGuideStatus.Success);
        context.Pathing.TryGetPlanarBinding("side", out var plane).Should().BeTrue();
        var far = new Vector2d((Fixed64)10, (Fixed64)10);
        Prove(new NavigationCellAddress("missing", default), start, start, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Stale);
        Prove(new NavigationCellAddress("side", new VoxelIndex(10, 1, 0)), start, start, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Stale);
        Prove(to, start, start, SideViewBudget, new NavigationCellAddress("side", new VoxelIndex(0, 0, 0)))
            .Should().Be(NavigationGuideStatus.Stale);
        Prove(to, start, start, Budget(lookups: 4096, edges: 0)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Prove(to, start, start, Budget(lookups: 0, edges: 1)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Prove(to, start, start, Budget(lookups: 1, edges: 1)).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Prove(to, far, start, SideViewBudget).Should().Be(NavigationGuideStatus.NoPath);
        Prove(to, start, far, SideViewBudget).Should().Be(NavigationGuideStatus.NoPath);

        var other = new GridConfiguration(new Vector3d(5, 0, 0), new Vector3d(6, 1, 0));
        context.World.TryAddGrid(other, out _).Should().BeTrue();
        PublishGuidedMap(context, new NavigationMapBuilder("elsewhere", other)
            .SetPlanarLayer(NavigationMode2D.SideView, 0, Fixed64.Zero).Build(), 3);
        Prove(new NavigationCellAddress("elsewhere", default), start, start, SideViewBudget)
            .Should().Be(NavigationGuideStatus.Stale);

        NavigationGuideStatus Prove(NavigationCellAddress destination, Vector2d a, Vector2d b,
            NavigationWorkBudget budget, NavigationCellAddress? source = null) =>
            context.Pathing.ValidateSideViewControllerMotion(source ?? from, destination, "side", plane, "side",
                a, b, profile, SideViewPolicy, budget);
        static NavigationWorkBudget Budget(int lookups, int edges) =>
            new(lookups, 2, 64, edges, 0, 0, 0, 0, 0, 4096, 0);
    }

    private static NavigationCellAddress TallAddress(int x) => new("side", new VoxelIndex(x, 1, 0));

    private static TrailblazerWorldContext TallSlopeScene(bool steep, bool ceiling, out NavigationAgentProfile2D profile,
        bool split = false, bool reverseGrade = false)
    {
        Fixed64 width = steep ? Fixed64.FromFraction(3, 4) : Fixed64.One;
        Fixed64 rise = steep ? Fixed64.One : Fixed64.FromFraction(3, 4);
        var context = TrailblazerWorldContext.CreateOwned(settings: PlanarSchedulingTestSettings.Create());
        context.SetFrameRate(16);
        var metrics = GridTopologyMetrics.Rectangular(width, Fixed64.One, Fixed64.Two);
        var grid = new GridConfiguration(Vector3d.Zero,
            new Vector3d(width, Fixed64.One, split ? Fixed64.Zero : Fixed64.Two), topologyMetrics: metrics);
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, (Fixed64)3);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        // A 3-4-5 slope and a radius divisible by five raw units give exact tangent witnesses.
        Fixed64 radius = Fixed64.FromRaw(500_000_000);
        Fixed64 axis = Fixed64.FromFraction(7, 4);
        profile = new NavigationAgentProfile2D(NavigationMode2D.SideView, radius, axis, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Half, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        Fixed64 ceilingHeight = axis + Fixed64.FromRaw(1_250_000_000);
        var builder = new NavigationMapBuilder("side", grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One);
        var headroomGrid = new GridConfiguration(new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.Two),
            new Vector3d(width, Fixed64.One, Fixed64.Two), topologyMetrics: metrics);
        var headroomBuilder = split
            ? new NavigationMapBuilder("headroom", headroomGrid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            : builder;
        for (int x = 0; x < 2; x++)
        {
            var owner = TallAddress(x).Index;
            var headroom = new VoxelIndex(x, 1, split ? 0 : 1);
            Vector2d left = new(width * x - width * Fixed64.Half, rise * (reverseGrade ? 1 - x : x - 1));
            Vector2d right = left + new Vector2d(width, reverseGrade ? -rise : rise);
            builder.AddCell(owner, solid).AddPlanarSupport(owner, left, right);
            headroomBuilder.AddCell(headroom, air);
            if (ceiling)
                headroomBuilder.AddPlanarSupport(headroom, left + new Vector2d(Fixed64.Zero, ceilingHeight),
                    right + new Vector2d(Fixed64.Zero, ceilingHeight));
        }
        var policy = new NavigationAreaPolicy(SideViewPolicy, new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy, 1, 1)).Should().BeTrue();
        PublishGuidedMap(context, builder.Build(), 2);
        if (split)
        {
            context.World.TryAddGrid(headroomGrid, out _).Should().BeTrue();
            PublishGuidedMap(context, headroomBuilder.Build(), 3);
            // This earlier, narrower lattice contains the tall capsule's center and supplies no ceiling.
            var aliasGrid = new GridConfiguration(new Vector3d(-Fixed64.Quarter, Fixed64.Zero, Fixed64.Two),
                new Vector3d(width + Fixed64.Quarter, Fixed64.One, Fixed64.Two),
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.FromFraction(3, 4), Fixed64.One, Fixed64.Two));
            context.World.TryAddGrid(aliasGrid, out _).Should().BeTrue();
            PublishGuidedMap(context, new NavigationMapBuilder("alias", aliasGrid)
                .SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One).SetDefaultCell(air).Build(), 4);
        }
        return context;
    }
}
