using FixedMathSharp;
using FluentAssertions;
using SwiftCollections.Dimensions;
using System;
using Trailblazer.Heightmaps;
using Xunit;

namespace Trailblazer.Tests.Heightmaps;

public sealed class HeightmapSurfaceTests
{
    [Fact]
    public void FromCompressed_ShouldRejectInvalidInputs()
    {
        var compression = new HeightmapCompression(Fixed64.Zero, Fixed64.One);
        var samples = new SwiftShortArray2D(1, 1);

        Action nullSamples = () => HeightmapSurface.FromCompressed(
            "NullSamples",
            null!,
            Vector3d.Zero,
            Fixed64.One,
            compression);
        Action zeroInterval = () => HeightmapSurface.FromCompressed(
            "ZeroInterval",
            samples,
            Vector3d.Zero,
            Fixed64.Zero,
            compression);
        Action negativeInterval = () => HeightmapSurface.FromCompressed(
            "NegativeInterval",
            samples,
            Vector3d.Zero,
            -Fixed64.One,
            compression);

        nullSamples.Should().Throw<ArgumentNullException>().WithParameterName("samples");
        zeroInterval.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("interval");
        negativeInterval.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("interval");
    }

    [Fact]
    public void FromHeights_ShouldRejectInvalidInputs()
    {
        var compression = new HeightmapCompression(Fixed64.Zero, Fixed64.One);

        Action nullHeights = () => HeightmapSurface.FromHeights(
            "NullHeights",
            null!,
            Vector3d.Zero,
            Fixed64.One,
            compression);
        Action zeroInterval = () => HeightmapSurface.FromHeights(
            "ZeroInterval",
            new Fixed64[1, 1],
            Vector3d.Zero,
            Fixed64.Zero,
            compression);

        nullHeights.Should().Throw<ArgumentNullException>().WithParameterName("heights");
        zeroInterval.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("interval");
    }

    [Fact]
    public void FromHeights_ShouldCompressIntoSameSamplingResult_AsCompressedSamples()
    {
        var compression = new HeightmapCompression(Fixed64.Zero, Fixed64.One / 2);
        var compressedSamples = new SwiftShortArray2D(2, 2);
        compressedSamples[0, 0] = 0;
        compressedSamples[1, 0] = 2;
        compressedSamples[0, 1] = 4;
        compressedSamples[1, 1] = 6;

        HeightmapSurface compressed = HeightmapSurface.FromCompressed(
            "Compressed",
            compressedSamples,
            Vector3d.Zero,
            Fixed64.One,
            compression);
        HeightmapSurface fromHeights = HeightmapSurface.FromHeights(
            "FromHeights",
            new Fixed64[2, 2]
            {
                { Fixed64.Zero, Fixed64.One },
                { (Fixed64)2, (Fixed64)3 }
            },
            Vector3d.Zero,
            Fixed64.One,
            compression);

        compressed.TrySampleGround(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Half), out Fixed64 compressedGroundY)
            .Should().BeTrue();
        fromHeights.TrySampleGround(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Half), out Fixed64 heightsGroundY)
            .Should().BeTrue();
        heightsGroundY.Should().Be(compressedGroundY);
    }

    [Fact]
    public void TrySampleGround_ShouldFailForNegativePositionOutsideMinBounds()
    {
        HeightmapSurface surface = CreateSurface(
            new short[2, 2]
            {
                { 1, 2 },
                { 3, 4 }
            });

        surface.TrySampleGround(new Vector3d(-Fixed64.Half, Fixed64.Zero, Fixed64.Half), out _).Should().BeFalse();
        surface.TrySampleGround(new Vector3d(Fixed64.Half, Fixed64.Zero, -Fixed64.Half), out _).Should().BeFalse();
    }

    [Fact]
    public void TrySampleGround_ShouldClampExactMaximumEdge_ToLastSample()
    {
        HeightmapSurface surface = CreateSurface(
            new short[2, 2]
            {
                { 1, 2 },
                { 3, 4 }
            });

        surface.TrySampleGround(new Vector3d(1, 0, 1), out Fixed64 groundY).Should().BeTrue();
        groundY.Should().Be((Fixed64)4);
    }

    [Fact]
    public void TrySampleGround_ShouldBilinearlyInterpolateFourNeighboringSamples()
    {
        HeightmapSurface surface = CreateSurface(
            new short[2, 2]
            {
                { 0, 10 },
                { 20, 30 }
            });

        surface.TrySampleGround(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Half), out Fixed64 groundY)
            .Should().BeTrue();

        groundY.Should().Be((Fixed64)15);
    }

    [Fact]
    public void TrySampleGround_ShouldInterpolateNonMidpointFixedPointFractions()
    {
        HeightmapSurface surface = CreateSurface(
            new short[2, 2]
            {
                { 0, 4 },
                { 8, 12 }
            });

        Vector3d samplePosition = new(Fixed64.Quarter, Fixed64.Zero, Fixed64.Half);

        surface.TrySampleGround(samplePosition, out Fixed64 groundY).Should().BeTrue();
        groundY.Should().Be((Fixed64)4);
    }

    private static HeightmapSurface CreateSurface(short[,] values)
    {
        return HeightmapSurface.FromCompressed(
            "TestSurface",
            new SwiftShortArray2D(values),
            Vector3d.Zero,
            Fixed64.One,
            new HeightmapCompression(Fixed64.Zero, Fixed64.One));
    }
}
