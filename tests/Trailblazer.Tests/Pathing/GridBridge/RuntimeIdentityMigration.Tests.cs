using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public sealed class RuntimeIdentityMigrationTests
{
    [Fact]
    public void FlowFieldSamplingGrid_ShouldPreserveLongWorldAndGridGenerations()
    {
        long worldGeneration = (long)int.MaxValue + 101;
        long gridGeneration = (long)int.MaxValue + 202;
        var sampleIndex = new WorldVoxelIndex(
            worldGeneration,
            7,
            gridGeneration,
            new VoxelIndex(1, 2, 3));
        var samplingGrid = new FlowFieldSamplingGrid(
            sampleIndex,
            Vector3d.Zero,
            Fixed64.One,
            1,
            2,
            3,
            1,
            2,
            3,
            1);

        samplingGrid.MatchesGrid(sampleIndex).Should().BeTrue();
        samplingGrid.MatchesGrid(new WorldVoxelIndex(
            worldGeneration,
            7,
            gridGeneration + 1,
            new VoxelIndex(1, 2, 3))).Should().BeFalse();
        samplingGrid.MatchesGrid(new WorldVoxelIndex(
            worldGeneration + 1,
            7,
            gridGeneration,
            new VoxelIndex(1, 2, 3))).Should().BeFalse();
    }

    [Fact]
    public void PendingExternalGridChange_ShouldPreserveLongGridGeneration()
    {
        long gridGeneration = (long)int.MaxValue + 303;

        var change = new PendingExternalGridChange(
            gridGeneration,
            1,
            Vector3d.Zero,
            Vector3d.One,
            requiresLiveGridTouchSelection: true,
            requiresAuthoredCellBoundsSelection: false);

        change.GridSpawnToken.Should().Be(gridGeneration);
    }
}
