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
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ChartRequestFactories_ShouldFailForInvalidEndpoints_WhenAllowUnwalkableIsFalse()
    {
        RegisterTwoPointChart("StrictChart");

        SolidVoxelFinder.TryGetPathEdgeVoxels(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out _,
            out _,
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeFalse();

        AStarPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeNull();

        FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: false).Should().BeNull();
    }

    [Fact]
    public void ChartRequestFactories_ShouldSnapInvalidEndpoints_WhenAllowUnwalkableIsTrue()
    {
        RegisterTwoPointChart("RelaxedChart");

        SolidVoxelFinder.TryGetPathEdgeVoxels(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            out Voxel? startVoxel,
            out Voxel? endVoxel,
            Fixed64.One,
            allowUnwalkableEndpoints: true).Should().BeTrue();

        TestRequire.NotNull(startVoxel).WorldPosition.Should().Be(Vector3d.Zero);
        TestRequire.NotNull(endVoxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        AStarPathRequest aStarRequest = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        TestRequire.NotNull(aStarRequest.StartNode).WorldPosition.Should().Be(Vector3d.Zero);
        TestRequire.NotNull(aStarRequest.EndNode).WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        TestRequire.NotNull(flowFieldRequest.StartNode).WorldPosition.Should().Be(Vector3d.Zero);
        TestRequire.NotNull(flowFieldRequest.EndNode).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void AStarRequest_ShouldResolveInvalidEndpointsConsistently_AcrossCreateUpdateAndSetters()
    {
        RegisterTwoPointChart("AStarConsistency");

        AStarPathRequest created = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        Voxel createdStart = TestRequire.NotNull(created.StartNode);
        Voxel createdEnd = TestRequire.NotNull(created.EndNode);
        createdStart.WorldPosition.Should().Be(Vector3d.Zero);
        createdEnd.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        AStarPathRequest updated = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));

        updated.UpdateRequest(new Vector3d(-1, 0, 0), new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        TestRequire.NotNull(updated.StartNode).WorldPosition.Should().Be(createdStart.WorldPosition);
        TestRequire.NotNull(updated.EndNode).WorldPosition.Should().Be(createdEnd.WorldPosition);

        AStarPathRequest setters = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));

        setters.TrySetOrigin(new Vector3d(-1, 0, 0)).Should().BeTrue();
        setters.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        TestRequire.NotNull(setters.StartNode).WorldPosition.Should().Be(createdStart.WorldPosition);
        TestRequire.NotNull(setters.EndNode).WorldPosition.Should().Be(createdEnd.WorldPosition);
    }

    [Fact]
    public void FlowFieldRequest_ShouldResolveInvalidEndpointsConsistently_AcrossCreateUpdateAndSetters()
    {
        RegisterTwoPointChart("FlowFieldConsistency");

        FlowFieldPathRequest created = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        Voxel createdStart = TestRequire.NotNull(created.StartNode);
        Voxel createdEnd = TestRequire.NotNull(created.EndNode);
        createdStart.WorldPosition.Should().Be(Vector3d.Zero);
        createdEnd.WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        FlowFieldPathRequest updated = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));

        updated.UpdateRequest(new Vector3d(-1, 0, 0), new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        TestRequire.NotNull(updated.StartNode).WorldPosition.Should().Be(createdStart.WorldPosition);
        TestRequire.NotNull(updated.EndNode).WorldPosition.Should().Be(createdEnd.WorldPosition);

        FlowFieldPathRequest setters = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));

        setters.TrySetOrigin(new Vector3d(-1, 0, 0)).Should().BeTrue();
        setters.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        TestRequire.NotNull(setters.StartNode).WorldPosition.Should().Be(createdStart.WorldPosition);
        TestRequire.NotNull(setters.EndNode).WorldPosition.Should().Be(createdEnd.WorldPosition);
    }

    [Fact]
    public void StarCast_ShouldBiasFallbackFromTheAnchorVoxelLocalOffset()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "StarCastLocalBias", new Vector3d(1, 0, 0));

        Fixed64 quarter = TestWorld.Context.VoxelSize / 4;
        Vector3d query = new(quarter * 3, Fixed64.Zero, quarter);

        SolidVoxelFinder.StarCast(TestWorld.Context, query, out Voxel? targetVoxel).Should().BeTrue();
        TestRequire.NotNull(targetVoxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void StarCast_ShouldReturnFalse_WhenQueryIsOutsideAnyGrid()
    {
        SolidVoxelFinder.StarCast(TestWorld.Context, new Vector3d(20, 0, 0), out Voxel? targetVoxel).Should().BeFalse();
        targetVoxel.Should().BeNull();
    }

    [Fact]
    public void StarCast_ShouldReturnFalse_WhenNoAlternativeVoxelExistsInsideGrid()
    {
        SolidVoxelFinder.StarCast(TestWorld.Context, Vector3d.Zero, out Voxel? targetVoxel, Fixed64.One).Should().BeFalse();
        targetVoxel.Should().BeNull();
    }

    [Fact]
    public void TryGetPathEdgeVoxels_ShouldUseDefaultUnitSize_WhenUnitSizeIsOmitted()
    {
        RegisterTwoPointChart("DefaultUnitSizePathEdges");

        SolidVoxelFinder.TryGetPathEdgeVoxels(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            out Voxel? originVoxel,
            out Voxel? targetVoxel,
            allowUnwalkableEndpoints: false).Should().BeTrue();

        TestRequire.NotNull(originVoxel).WorldPosition.Should().Be(Vector3d.Zero);
        TestRequire.NotNull(targetVoxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void GetStartAndEndVoxel_ShouldUseDefaultUnitSize_WhenUnitSizeIsOmitted()
    {
        RegisterTwoPointChart("DefaultUnitSizeEndpoints");

        SolidVoxelFinder.GetStartVoxel(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            out Voxel? originVoxel,
            allowUnwalkableEndpoints: false).Should().BeTrue();
        SolidVoxelFinder.GetEndVoxel(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            out Voxel? targetVoxel,
            allowUnwalkableEndpoints: false).Should().BeTrue();

        TestRequire.NotNull(originVoxel).WorldPosition.Should().Be(Vector3d.Zero);
        TestRequire.NotNull(targetVoxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void GetClosestVoxelForSize_ShouldResolveUsingPublicWrapper()
    {
        RegisterTwoPointChart("ClosestVoxelForSize");

        SolidVoxelFinder.GetClosestVoxelForSize(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out Voxel? targetVoxel,
            allowUnwalkableEndpoints: false).Should().BeTrue();

        TestRequire.NotNull(targetVoxel).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void GetClosestVoxelForSize_ShouldReturnFalse_WhenTargetIsOutsideAnyGrid()
    {
        RegisterTwoPointChart("ClosestVoxelForSizeOutsideGrid");

        SolidVoxelFinder.GetClosestVoxelForSize(TestWorld.Context, Vector3d.Zero,
            new Vector3d(20, 0, 0),
            Fixed64.One,
            out Voxel? targetVoxel,
            allowUnwalkableEndpoints: false).Should().BeFalse();

        targetVoxel.Should().BeNull();
    }

    [Fact]
    public void TryGetClosestWalkableVoxel_ShouldReturnFalse_WhenNoNeighborIsTraversable()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "NoNeighborWalkable", Vector3d.Zero);
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        SolidVoxelFinder.TryGetClosestWalkableVoxel(TestWorld.Context, voxel, out Voxel? closestNeighbor).Should().BeFalse();
        closestNeighbor.Should().BeNull();
    }

    private static void RegisterTwoPointChart(string chartName)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;
        PathTestFactory.RegisterFromData(TestWorld.Context, chartName, data, Vector3d.Zero);
    }
}
