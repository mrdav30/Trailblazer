using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
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
        context.VoxelSize.Should().Be(GridWorld.DefaultRectangularCellSize);

        context.Dispose();

        world.IsActive.Should().BeTrue();
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
    public void CreateOwned_ShouldCreateContextOwnedWorld()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridWorld world = context.World;

        world.IsActive.Should().BeTrue();
        context.VoxelSize.Should().Be(GridWorld.DefaultRectangularCellSize);

        context.Dispose();

        world.IsActive.Should().BeFalse();
    }

    [Fact]
    public void VoxelSize_ShouldUseConfiguredCubicCellEdge()
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.One,
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)3)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        context.VoxelSize.Should().Be((Fixed64)3);
    }

    [Fact]
    public void VoxelSize_ShouldRejectHexTopology()
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.One,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        Action readVoxelSize = () => _ = context.VoxelSize;

        readVoxelSize.Should().Throw<NotSupportedException>().WithMessage("*hex*fast-follow*");
    }

    [Fact]
    public void VoxelSize_ShouldRejectSparseStorage()
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.One,
                storageKind: GridStorageKind.Sparse),
            new[] { new VoxelIndex(0, 0, 0) },
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        Action readVoxelSize = () => _ = context.VoxelSize;

        readVoxelSize.Should().Throw<NotSupportedException>().WithMessage("*sparse*fast-follow*");
    }

    [Fact]
    public void VoxelSize_ShouldRejectAnisotropicMetrics()
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.One,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, (Fixed64)2, Fixed64.One)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        Action readVoxelSize = () => _ = context.VoxelSize;

        readVoxelSize.Should().Throw<NotSupportedException>().WithMessage("*anisotropic*fast-follow*");
    }

    [Fact]
    public void VoxelSize_ShouldRejectConflictingActiveGridMetrics()
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.One,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One)),
            out _).Should().BeTrue();
        world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(2, 0, 0),
                new Vector3d(4, 2, 2),
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

        Action readVoxelSize = () => _ = context.VoxelSize;

        readVoxelSize.Should().Throw<NotSupportedException>().WithMessage("*conflicting*fast-follow*");
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

}
