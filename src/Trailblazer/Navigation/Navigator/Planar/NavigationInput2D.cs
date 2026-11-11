//=======================================================================
// NavigationInput2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>Describes one frame of analog movement, jump permission, and optional facing intent.</summary>
public readonly struct NavigationInput2D
{
    /// <summary>Creates input. Null movement selects guidance; explicit movement replaces it.</summary>
    public NavigationInput2D(Vector2d? move, bool jumpRequested, Vector2d? facing = null, bool blockJump = false)
    {
        Move = move;
        JumpRequested = jumpRequested;
        Facing = facing;
        BlockJump = blockJump;
    }

    /// <summary>Gets movement of magnitude at most one, or null for guidance.</summary>
    public Vector2d? Move { get; }

    /// <summary>Gets the host's jump input edge.</summary>
    public bool JumpRequested { get; }

    /// <summary>Gets the optional nonzero facing override.</summary>
    public Vector2d? Facing { get; }

    /// <summary>Gets whether the host denies a jump this frame.</summary>
    public bool BlockJump { get; }
}
