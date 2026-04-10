using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class SolidVoxelFinderTests : IDisposable
{
    public SolidVoxelFinderTests()
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
    public void ChartRequestFactories_ShouldFailForInvalidEndpoints_WhenAllowUnwalkableIsFalse()
    {
        RegisterTwoPointChart("StrictChart");

        SolidVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out _,
            out _,
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeFalse();

        AStarPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeNull();

        FlowFieldPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeNull();
    }

    [Fact]
    public void ChartRequestFactories_ShouldSnapInvalidEndpoints_WhenAllowUnwalkableIsTrue()
    {
        RegisterTwoPointChart("RelaxedChart");

        SolidVoxelFinder.TryGetPathEdgeVoxels(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out Voxel startVoxel,
            out Voxel endVoxel,
            Fixed64.One,
            allowUnwalkableEndpoints: true).Should().BeTrue();

        startVoxel.WorldPosition.Should().Be(Vector3d.Zero);
        endVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        AStarPathRequest aStarRequest = AStarPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        aStarRequest.Should().NotBeNull();
        aStarRequest.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        aStarRequest.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        FlowFieldPathRequest flowFieldRequest = FlowFieldPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        flowFieldRequest.Should().NotBeNull();
        flowFieldRequest.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        flowFieldRequest.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void AStarRequest_ShouldResolveInvalidEndpointsConsistently_AcrossCreateUpdateAndSetters()
    {
        RegisterTwoPointChart("AStarConsistency");

        AStarPathRequest created = AStarPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        created.Should().NotBeNull();
        created.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        created.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        AStarPathRequest updated = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        updated.Should().NotBeNull();

        updated.UpdateRequest(new Vector3d(-1, 0, 0), new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        updated.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        updated.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);

        AStarPathRequest setters = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        setters.Should().NotBeNull();

        setters.TrySetOrigin(new Vector3d(-1, 0, 0)).Should().BeTrue();
        setters.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        setters.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        setters.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);
    }

    [Fact]
    public void FlowFieldRequest_ShouldResolveInvalidEndpointsConsistently_AcrossCreateUpdateAndSetters()
    {
        RegisterTwoPointChart("FlowFieldConsistency");

        FlowFieldPathRequest created = FlowFieldPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        created.Should().NotBeNull();
        created.StartNode.WorldPosition.Should().Be(Vector3d.Zero);
        created.EndNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        FlowFieldPathRequest updated = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        updated.Should().NotBeNull();

        updated.UpdateRequest(new Vector3d(-1, 0, 0), new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        updated.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        updated.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);

        FlowFieldPathRequest setters = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        setters.Should().NotBeNull();

        setters.TrySetOrigin(new Vector3d(-1, 0, 0)).Should().BeTrue();
        setters.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        setters.StartNode.WorldPosition.Should().Be(created.StartNode.WorldPosition);
        setters.EndNode.WorldPosition.Should().Be(created.EndNode.WorldPosition);
    }

    [Fact]
    public void StarCast_ShouldBiasFallbackFromTheAnchorVoxelLocalOffset()
    {
        PathTestFactory.RegisterSingleWalkablePoint("StarCastLocalBias", new Vector3d(1, 0, 0));

        Fixed64 quarter = GlobalGridManager.VoxelSize / 4;
        Vector3d query = new(quarter * 3, Fixed64.Zero, quarter);

        SolidVoxelFinder.StarCast(query, out Voxel targetVoxel).Should().BeTrue();
        targetVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void StarCast_ShouldReturnFalse_WhenQueryIsOutsideAnyGrid()
    {
        SolidVoxelFinder.StarCast(new Vector3d(20, 0, 0), out Voxel targetVoxel).Should().BeFalse();
        targetVoxel.Should().BeNull();
    }

    [Fact]
    public void GetClosestVoxelForSize_ShouldResolveUsingPublicWrapper()
    {
        RegisterTwoPointChart("ClosestVoxelForSize");

        SolidVoxelFinder.GetClosestVoxelForSize(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out Voxel targetVoxel,
            allowUnwalkableEndpoints: false).Should().BeTrue();

        targetVoxel.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void TryGetClosestWalkableVoxel_ShouldReturnFalse_WhenNoNeighborIsTraversable()
    {
        PathTestFactory.RegisterSingleWalkablePoint("NoNeighborWalkable", Vector3d.Zero);
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel voxel).Should().BeTrue();

        SolidVoxelFinder.TryGetClosestWalkableVoxel(voxel, out Voxel closestNeighbor).Should().BeFalse();
    }

    private static void RegisterTwoPointChart(string chartName)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;
        PathTestFactory.RegisterFromData(chartName, data, Vector3d.Zero);
    }
}
