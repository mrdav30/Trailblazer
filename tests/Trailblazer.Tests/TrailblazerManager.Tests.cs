using FixedMathSharp;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Reflection;
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
        TrailblazerManager.Initialize();

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
    public void Initialize_Should_BeIdempotent_And_NotDuplicateInternalHooks()
    {
        TrailblazerManager.Initialize();

        int simulateHookCount = GetLifecycleHookCount("_simulateHooks");
        int resetHookCount = GetLifecycleHookCount("_resetHooks");

        TrailblazerManager.Initialize();

        GetLifecycleHookCount("_simulateHooks").Should().Be(simulateHookCount);
        GetLifecycleHookCount("_resetHooks").Should().Be(resetHookCount);
        simulateHookCount.Should().BeGreaterThanOrEqualTo(1);
        resetHookCount.Should().BeGreaterThanOrEqualTo(1);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetFrameRate_ShouldRejectNonPositiveValues_AndPreserveCurrentTiming(int invalidFrameRate)
    {
        TrailblazerManager.SetFrameRate(DefaultFrameRate);

        Action act = () => TrailblazerManager.SetFrameRate(invalidFrameRate);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("frameRate");
        TrailblazerManager.FrameRate.Should().Be(DefaultFrameRate);
        TrailblazerManager.DeltaTime.Should().Be(Fixed64.One / (Fixed64)DefaultFrameRate);
    }

    [Fact]
    public void LateSimulateAndVisualize_ShouldInvokeHooks_AndTrackAccumulation()
    {
        TrailblazerManager.Reset();

        int lateCalls = 0;
        int visualizeCalls = 0;

        using var late = TrailblazerManager.RegisterOnLateSimulate(
            owner: "TrailblazerManagerTests.Late",
            order: 0,
            callback: () => lateCalls++);
        using var visualize = TrailblazerManager.RegisterOnVisualize(
            owner: "TrailblazerManagerTests.Visualize",
            order: 0,
            callback: () => visualizeCalls++);

        TrailblazerManager.LateSimulate();

        lateCalls.Should().Be(1);
        TrailblazerManager.ResetAccumulation.Should().BeTrue();

        TrailblazerManager.Visualize();
        TrailblazerManager.Visualize();

        visualizeCalls.Should().Be(2);
        TrailblazerManager.ResetAccumulation.Should().BeFalse();
        TrailblazerManager.AccumulatedTime.Should().Be(TrailblazerManager.DeltaTime * 2);
        TrailblazerManager.ExpectedAccumulation.Should().Be((Fixed64)2);
    }

    [Fact]
    public void ResetAndFrameRateChanged_ShouldInvokeRegisteredHooks()
    {
        TrailblazerManager.Reset();

        int resetCalls = 0;
        int frameRateCalls = 0;

        using var reset = TrailblazerManager.RegisterOnReset(
            owner: "TrailblazerManagerTests.Reset",
            order: 0,
            callback: () => resetCalls++);
        using var frameRate = TrailblazerManager.RegisterOnFrameRateChanged(
            owner: "TrailblazerManagerTests.FrameRate",
            order: 0,
            callback: () => frameRateCalls++);

        TrailblazerManager.Simulate();
        TrailblazerManager.SetFrameRate(48);

        frameRateCalls.Should().Be(1);
        TrailblazerManager.FrameRate.Should().Be(48);
        TrailblazerManager.DeltaTime.Should().Be(Fixed64.One / (Fixed64)48);

        TrailblazerManager.Reset();

        resetCalls.Should().Be(1);
        TrailblazerManager.FrameCount.Should().Be(0);
        TrailblazerManager.TotalTime.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void GetFrameFromTime_ShouldUseCurrentInverseDeltaTime()
    {
        TrailblazerManager.SetFrameRate(10);

        Fixed64 timestamp = (TrailblazerManager.DeltaTime * 3) + (TrailblazerManager.DeltaTime / 2);

        TrailblazerManager.GetFrameFromTime(timestamp).Should().Be(3);
    }

    private static int GetLifecycleHookCount(string fieldName)
    {
        FieldInfo field = typeof(TrailblazerManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Unable to find TrailblazerManager field '{fieldName}'.");
        object hooks = field.GetValue(null)
            ?? throw new InvalidOperationException($"TrailblazerManager field '{fieldName}' was null.");
        PropertyInfo countProperty = hooks.GetType().GetProperty("Count")
            ?? throw new InvalidOperationException($"Lifecycle hook collection '{fieldName}' does not expose a Count property.");

        return (int)(countProperty.GetValue(hooks)
            ?? throw new InvalidOperationException($"Lifecycle hook collection '{fieldName}' returned a null Count value."));
    }
}
