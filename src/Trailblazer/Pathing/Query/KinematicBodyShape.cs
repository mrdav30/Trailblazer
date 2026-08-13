//=======================================================================
// KinematicBodyShape.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using SwiftCollections.Diagnostics;
using SwiftCollections.Utility;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the authoritative deterministic body geometry used by navigation and movement.
/// </summary>
public readonly struct KinematicBodyShape : IEquatable<KinematicBodyShape>
{
    /// <summary>
    /// Gets the non-negative horizontal body radius.
    /// </summary>
    public Fixed64 Radius { get; }

    /// <summary>
    /// Gets the positive body height measured upward from the foot position.
    /// </summary>
    public Fixed64 Height { get; }

    /// <summary>
    /// Gets the non-negative vertical offset from the host root to its foot position.
    /// </summary>
    public Fixed64 RootToFootOffsetY { get; }

    /// <summary>
    /// Creates an immutable body shape.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is outside its valid range.</exception>
    public KinematicBodyShape(Fixed64 radius, Fixed64 height, Fixed64 rootToFootOffsetY)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            radius < Fixed64.Zero,
            actualValue: null,
            nameof(radius),
            "Body radius cannot be negative.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            height <= Fixed64.Zero,
            actualValue: null,
            nameof(height),
            "Body height must be positive.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            rootToFootOffsetY < Fixed64.Zero,
            actualValue: null,
            nameof(rootToFootOffsetY),
            "Root-to-foot offset cannot be negative.");

        Radius = radius;
        Height = height;
        RootToFootOffsetY = rootToFootOffsetY;
    }

    /// <inheritdoc/>
    public bool Equals(KinematicBodyShape other) =>
        Radius == other.Radius
        && Height == other.Height
        && RootToFootOffsetY == other.RootToFootOffsetY;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KinematicBodyShape other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(Radius.GetHashCode(), Height.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, RootToFootOffsetY.GetHashCode());
    }

    /// <summary>
    /// Returns whether two shapes have exactly equal dimensions.
    /// </summary>
    public static bool operator ==(KinematicBodyShape left, KinematicBodyShape right) => left.Equals(right);

    /// <summary>
    /// Returns whether two shapes have different dimensions.
    /// </summary>
    public static bool operator !=(KinematicBodyShape left, KinematicBodyShape right) => !left.Equals(right);

    internal void Validate(string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgument(
            Radius < Fixed64.Zero || Height <= Fixed64.Zero || RootToFootOffsetY < Fixed64.Zero,
            parameterName,
            "Body shape must have a non-negative radius, positive height, and non-negative root-to-foot offset.");
    }
}
