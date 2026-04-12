using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class VolumeVoxelFinderTests : IDisposable
{
    public VolumeVoxelFinderTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();
        VolumeMediumRules.ClearGasVoxelRule();
        VolumeMediumRules.ClearLiquidVoxelRule();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumeRequestFactories_ShouldFailForInvalidEndpoints_WhenAllowUnwalkableIsFalse()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "StrictVolume");

        VolumeVoxelFinder.TryGetPathEdgeVoxels(
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

        VolumeVoxelFinder.TryGetPathEdgeVoxels(
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

        VolumeVoxelFinder.TryGetPathEdgeVoxels(
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

        VolumeVoxelFinder.TryGetPathEdgeVoxels(
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

        VolumeVoxelFinder.IsTraversable(edgeVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeFalse();
        VolumeVoxelFinder.IsTraversable(interiorVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeTrue();

        VolumeVoxelFinder.GetStartVoxel(
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

    [Fact]
    public void VolumeRequest_ShouldAllowSizeFallback_WhenSolidBackedEndpointOnlyFailsClearance()
    {
        ConfigureGrid(Vector3d.Zero, new Vector3d(6, 6, 6));
        bool[,,] data = new bool[1, 5, 3];
        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 3; z++)
                data[0, x, z] = true;
        }
        PathTestFactory.RegisterFromData("SolidGasFallback", data, Vector3d.Zero);

        VolumeMediumRules.SetGasVoxelRule(static voxel => voxel != null && voxel.HasPartition<SolidChartPartition>());

        GlobalGridManager.TryGetVoxel(new Vector3d(0, 0, 1), out Voxel edgeVoxel).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel interiorVoxel).Should().BeTrue();

        VolumeVoxelFinder.IsTraversable(edgeVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeFalse();
        VolumeVoxelFinder.IsTraversable(interiorVoxel, Fixed64.Two, TraversalMedium.Gas).Should().BeTrue();

        VolumeVoxelFinder.GetStartVoxel(
            new Vector3d(0, 0, 1),
            new Vector3d(4, 0, 1),
            out Voxel startVoxel,
            allowUnwalkableEndpoints: false,
            unitSize: Fixed64.Two,
            medium: TraversalMedium.Gas).Should().BeTrue();

        startVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 1));
    }

    [Fact]
    public void IsDirectPathClear_ShouldReturnFalse_WhenMediumIsNotConfigured()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        // No gas rule, no authored gas volume → IsConfigured(Gas) = false.
        VolumeVoxelFinder.IsDirectPathClear(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas).Should().BeFalse();
    }

    [Fact]
    public void IsDirectPathClear_ShouldReturnTrue_WhenGasCorridorIsUnobstructed()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 3, "GasClearLine");

        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel startVoxel).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(2, 0, 0), out Voxel endVoxel).Should().BeTrue();

        VolumeVoxelFinder.IsDirectPathClear(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas,
            startNode: startVoxel,
            endNode: endVoxel).Should().BeTrue();
    }

    [Fact]
    public void IsDirectPathClear_ShouldReturnFalse_WhenBlockerIsInPath()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        // Only the endpoints are gas — nothing in between.
        PathTestFactory.RegisterGeneratedVolumePoint(Vector3d.Zero, TraversalMedium.Gas, "GasGap");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 0), TraversalMedium.Gas, "GasGap");

        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel startVoxel).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(2, 0, 0), out Voxel endVoxel).Should().BeTrue();

        // The voxel at (1,0,0) has no gas medium → IsTraversable(1,0,0) returns false.
        VolumeVoxelFinder.IsDirectPathClear(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false,
            medium: TraversalMedium.Gas,
            startNode: startVoxel,
            endNode: endVoxel).Should().BeFalse();
    }

    [Fact]
    public void IsDirectPathClear_ShouldReturnFalse_WhenRelaxedEndpointFailsMediumCheck()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        // Solid start, then gas neighbors — the solid voxel is registered as the relaxed startNode but
        // it has no gas membership, so PassesMedium fails and IsDirectPathClear returns false.
        PathTestFactory.RegisterSingleWalkablePoint("RelaxedSolidStart", Vector3d.Zero);
        RegisterGasLine(new Vector3d(1, 0, 0), 2, "RelaxedGasRest");

        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel solidStart).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(2, 0, 0), out Voxel gasEnd).Should().BeTrue();

        // allowUnwalkableEndpoints=true + startNode given → the solid voxel is a relaxed endpoint
        // but PassesMedium(solidVoxel, Gas) = false → returns false.
        VolumeVoxelFinder.IsDirectPathClear(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas,
            startNode: solidStart,
            endNode: gasEnd).Should().BeFalse();
    }

    [Fact]
    public void IsDirectPathClear_ShouldAllowRelaxedEndNode_WhenOnlyEndNodeMatches()
    {
        ConfigureGrid(Vector3d.Zero, new Vector3d(6, 6, 6));
        RegisterGasLine(new Vector3d(0, 2, 2), 3, "RelaxedEndOnly");

        GlobalGridManager.TryGetVoxel(new Vector3d(1, 2, 2), out Voxel interiorStart).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(0, 2, 2), out Voxel edgeEnd).Should().BeTrue();

        VolumeVoxelFinder.IsTraversable(interiorStart, Fixed64.Two, TraversalMedium.Gas).Should().BeTrue();
        VolumeVoxelFinder.IsTraversable(edgeEnd, Fixed64.Two, TraversalMedium.Gas).Should().BeFalse();

        VolumeVoxelFinder.IsDirectPathClear(
            new Vector3d(1, 2, 2),
            new Vector3d(0, 2, 2),
            Fixed64.Two,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas,
            startNode: null,
            endNode: edgeEnd).Should().BeTrue();
    }

    [Fact]
    public void IsTraversable_ShouldReturnFalse_WhenVoxelHasNoTrailblazerPartition()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        // Register gas elsewhere so IsConfigured(Gas) = true, giving us a configured medium.
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(3, 0, 0), TraversalMedium.Gas, "GasElsewhere");

        // Get a plain grid voxel that has no chart partition at all.
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel plainVoxel).Should().BeTrue();

        // IsBaseTraversable → no VolumeChartPartition and no SolidChartPartition → return false.
        VolumeVoxelFinder.IsTraversable(plainVoxel, Fixed64.One, TraversalMedium.Gas).Should().BeFalse();
    }

    private static void ConfigureGrid(Vector3d minBounds, Vector3d maxBounds)
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(minBounds, maxBounds), out _);
    }

    /// <summary>
    /// Covers the null-coalescing branch <c>unitSize ?? GlobalGridManager.VoxelSize</c> in
    /// <c>GetStartVoxel</c> when <c>unitSize</c> is omitted (defaults to <c>null</c>).
    /// </summary>
    [Fact]
    public void GetStartVoxel_ShouldUseGlobalVoxelSize_WhenUnitSizeIsOmitted()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "GasStartNullSize");

        // No unitSize argument → parameter defaults to null → covers the ?? VoxelSize branch.
        bool resolved = VolumeVoxelFinder.GetStartVoxel(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            out Voxel startVoxel);

        resolved.Should().BeTrue();
        startVoxel.Should().NotBeNull();
        startVoxel.WorldPosition.Should().Be(Vector3d.Zero);
    }

    /// <summary>
    /// Covers the null-coalescing branch <c>unitSize ?? GlobalGridManager.VoxelSize</c> in
    /// <c>GetEndVoxel</c> when <c>unitSize</c> is omitted (defaults to <c>null</c>).
    /// </summary>
    [Fact]
    public void GetEndVoxel_ShouldUseGlobalVoxelSize_WhenUnitSizeIsOmitted()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        RegisterGasLine(Vector3d.Zero, 2, "GasEndNullSize");

        // No unitSize argument → parameter defaults to null → covers the ?? VoxelSize branch.
        bool resolved = VolumeVoxelFinder.GetEndVoxel(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            out Voxel endVoxel);

        resolved.Should().BeTrue();
        endVoxel.Should().NotBeNull();
        endVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void TryGetClosestTraversableVoxel_ShouldPreferPerpendicularNeighbor_WhenOneIsAvailable()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 1), TraversalMedium.Gas, "GasClosestPerpendicular");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 2), TraversalMedium.Gas, "GasClosestPerpendicular");

        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel center).Should().BeTrue();

        VolumeVoxelFinder.TryGetClosestTraversableVoxel(
            center,
            out Voxel closestNeighbor,
            Fixed64.One,
            TraversalMedium.Gas).Should().BeTrue();

        closestNeighbor.Should().NotBeNull();
        closestNeighbor.WorldPosition.Should().Be(new Vector3d(2, 0, 1),
            "perpendicular neighbors are searched before diagonal candidates");
    }

    [Fact]
    public void TryGetClosestTraversableVoxel_ShouldFallbackToDiagonalNeighbor_WhenNoPerpendicularCandidateMatches()
    {
        ConfigureGrid(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 2), TraversalMedium.Gas, "GasClosestDiagonal");

        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel center).Should().BeTrue();

        VolumeVoxelFinder.TryGetClosestTraversableVoxel(
            center,
            out Voxel closestNeighbor,
            Fixed64.One,
            TraversalMedium.Gas).Should().BeTrue();

        closestNeighbor.Should().NotBeNull();
        closestNeighbor.WorldPosition.Should().Be(new Vector3d(2, 0, 2));
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
