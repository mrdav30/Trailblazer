//=======================================================================
// GravitasContact.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation;

/// <summary>Carries translated Gravitas support geometry, friction, and platform motion for the navigator.</summary>
internal readonly struct GravitasContact
{
    internal GravitasContact(
        Fixed64 surfaceLevel,
        Vector3d surfaceNormal,
        Fixed64 surfaceFriction,
        PlatformSnapshot platform)
    {
        HasSupport = true;
        SurfaceLevel = surfaceLevel;
        SurfaceNormal = surfaceNormal;
        SurfaceFriction = surfaceFriction;
        Platform = platform;
    }

    internal bool HasSupport { get; }
    internal Fixed64 SurfaceLevel { get; }
    internal Vector3d SurfaceNormal { get; }
    internal Fixed64 SurfaceFriction { get; }
    internal PlatformSnapshot Platform { get; }
}
