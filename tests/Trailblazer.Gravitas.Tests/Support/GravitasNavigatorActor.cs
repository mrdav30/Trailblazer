using System;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using Trailblazer.Navigation;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasNavigatorActor : IDisposable
{
    internal GravitasNavigatorActor(GravitasNavigator3D navigator, SolidBody body)
    {
        Navigator = navigator;
        Body = body;
    }

    internal GravitasNavigator3D Navigator { get; }

    internal SolidBody Body { get; }

    internal Vector3d NavigatorRequestedPosition { get; set; }

    internal Vector3d HostRequestedPosition { get; set; }

    internal LSCollider? PendingStepCollider { get; set; }

    internal Fixed64 PendingStepSurface { get; set; }

    public void Dispose()
    {
        Navigator.Reset();
        Body.Deactivate();
    }
}
