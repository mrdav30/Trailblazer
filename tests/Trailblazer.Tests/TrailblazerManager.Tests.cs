using FixedMathSharp;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Tests.Navigation.Steering;
using Trailblazer.Tests.Navigation.Turning;
using Xunit;

namespace Trailblazer.Tests;

[Collection("TrailblazerCollection")]
public class TrailblazerManagerTests : IDisposable
{
    private const int DefaultFrameRate = 32;

    public void Dispose()
    {
        TrailblazerManager.SetFrameRate(DefaultFrameRate);
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Simulate_Should_InvokeOrderedHooks_InAscendingOrder()
    {
        var calls = new List<string>();
        using var late = TrailblazerManager.RegisterOnSimulate(
            owner: "TrailblazerManagerTests.Simulate.Late",
            order: 900,
            callback: () => calls.Add("late"));
        using var early = TrailblazerManager.RegisterOnSimulate(
            owner: "TrailblazerManagerTests.Simulate.Early",
            order: -900,
            callback: () => calls.Add("early"));

        TrailblazerManager.Simulate();

        calls.Should().ContainInOrder("early", "late");
    }

    [Fact]
    public void SetFrameRate_Should_RefreshNavSteeringAutoStopThreshold_AfterTypeInitialization()
    {
        var steering = new NavSteering(Fixed64.One);
        var agent = new MockSteerAgent();

        TrailblazerManager.SetFrameRate(64);
        steering.PauseAutoStop();

        for (int i = 0; i < 7; i++)
            steering.GetHeading(agent);

        steering.CanAutoStop.Should().BeFalse();

        steering.GetHeading(agent);

        steering.CanAutoStop.Should().BeTrue();
    }

    [Fact]
    public void SetFrameRate_Should_RefreshNavTurningCollisionThreshold_AfterInitialization()
    {
        TrailblazerManager.SetFrameRate(DefaultFrameRate);

        var turning = new NavTurning(Fixed64.One);
        var navigator = new MockTurnAgent
        {
            LastPosition = Vector3d.Zero,
            Position = new Vector3d(Fixed64.Zero, (Fixed64)0.01f, Fixed64.Zero),
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity
        };

        TrailblazerManager.SetFrameRate(64);

        turning.NotifyCollision();
        turning.TrySimulateTurn(navigator.Position, navigator.LastPosition, navigator.Forward, navigator.Rotation, out _);
        turning.TrySimulateTurn(navigator.Position, navigator.LastPosition, navigator.Forward, navigator.Rotation, out _);

        turning.TargetReached.Should().BeFalse();
    }
}
