using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using System;
using System.Collections.Generic;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class TrailblazerWorldContextTests : IDisposable
{
    private const int DefaultFrameRate = 32;

    public void Dispose()
    {
        TrailblazerWorldManager.Reset();
        TrailblazerManager.SetFrameRate(DefaultFrameRate);
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Attach_ShouldBindExternalWorldWithoutTakingOwnershipByDefault()
    {
        using var world = new GridWorld();

        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        context.World.Should().BeSameAs(world);
        context.VoxelSize.Should().Be(world.VoxelSize);

        context.Dispose();

        world.IsActive.Should().BeTrue();
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
    public void CreateOwned_ShouldCreateContextOwnedWorld()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridWorld world = context.World;

        world.IsActive.Should().BeTrue();
        context.VoxelSize.Should().Be(world.VoxelSize);

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
    public void TrailblazerManagerInitialize_ShouldCreateDefaultContextFacade()
    {
        using var world = new GridWorld();

        TrailblazerManager.Initialize(world);

        TrailblazerManager.DefaultContext.Should().NotBeNull();
        TrailblazerManager.DefaultContext.World.Should().BeSameAs(world);
        TrailblazerWorldManager.World.Should().BeSameAs(world);
    }

    [Fact]
    public void TrailblazerManagerInitialize_ShouldPreserveConfiguredFacadeFrameRate()
    {
        TrailblazerManager.SetFrameRate(48);
        using var world = new GridWorld();

        TrailblazerManager.Initialize(world);

        TrailblazerManager.FrameRate.Should().Be(48);
        TrailblazerManager.DefaultContext.FrameRate.Should().Be(48);
    }

    [Fact]
    public void TrailblazerManagerSimulate_ShouldAdvanceDefaultContextClock()
    {
        using var world = new GridWorld();
        TrailblazerManager.Initialize(world);

        TrailblazerManager.Simulate();

        TrailblazerManager.FrameCount.Should().Be(1);
        TrailblazerManager.DefaultContext.FrameCount.Should().Be(1);
    }
}
