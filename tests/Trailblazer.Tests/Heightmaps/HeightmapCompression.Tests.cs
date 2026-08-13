using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Heightmaps;
using Xunit;

namespace Trailblazer.Tests.Heightmaps;

public sealed class HeightmapCompressionTests
{
    [Fact]
    public void Constructor_ShouldRejectNonPositiveHeightStep()
    {
        Action zero = () => new HeightmapCompression(Fixed64.Zero, Fixed64.Zero);
        Action negative = () => new HeightmapCompression(Fixed64.Zero, -Fixed64.One);

        zero.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("heightStep");
        negative.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("heightStep");
    }

    [Fact]
    public void Decompress_ShouldReturnReferenceHeight_ForZeroValue()
    {
        var compression = new HeightmapCompression((Fixed64)(-20), Fixed64.One / 4);

        compression.Decompress(0).Should().Be((Fixed64)(-20));
    }

    [Fact]
    public void Decompress_ShouldApplySignedStepOffset()
    {
        var compression = new HeightmapCompression((Fixed64)10, Fixed64.One / 2);

        compression.Decompress(6).Should().Be((Fixed64)13);
        compression.Decompress(-4).Should().Be((Fixed64)8);
    }

    [Fact]
    public void CompressClamped_ShouldClampOutsideRepresentableShortRange()
    {
        var compression = new HeightmapCompression(Fixed64.Zero, Fixed64.One);

        compression.CompressClamped((Fixed64)short.MaxValue + Fixed64.One).Should().Be(short.MaxValue);
        compression.CompressClamped((Fixed64)short.MinValue - Fixed64.One).Should().Be(short.MinValue);
    }

    [Fact]
    public void CompressClamped_ShouldQuantizeRelativeToReferenceHeight()
    {
        var compression = new HeightmapCompression((Fixed64)(-10), Fixed64.One / 4);

        compression.CompressClamped((Fixed64)(-9)).Should().Be(4);
        compression.CompressClamped((Fixed64)(-11)).Should().Be(-4);
    }
}
