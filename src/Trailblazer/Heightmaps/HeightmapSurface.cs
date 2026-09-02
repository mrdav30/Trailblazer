//=======================================================================
// HeightmapSurface.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Dimensions;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Immutable compressed X/Z lattice that resolves environment ground/contact Y.
/// </summary>
public sealed class HeightmapSurface
{
    private readonly SwiftShortArray2D _samples;

    private HeightmapSurface(
        string name,
        SwiftShortArray2D samples,
        Vector3d minBounds,
        Fixed64 interval,
        HeightmapCompression compression)
    {
        Name = name;
        _samples = samples;
        MinBounds = minBounds;
        Interval = interval;
        Compression = compression;
        Width = samples.Width;
        Depth = samples.Height;
        MaxBounds = new Vector3d(
            minBounds.X + (Width - 1) * interval,
            minBounds.Y,
            minBounds.Z + (Depth - 1) * interval);
    }

    /// <summary>
    /// Stable authored surface name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Minimum world-space X/Z sample coordinate. The Y value is descriptive metadata only.
    /// </summary>
    public Vector3d MinBounds { get; }

    /// <summary>
    /// Maximum inclusive world-space X/Z sample coordinate. The Y value matches <see cref="MinBounds"/>.
    /// </summary>
    public Vector3d MaxBounds { get; }

    /// <summary>
    /// Distance between adjacent height samples along X and Z.
    /// </summary>
    public Fixed64 Interval { get; }

    /// <summary>
    /// Number of samples along X.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Number of samples along Z.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Compression metadata used by all stored samples.
    /// </summary>
    public HeightmapCompression Compression { get; }

    /// <summary>
    /// Creates a heightmap from already compressed baked samples. This is the preferred runtime
    /// construction path because it avoids setup-time quantization work.
    /// </summary>
    /// <param name="name">Stable authored surface name.</param>
    /// <param name="samples">Compressed X/Z samples indexed by X, then Z.</param>
    /// <param name="minBounds">Minimum world-space X/Z sample coordinate.</param>
    /// <param name="interval">Positive distance between adjacent samples.</param>
    /// <param name="compression">Compression metadata used to decompress samples.</param>
    public static HeightmapSurface FromCompressed(
        string name,
        SwiftShortArray2D samples,
        Vector3d minBounds,
        Fixed64 interval,
        HeightmapCompression compression)
    {
        ValidateName(name);
        ValidateSamples(samples);
        ValidateInterval(interval);
        ValidateCompression(compression);

        SwiftShortArray2D copy = CopySamples(samples);
        return new HeightmapSurface(name, copy, minBounds, interval, compression);
    }

    /// <summary>
    /// Creates a heightmap from fixed-point heights by quantizing them once into compressed storage.
    /// Use this for tests, generated data, or host tooling that already owns fixed-point heights;
    /// runtime sampling still uses the same compressed representation as <see cref="FromCompressed"/>.
    /// </summary>
    /// <param name="name">Stable authored surface name.</param>
    /// <param name="heights">Ground/contact Y samples indexed by X, then Z.</param>
    /// <param name="minBounds">Minimum world-space X/Z sample coordinate.</param>
    /// <param name="interval">Positive distance between adjacent samples.</param>
    /// <param name="compression">Compression metadata used to quantize samples.</param>
    public static HeightmapSurface FromHeights(
        string name,
        Fixed64[,] heights,
        Vector3d minBounds,
        Fixed64 interval,
        HeightmapCompression compression)
    {
        ValidateName(name);
        SwiftThrowHelper.ThrowIfNull(heights, nameof(heights));
        ValidateInterval(interval);
        ValidateCompression(compression);

        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        SwiftThrowHelper.ThrowIfArgument(
            width <= 0 || depth <= 0,
            paramName: nameof(heights),
            message: "Height samples must have positive width and depth.");

        var compressed = new SwiftShortArray2D(width, depth);
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                compressed[x, z] = compression.CompressClamped(heights[x, z]);

        return new HeightmapSurface(name, compressed, minBounds, interval, compression);
    }

    /// <summary>
    /// Attempts to sample environment ground/contact Y at the supplied world X/Z coordinate.
    /// </summary>
    public bool TrySampleGround(Vector3d worldPosition, out Fixed64 groundY)
    {
        if (!TryResolveLocalPosition(worldPosition, out int x0, out int z0, out int x1, out int z1, out Fixed64 fractionX, out Fixed64 fractionZ))
        {
            groundY = Fixed64.Zero;
            return false;
        }

        Fixed64 h00 = GetHeight(x0, z0);
        Fixed64 h10 = GetHeight(x1, z0);
        Fixed64 h01 = GetHeight(x0, z1);
        Fixed64 h11 = GetHeight(x1, z1);

        Fixed64 xLerp0 = FixedMath.Lerp(h00, h10, fractionX);
        Fixed64 xLerp1 = FixedMath.Lerp(h01, h11, fractionX);
        groundY = FixedMath.Lerp(xLerp0, xLerp1, fractionZ);
        return true;
    }

    private bool TryResolveLocalPosition(
        Vector3d worldPosition,
        out int x0,
        out int z0,
        out int x1,
        out int z1,
        out Fixed64 fractionX,
        out Fixed64 fractionZ)
    {
        Fixed64 localX = (worldPosition.X - MinBounds.X) / Interval;
        Fixed64 localZ = (worldPosition.Z - MinBounds.Z) / Interval;
        Fixed64 maxX = (Fixed64)(Width - 1);
        Fixed64 maxZ = (Fixed64)(Depth - 1);
        if (localX < Fixed64.Zero || localZ < Fixed64.Zero || localX > maxX || localZ > maxZ)
        {
            x0 = z0 = x1 = z1 = -1;
            fractionX = fractionZ = Fixed64.Zero;
            return false;
        }

        x0 = localX.FloorToInt();
        z0 = localZ.FloorToInt();
        x1 = Math.Min(x0 + 1, Width - 1);
        z1 = Math.Min(z0 + 1, Depth - 1);
        fractionX = localX - (Fixed64)x0;
        fractionZ = localZ - (Fixed64)z0;
        return true;
    }

    private Fixed64 GetHeight(int x, int z)
    {
        return Compression.Decompress(_samples[x, z]);
    }

    private static void ValidateName(string name)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(name),
            paramName: nameof(name),
            message: "Heightmap surface name cannot be null or whitespace.");
    }

    private static void ValidateSamples(SwiftShortArray2D samples)
    {
        SwiftThrowHelper.ThrowIfNull(samples, nameof(samples));
        SwiftThrowHelper.ThrowIfArgument(
            samples.Width <= 0 || samples.Height <= 0,
            paramName: nameof(samples),
            message: "Height samples must have positive width and depth.");
    }

    private static void ValidateInterval(Fixed64 interval)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            interval <= Fixed64.Zero,
            actualValue: null,
            paramName: nameof(interval),
            message: "Heightmap interval must be positive.");
    }

    private static void ValidateCompression(HeightmapCompression compression)
    {
        SwiftThrowHelper.ThrowIfArgument(
            !compression.IsValid,
            paramName: nameof(compression),
            message: "Heightmap compression requires a positive height step.");
    }

    private static SwiftShortArray2D CopySamples(SwiftShortArray2D source)
    {
        var copy = new SwiftShortArray2D(source.Width, source.Height);
        for (int x = 0; x < source.Width; x++)
            for (int z = 0; z < source.Height; z++)
                copy[x, z] = source[x, z];

        return copy;
    }
}
