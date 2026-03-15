using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorTests : IDisposable
{
    public NavigatorTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_CreateAStarRequest_FromNavigatorDefaults()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorAStar", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowUnwalkable = true;
        navigator.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        navigator.GuidedAStarMaxClimbHeight = (Fixed64)2;

        Vector3d target = new(4, 0, 0);
        navigator.ApplyGuidedTrekRequest(target, rate: TrekRate.Moderate, groupId: 4);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
        navigator.Steering.MovementGroupID.Should().Be(4);

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.AllowUnwalkable.Should().BeTrue();
        request.Heuristic.Should().Be(HeuristicMethod.Euclidean);
        request.MaxClimbHeight.Should().Be((Fixed64)2);

        PathManager.UnloadChart("NavigatorAStar");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_Allow_PerCallPathModeOverride()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorFlowField", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.GuidedPathMode = GuidedPathMode.AStar;
        navigator.GuidedAllowUnwalkable = true;
        navigator.GuidedFlowFieldExtraFloodRange = 24;

        Vector3d target = new(4, 0, 0);
        navigator.ApplyGuidedTrekRequest(target, pathMode: GuidedPathMode.FlowField, rate: TrekRate.Fast);

        navigator.IsGuideded.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Fast);

        var request = navigator.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        request.Origin.Should().Be(navigator.Position);
        request.TargetPosition.Should().Be(target);
        request.UnitSize.Should().Be(navigator.Size);
        request.AllowUnwalkable.Should().BeTrue();
        request.ExtraFloodRange.Should().Be(24);

        PathManager.UnloadChart("NavigatorFlowField");
    }

    [Fact]
    public void ApplyGuidedTrekRequest_Should_IgnoreInvalidTargets_WithoutEnteringGuidedMode()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyGuidedTrekRequest(new Vector3d(100, 0, 100), rate: TrekRate.Moderate);

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.Steering.CurrentRequest.Should().BeNull();
        navigator.Steering.ShouldMove.Should().BeFalse();
    }

    [Fact]
    public void Reset_ShouldClearGuidedMode()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorResetGuided", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Moderate);

        navigator.IsGuideded.Should().BeTrue();

        navigator.Reset();

        navigator.IsGuideded.Should().BeFalse();

        PathManager.UnloadChart("NavigatorResetGuided");
    }

    [Fact]
    public void Simulate_ShouldResolveHeading_ForGuidedRequests()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("NavigatorGuidedHeading", data, Vector3d.Zero);

        var navigator = CreateNavigator(Vector3d.Zero);
        navigator.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Moderate);

        TrailblazerManager.Simulate();
        navigator.Simulate();

        navigator.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        Vector3d.Dot(navigator.FrameRequest.Direction, Vector3d.Right).Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("NavigatorGuidedHeading");
    }

    private static TestNavigator CreateNavigator(Vector3d position)
    {
        var navigator = new TestNavigator();
        navigator.Setup(position, size: Fixed64.One);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Ground,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }
}
