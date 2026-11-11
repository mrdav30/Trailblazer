//=======================================================================
// NavigationSupport2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>Describes a physical witness at the accepted pose, independently of grounding policy.</summary>
public readonly struct NavigationSupport2D
{
    /// <summary>Creates a support sample with a unit normal and non-negative friction.</summary>
    public NavigationSupport2D(Vector2d point, Vector2d normal, Fixed64 friction, NavigationCarrier2D? carrier = null)
    {
        Point = point;
        Normal = normal;
        Friction = friction;
        Carrier = carrier;
        Validate();
    }
    /// <summary>Gets the physical witness in world X/Z.</summary>
    public Vector2d Point { get; }
    /// <summary>Gets the unit surface normal in world X/Z.</summary>
    public Vector2d Normal { get; }
    /// <summary>Gets the non-negative host material friction.</summary>
    public Fixed64 Friction { get; }
    /// <summary>Gets the carrier binding, if the witness belongs to one.</summary>
    public NavigationCarrier2D? Carrier { get; }

    internal void Validate()
    {
        // Bound components before the fixed-point unit-length check.
        ChronicleGuard.ThrowIfArgument(Normal.X < -Fixed64.One || Normal.X > Fixed64.One
            || Normal.Y < -Fixed64.One || Normal.Y > Fixed64.One
            || !Normal.IsNormalized() || Friction < Fixed64.Zero, nameof(Normal));
        if (Carrier is NavigationCarrier2D carrier)
            ChronicleGuard.ThrowIfArgument(carrier.StableId <= 0 || carrier.Lifetime <= 0, nameof(Carrier));
    }
}
