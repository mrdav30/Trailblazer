//=======================================================================
// NavigationCarrier2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>Identifies a carrier registration and its witness anchor in the carrier's local frame.</summary>
public readonly struct NavigationCarrier2D
{
    /// <summary>Creates a binding to a positive host-stable ID and registration lifetime.</summary>
    public NavigationCarrier2D(int stableId, long lifetime, NavigationPose2D pose, Vector2d localAnchor)
    {
        ChronicleGuard.ThrowIfArgument(stableId <= 0 || lifetime <= 0, nameof(stableId));
        StableId = stableId;
        Lifetime = lifetime;
        Pose = pose;
        LocalAnchor = localAnchor;
    }
    /// <summary>Gets the positive host-stable carrier ID.</summary>
    public int StableId { get; }
    /// <summary>Gets the positive registration lifetime, distinct from the stable ID.</summary>
    public long Lifetime { get; }
    /// <summary>Gets the carrier pose at support sampling.</summary>
    public NavigationPose2D Pose { get; }
    /// <summary>Gets the physical witness in the carrier's local frame.</summary>
    public Vector2d LocalAnchor { get; }
}
