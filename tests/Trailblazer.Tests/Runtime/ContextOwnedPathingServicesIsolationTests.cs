using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Worlds;

[Collection("PathingCollection")]
public sealed class ContextOwnedPathingServicesIsolationTests : IDisposable
{
    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ContextTransitions_ShouldAllowSameTransitionIdInSeparateWorlds()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "SharedTransitionChart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSolidLine(contextB, "SharedTransitionChart", Vector3d.Zero, 2);

        TraversalTransition transitionA = PathTestFactory.CreateJumpTransition(contextA, "shared-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        TraversalTransition transitionB = PathTestFactory.CreateJumpTransition(contextB, "shared-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        int contextBVersionBefore = contextB.Transitions.RegistryVersion;

        contextA.Transitions.Register(transitionA).Should().BeTrue();
        contextB.Transitions.RegistryVersion.Should().Be(contextBVersionBefore);
        contextB.Transitions.Register(transitionB).Should().BeTrue();

        contextA.Transitions.IsRegistered("shared-transition").Should().BeTrue();
        contextB.Transitions.IsRegistered("shared-transition").Should().BeTrue();

        contextA.Transitions.Unregister("shared-transition").Should().BeTrue();

        contextA.Transitions.IsRegistered("shared-transition").Should().BeFalse();
        contextB.Transitions.IsRegistered("shared-transition").Should().BeTrue();
        contextB.Transitions.IsActive("shared-transition").Should().BeTrue();
    }

    [Fact]
    public void ContextTransitionQueryCaches_ShouldStayWorldLocalForEquivalentGridIndices()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "WorldATransitionQueryChart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSolidLine(contextB, "WorldBTransitionQueryChart", Vector3d.Zero, 2);
        TraversalTransition transitionA = PathTestFactory.CreateJumpTransition(contextA, "world-a-transition", Vector3d.Zero, new Vector3d(1, 0, 0));
        Voxel contextAVoxel = PathTestFactory.RequireVoxel(contextA, Vector3d.Zero);
        Voxel contextBVoxel = PathTestFactory.RequireVoxel(contextB, Vector3d.Zero);
        contextAVoxel.GridIndex.Should().Be(contextBVoxel.GridIndex);

        contextA.Transitions.Register(transitionA).Should().BeTrue();

        contextA.Transitions.GetDirectedTransitionsFromSourceGrid(contextAVoxel.GridIndex)
            .Should()
            .ContainSingle(transition => transition.Id == "world-a-transition");
        contextB.Transitions.GetDirectedTransitionsFromSourceGrid(contextBVoxel.GridIndex)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ContextVolumeRules_ShouldInvalidateOnlyOwningVolumeGuideCache()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterVolumeLine(contextA, Vector3d.Zero, TraversalMedium.Gas, 2, "SharedVolumeChart");
        PathTestFactory.RegisterVolumeLine(contextB, Vector3d.Zero, TraversalMedium.Gas, 2, "SharedVolumeChart");
        VolumePathRequest requestA = TestRequire.NotNull(VolumePathRequest.Create(
            contextA, Vector3d.Zero, Vector3d.Right, Fixed64.One, medium: TraversalMedium.Gas));
        VolumePathRequest requestB = TestRequire.NotNull(VolumePathRequest.Create(
            contextB, Vector3d.Zero, Vector3d.Right, Fixed64.One, medium: TraversalMedium.Gas));
        contextA.Guides.RequestGuide(requestA, out VolumeGuide? guideA).Should().BeTrue();
        contextB.Guides.RequestGuide(requestB, out VolumeGuide? guideB).Should().BeTrue();
        contextA.Guides.ReturnGuide(guideA);
        contextB.Guides.ReturnGuide(guideB);

        contextA.VolumeRules.SetGasVoxelRule(static _ => true);

        contextA.VolumeRules.HasGasVoxelRule.Should().BeTrue();
        contextB.VolumeRules.HasGasVoxelRule.Should().BeFalse();
        contextA.Guides.TotalVolumeGuideCount.Should().Be(0);
        contextB.Guides.TotalVolumeGuideCount.Should().Be(1);
    }

    [Fact]
    public void ContextFlowGuides_ShouldInvalidateOnlyOwningGuideCaches()
    {
        using TrailblazerWorldContext contextA = PathTestFactory.CreateContextWithGrid();
        using TrailblazerWorldContext contextB = PathTestFactory.CreateContextWithGrid();
        PathTestFactory.RegisterSolidLine(contextA, "SharedGuideChart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSolidLine(contextB, "SharedGuideChart", Vector3d.Zero, 2);
        FlowFieldPathRequest requestA = TestRequire.NotNull(FlowFieldPathRequest.Create(
            contextA, Vector3d.Zero, Vector3d.Right, Fixed64.One));
        FlowFieldPathRequest requestB = TestRequire.NotNull(FlowFieldPathRequest.Create(
            contextB, Vector3d.Zero, Vector3d.Right, Fixed64.One));
        contextA.Guides.RequestGuide(requestA, out FlowFieldGuide? guideA).Should().BeTrue();
        contextB.Guides.RequestGuide(requestB, out FlowFieldGuide? guideB).Should().BeTrue();
        contextA.Guides.ReturnGuide(guideA);
        contextB.Guides.ReturnGuide(guideB);

        contextA.Guides.InvalidateCacheFor("SharedGuideChart");

        contextA.Guides.TotalFlowGuideCount.Should().Be(0);
        contextB.Guides.TotalFlowGuideCount.Should().Be(1);
    }

}
