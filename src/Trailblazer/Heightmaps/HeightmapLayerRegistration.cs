//=======================================================================
// HeightmapLayerRegistration.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Context-local registration metadata for one heightmap layer.
/// </summary>
public sealed class HeightmapLayerRegistration
{
    internal HeightmapLayerRegistration(
        HeightmapSurface surface,
        Fixed64 minSelectionY,
        Fixed64 maxSelectionY,
        int priority,
        int registrationOrder)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        LayerName = surface.Name;
        MinSelectionY = minSelectionY;
        MaxSelectionY = maxSelectionY;
        Priority = priority;
        RegistrationOrder = registrationOrder;
    }

    /// <summary>
    /// Stable registered layer name.
    /// </summary>
    public string LayerName { get; }

    /// <summary>
    /// Compressed surface data used by this layer.
    /// </summary>
    public HeightmapSurface Surface { get; }

    /// <summary>
    /// Inclusive lower contact-Y bound for selecting this layer.
    /// </summary>
    public Fixed64 MinSelectionY { get; }

    /// <summary>
    /// Exclusive upper contact-Y bound for selecting this layer.
    /// </summary>
    public Fixed64 MaxSelectionY { get; }

    /// <summary>
    /// Deterministic tie-break priority for overlapping layers.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Deterministic context-local registration order.
    /// </summary>
    public int RegistrationOrder { get; }

    internal bool ContainsSelectionY(Fixed64 contactY)
    {
        return contactY >= MinSelectionY && contactY < MaxSelectionY;
    }
}
