using System;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationAddressStampSetTests
{
    [Fact]
    public void AddContainsAndReset_ShouldProvideBoundedGenerationScopedDeduplication()
    {
        Action invalid = () => _ = new NavigationAddressStampSet(0);
        var set = new NavigationAddressStampSet(2);
        var first = new NavigationCellAddress("map", default);
        var second = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));

        invalid.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("capacity");
        set.Add(first).Should().BeTrue();
        set.Add(first).Should().BeFalse();
        set.Add(second).Should().BeTrue();
        set.Contains(first).Should().BeTrue();
        set.Contains(second).Should().BeTrue();

        set.Reset();

        set.Contains(first).Should().BeFalse();
        set.Contains(second).Should().BeFalse();
        set.Add(first).Should().BeTrue();
    }

    [Fact]
    public void GenerationAdvance_WhenIdentityIsExhausted_ShouldFailClosed()
    {
        Action advance = () => NavigationGenerationCounter.Advance(
            long.MaxValue,
            "Address stamp generation capacity is exhausted.");

        advance.Should().Throw<InvalidOperationException>()
            .WithMessage("Address stamp generation capacity is exhausted.");
    }
}
