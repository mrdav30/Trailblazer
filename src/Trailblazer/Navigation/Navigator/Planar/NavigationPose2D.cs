//=======================================================================
// NavigationPose2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>Describes a native planar collider pose in world X/Z coordinates.</summary>
public readonly struct NavigationPose2D
{
    /// <summary>Creates a pose with rotation in the interval [-π, π).</summary>
    public NavigationPose2D(Vector2d center, Fixed64 rotationRadians)
    {
        Fixed64 angle = rotationRadians % Fixed64.TwoPi;
        if (angle >= Fixed64.Pi)
            angle -= Fixed64.TwoPi;
        if (angle < -Fixed64.Pi)
            angle += Fixed64.TwoPi;
        Center = center;
        RotationRadians = angle;
    }

    /// <summary>Gets the collider center in world X/Z.</summary>
    public Vector2d Center { get; }

    /// <summary>Gets the rotation from planar Right toward Forward in radians.</summary>
    public Fixed64 RotationRadians { get; }
}
