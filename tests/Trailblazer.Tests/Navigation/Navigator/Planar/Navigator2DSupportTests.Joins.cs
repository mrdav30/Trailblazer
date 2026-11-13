using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar, NavigationAcquisitionMode.Immediate, false, false)]
    [InlineData(PathAlgorithm.FlowField, NavigationAcquisitionMode.Scheduled, true, false)]
    [InlineData(PathAlgorithm.AStar, NavigationAcquisitionMode.Scheduled, false, true)]
    public void ValleyJoinRequiresTheExactGroundedWaypoint(PathAlgorithm algorithm, NavigationAcquisitionMode mode,
        bool reverse, bool loseSupport)
    {
        Run().Should().Equal(Run());

        List<(Vector2d Center, Vector2d Velocity, NavigationGuideStatus? Status)> Run()
        {
            using var context = GuidedScene(PathTestFactory.BuildSideViewValley(GuidedGrid).Build());
            var from = GuidedAddress(reverse ? 1 : 0);
            var to = GuidedAddress(reverse ? 0 : 1);
            var profile = new NavigationAgentProfile2D(NavigationMode2D.SideView, Fixed64.FromFraction(15, 64),
                Fixed64.Half, Fixed64.Zero, Fixed64.Zero, Fixed64.Half, TraversalMedia.Solid,
                TraversalCapability.None);
            context.Pathing.ResolveSideViewAnchor(from, profile, SideViewPolicy, SideViewBudget, out var start)
                .Should().Be(NavigationGuideStatus.Success);
            context.Pathing.ResolveSideViewAnchor(to, profile, SideViewPolicy, SideViewBudget, out var end)
                .Should().Be(NavigationGuideStatus.Success);
            var corner = new Vector2d(Fixed64.FromFraction(27, 64), Fixed64.FromFraction(15, 64));
            using var host = new Host(context, profile, new NavigationMotorSettings2D(Fixed64.Two, Fixed64.Zero,
                (Fixed64)16, Fixed64.Zero), new NavigationPose2D(start, Fixed64.Zero));
            host.Sample(Ground);
            host.RefreshSupport();
            host.ConfigureAcquisitionMode(mode);
            host.ApplySideViewGuidedTrekRequest(from, to, SideViewPolicy, algorithm, SideViewBudget)
                .Should().Be(mode == NavigationAcquisitionMode.Immediate ? NavigationGuideStatus.Success : NavigationGuideStatus.Pending);

            bool acceptedCorner = false;
            bool clipped = false;
            var replay = new List<(Vector2d, Vector2d, NavigationGuideStatus?)>();
            for (int i = 0; i < 128 && !host.HasArrived; i++)
            {
                context.Simulate();
                host.PrepareFrameMotion();
                var target = host.Request.Target.Center;
                if (!acceptedCorner)
                    (reverse ? target.X >= corner.X : target.X <= corner.X).Should().BeTrue();
                if (!reverse && !clipped && target == corner)
                {
                    target = (target + host.Pose.Center) * Fixed64.Half;
                    clipped = true;
                }
                host.Accept(target, loseSupport && target == corner ? null : Ground);
                host.CommitFrameMotion();
                context.LateSimulate();
                replay.Add((host.Pose.Center, host.Velocity, host.GuidanceStatus));
                acceptedCorner |= target == corner;
                if (loseSupport && acceptedCorner)
                    break;
            }

            acceptedCorner.Should().BeTrue();
            clipped.Should().Be(!reverse);
            host.Pose.Center.Should().Be(loseSupport ? corner : end);
            host.HasArrived.Should().Be(!loseSupport);
            host.GuidanceStatus.Should().Be(loseSupport ? NavigationGuideStatus.InvalidEnd : NavigationGuideStatus.Success);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
            return replay;
        }
    }
}
