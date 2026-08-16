using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

public sealed class RuntimeIdentityMigrationTests
{
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
