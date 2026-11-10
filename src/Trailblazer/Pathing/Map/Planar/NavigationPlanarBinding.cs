//=======================================================================
// NavigationPlanarBinding.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies the single grid layer and world Y embedding of a planar map.</summary>
public readonly struct NavigationPlanarBinding : IEquatable<NavigationPlanarBinding>
{
    internal NavigationPlanarBinding(NavigationMode2D mode, int layerIndex, Fixed64 embeddingY)
    {
        Mode = mode;
        LayerIndex = layerIndex;
        EmbeddingY = embeddingY;
    }

    /// <summary>Gets the planar map's motion mode.</summary>
    public NavigationMode2D Mode { get; }

    /// <summary>Gets the topology-local index of the selected layer.</summary>
    public int LayerIndex { get; }

    /// <summary>Gets the world Y coordinate used to embed the navigation plane.</summary>
    public Fixed64 EmbeddingY { get; }

    /// <inheritdoc/>
    public bool Equals(NavigationPlanarBinding other) =>
        Mode == other.Mode && LayerIndex == other.LayerIndex && EmbeddingY == other.EmbeddingY;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationPlanarBinding other && Equals(other);

    /// <summary>
    /// Determines whether two planar bindings are equal.
    /// </summary>
    public static bool operator ==(NavigationPlanarBinding left, NavigationPlanarBinding right) =>
        left.Equals(right);

    /// <summary>
    /// Determines whether two planar bindings are not equal.
    /// </summary>
    public static bool operator !=(NavigationPlanarBinding left, NavigationPlanarBinding right) =>
        !(left == right);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(
            SwiftHashTools.CombineHashCodes((int)Mode, LayerIndex), EmbeddingY.GetHashCode());
}
