using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class RawVoxelFinderTests : IDisposable
{
    public RawVoxelFinderTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumeRequestFactories_ShouldFailForInvalidEndpoints_WhenAllowUnwalkableIsFalse()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "StrictVolume");

        RawVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out _,
            out _,
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas).Should().BeFalse();

        VolumePathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas).Should().BeNull();
    }

    [Fact]
    public void VolumeRequest_ShouldResolveInvalidEndpointsConsistently_AcrossCreateUpdateAndSetters()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "RelaxedVolume");

        RawVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out Voxel startVoxel,
            out Voxel endVoxel,
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas).Should().BeTrue();

        startVoxel.WorldPosition.Should().Be(Vector3d.Zero);
        endVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        VolumePathRequest created = VolumePathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas);
        created.Should().NotBeNull();
        created.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        created.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        VolumePathRequest updated = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas);
        updated.Should().NotBeNull();

        updated.UpdateRequest(new Vector3d(-1, 0, 0), new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        updated.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        updated.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);

        VolumePathRequest setters = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas);
        setters.Should().NotBeNull();

        setters.TrySetOrigin(new Vector3d(-1, 0, 0)).Should().BeTrue();
        setters.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        setters.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        setters.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);
    }

    [Fact]
    public void VolumeSnapping_ShouldHonorHostExtendedSolidBackedEndpoints()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));

        PathTestFactory.RegisterSingleWalkablePoint("HostExtendedStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HostExtendedEnd", new Vector3d(1, 0, 0));

        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel != null
            && (voxel.WorldPosition == Vector3d.Zero
            || voxel.WorldPosition == new Vector3d(1, 0, 0)));

        RawVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out Voxel startVoxel,
            out Voxel endVoxel,
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas).Should().BeTrue();

        startVoxel.WorldPosition.Should().Be(Vector3d.Zero);
        endVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        VolumePathRequest request = VolumePathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas);
        request.Should().NotBeNull();
        request.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        request.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void VolumeRequest_ShouldRejectFallbackCandidates_WhenTheyDoNotMatchRequestedMedium()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "WrongMedium");

        VolumeMediumRules.SetLiquidVoxelRule(static _ => false);

        RawVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out _,
            out _,
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Liquid).Should().BeFalse();

        VolumePathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Liquid).Should().BeNull();
    }

    [Fact]
    public void VolumeRequest_ShouldAllowSizeFallback_WhenExactEndpointOnlyFailsClearance()
    {
        ConfigureGrid(Vector3d.Zero, new Vector3d(6, 6, 6));
        RegisterGasLine(new Vector3d(0, 2, 2), 5, "VolumeSizeFallback");

        GlobalGridManager.TryGetVoxel(new Vector3d(0, 2, 2), out Voxel edgeVoxel).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 2, 2), out Voxel interiorVoxel).Should().BeTrue();

        RawVoxelFinder.IsTraversable(edgeVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeFalse();
        RawVoxelFinder.IsTraversable(interiorVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeTrue();

        RawVoxelFinder.GetStartVoxel(
            new Vector3d(0, 2, 2),
            new Vector3d(4, 2, 2),
            out Voxel startVoxel,
            allowUnwalkableEndpoints: false,
            unitSize: Fixed64.Two,
            medium: TraversalMedium.Gas).Should().BeTrue();

        startVoxel.WorldPosition.Should().Be(new Vector3d(1, 2, 2));

        VolumePathRequest request = VolumePathRequest.Create(
            new Vector3d(0, 2, 2),
            new Vector3d(4, 2, 2),
            Fixed64.Two,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas);
        request.Should().NotBeNull();
        request.StartNode.WorldPosition.Should().Be(new Vector3d(1, 2, 2));
        request.EndNode.WorldPosition.Should().Be(new Vector3d(4, 2, 2));
    }

    private static void ConfigureGrid(Vector3d minBounds, Vector3d maxBounds)
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(minBounds, maxBounds), out _);
    }

    private static void RegisterGasLine(Vector3d start, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                new Vector3d(start.x + i, start.y, start.z),
                TraversalMedium.Gas,
                chartNamePrefix);
        }
    }
}
