using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation;

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
