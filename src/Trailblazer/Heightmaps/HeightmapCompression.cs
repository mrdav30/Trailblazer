using FixedMathSharp;
using System;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Describes deterministic quantization for compact heightmap samples.
/// </summary>
public readonly struct HeightmapCompression
{
    /// <summary>
    /// Ground height represented by a compressed value of zero.
    /// </summary>
    public Fixed64 ReferenceHeight { get; }

    /// <summary>
    /// World-height delta represented by one compressed unit.
    /// </summary>
    public Fixed64 HeightStep { get; }

    /// <summary>
    /// Gets whether this compression metadata can be used for height conversion.
    /// </summary>
    public bool IsValid => HeightStep > Fixed64.Zero;

    /// <summary>
    /// Creates compression metadata for short-backed height samples.
    /// </summary>
    /// <param name="referenceHeight">Ground height represented by compressed value zero.</param>
    /// <param name="heightStep">Positive world-height delta represented by one compressed unit.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="heightStep"/> is not positive.</exception>
    public HeightmapCompression(Fixed64 referenceHeight, Fixed64 heightStep)
    {
        if (heightStep <= Fixed64.Zero)
            throw new ArgumentOutOfRangeException(nameof(heightStep), "Height step must be positive.");

        ReferenceHeight = referenceHeight;
        HeightStep = heightStep;
    }

    /// <summary>
    /// Converts a compressed sample into environment ground/contact Y.
    /// </summary>
    public Fixed64 Decompress(short compressed)
    {
        ThrowIfInvalid();
        return ReferenceHeight + compressed * HeightStep;
    }

    /// <summary>
    /// Quantizes a ground/contact Y value to the nearest representable short sample.
    /// </summary>
    public short CompressClamped(Fixed64 groundY)
    {
        ThrowIfInvalid();

        Fixed64 scaled = (groundY - ReferenceHeight) / HeightStep;
        if (scaled >= (Fixed64)short.MaxValue)
            return short.MaxValue;
        if (scaled <= (Fixed64)short.MinValue)
            return short.MinValue;

        return (short)scaled.RoundToInt();
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException("Heightmap compression requires a positive height step.");
    }
}
