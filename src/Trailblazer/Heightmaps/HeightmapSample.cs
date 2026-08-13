//=======================================================================
// HeightmapSample.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Result of a deterministic heightmap ground sample.
/// </summary>
public readonly struct HeightmapSample
{
    /// <summary>
    /// Name of the registered layer that supplied the sample.
    /// </summary>
    public string LayerName { get; }

    /// <summary>
    /// World position used for the sample query.
    /// </summary>
    public Vector3d QueryPosition { get; }

    /// <summary>
    /// Environment ground/contact Y resolved from the layer.
    /// </summary>
    public Fixed64 GroundY { get; }

    /// <summary>
    /// Absolute distance between query contact Y and the sampled ground Y used for layer selection.
    /// </summary>
    public Fixed64 DistanceFromSelectionY { get; }

    /// <summary>
    /// Creates a new heightmap sample result.
    /// </summary>
    public HeightmapSample(
        string layerName,
        Vector3d queryPosition,
        Fixed64 groundY,
        Fixed64 distanceFromSelectionY)
    {
        LayerName = layerName;
        QueryPosition = queryPosition;
        GroundY = groundY;
        DistanceFromSelectionY = distanceFromSelectionY;
    }
}
