//=======================================================================
// NavigationMotionRequest2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>Identifies the absolute target submitted for one fixed frame.</summary>
public readonly struct NavigationMotionRequest2D
{
    internal NavigationMotionRequest2D(long frame, NavigationPose2D target)
    {
        Frame = frame;
        Target = target;
    }

    /// <summary>Gets the context's frame number at preparation.</summary>
    public long Frame { get; }

    /// <summary>Gets the requested absolute pose.</summary>
    public NavigationPose2D Target { get; }
}
