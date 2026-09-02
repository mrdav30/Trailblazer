using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class TrailblazerWorldContextTests : IDisposable
{
    public void Dispose()
    {
        TestWorld.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Attach_ShouldBindExternalWorldWithoutTakingOwnershipByDefault()
    {
        using var world = new GridWorld();

        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        context.World.Should().BeSameAs(world);
        context.Dispose();

        world.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Attach_ShouldRetainExplicitContextSettings()
    {
        using var world = new GridWorld();
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;

        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(
            world,
            settings: settings);

        context.Settings.Should().BeSameAs(settings);
    }

    [Fact]
    public void Attach_ShouldRejectNullOrInactiveWorlds()
    {
        Action nullWorld = () => TrailblazerWorldContext.Attach(null!);
        nullWorld.Should().Throw<ArgumentNullException>().WithParameterName("world");

        var inactiveWorld = new GridWorld();
        inactiveWorld.Dispose();

        Action inactive = () => TrailblazerWorldContext.Attach(inactiveWorld);
        inactive.Should().Throw<InvalidOperationException>().WithMessage("*active GridWorld*");
    }

    [Fact]
    public void Attach_ShouldRejectSameWorldUntilExistingContextIsDisposed()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext contextA = TrailblazerWorldContext.Attach(world);
        TrailblazerWorldContext? duplicate = null;

        Action duplicateAttach = () => duplicate = TrailblazerWorldContext.Attach(world);

        duplicateAttach.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already*TrailblazerWorldContext*");
        duplicate?.Dispose();

        contextA.Dispose();

        using TrailblazerWorldContext contextB = TrailblazerWorldContext.Attach(world);
        contextB.World.Should().BeSameAs(world);
    }

    [Fact]
    public void Attach_ShouldDisposeExternalWorld_WhenOwnershipIsTaken()
    {
        var world = new GridWorld();

        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world, takeOwnership: true);

        context.World.Should().BeSameAs(world);

        context.Dispose();

        world.IsActive.Should().BeFalse();
    }

    [Fact]
    public void BoundContext_ShouldRejectUseAfterHostDisposesExternalWorld()
    {
        var world = new GridWorld();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
        world.Dispose();

        Action readClock = () => _ = context.FrameRate;
        Action simulate = context.Simulate;
        Action inspectNavigationGraph = () => context.Pathing.GetNavigationGraphDiagnostics();

        readClock.Should().Throw<InvalidOperationException>()
            .WithMessage("*inactive GridWorld*");
        simulate.Should().Throw<InvalidOperationException>()
            .WithMessage("*inactive GridWorld*");
        inspectNavigationGraph.Should().Throw<InvalidOperationException>()
            .WithMessage("*inactive GridWorld*");
    }

    [Fact]
    public void CreateOwned_ShouldCreateContextOwnedWorld()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridWorld world = context.World;

        world.IsActive.Should().BeTrue();
        context.Dispose();

        world.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Simulate_ShouldAdvanceEachContextClockIndependently()
    {
        using TrailblazerWorldContext contextA = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext contextB = TrailblazerWorldContext.CreateOwned();

        contextA.SetFrameRate(20);
        contextB.SetFrameRate(64);

        contextA.Simulate();
        contextA.Simulate();
        contextB.Simulate();

        contextA.FrameRate.Should().Be(20);
        contextB.FrameRate.Should().Be(64);
        contextA.FrameCount.Should().Be(2);
        contextB.FrameCount.Should().Be(1);
        contextA.DeltaTime.Should().Be(Fixed64.One / (Fixed64)20);
        contextB.DeltaTime.Should().Be(Fixed64.One / (Fixed64)64);
        contextA.TotalTime.Should().Be(contextA.DeltaTime * 2);
        contextB.TotalTime.Should().Be(contextB.DeltaTime);
    }

    [Fact]
    public void Reset_ShouldClearOnlyThisContextClock()
    {
        using TrailblazerWorldContext contextA = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext contextB = TrailblazerWorldContext.CreateOwned();

        contextA.Simulate();
        contextB.Simulate();
        contextB.Simulate();

        contextA.Reset();

        contextA.FrameCount.Should().Be(0);
        contextA.TotalTime.Should().Be(Fixed64.Zero);
        contextB.FrameCount.Should().Be(2);
        contextB.TotalTime.Should().Be(contextB.DeltaTime * 2);
    }

    [Fact]
    public void LateSimulateAndVisualize_ShouldTrackAccumulationPerContext()
    {
        using TrailblazerWorldContext contextA = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext contextB = TrailblazerWorldContext.CreateOwned();

        contextA.LateSimulate();
        contextA.Visualize();
        contextB.Visualize();
        contextB.Visualize();

        contextA.ResetAccumulation.Should().BeFalse();
        contextA.AccumulatedTime.Should().Be(contextA.DeltaTime);
        contextA.ExpectedAccumulation.Should().Be(Fixed64.One);

        contextB.ResetAccumulation.Should().BeFalse();
        contextB.AccumulatedTime.Should().Be(contextB.DeltaTime * 2);
        contextB.ExpectedAccumulation.Should().Be((Fixed64)2);
    }

    [Fact]
    public void RegisterOnSimulate_ShouldInvokeContextHooksInOrder()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var calls = new List<string>();

        using IDisposable late = context.RegisterOnSimulate("ContextHook.Late", 100, () => calls.Add("late"));
        using IDisposable early = context.RegisterOnSimulate("ContextHook.Early", -100, () => calls.Add("early"));

        context.Simulate();

        calls.Should().ContainInOrder("early", "late");
    }

    [Fact]
    public void RegisterOnSimulate_ShouldRejectBlankAndDuplicateOwnersWithoutChangingHooks()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        int callCount = 0;
        using IDisposable registration = context.RegisterOnSimulate(
            "host",
            0,
            () => callCount++);

        Action blank = () => context.RegisterOnSimulate(" ", 1, () => callCount++);
        Action duplicate = () => context.RegisterOnSimulate("host", 1, () => callCount++);

        blank.Should().Throw<ArgumentException>().WithParameterName("owner");
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*host*already registered*");
        context.Simulate();
        callCount.Should().Be(1);
    }

}
