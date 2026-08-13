//=======================================================================
// MovementGroupTarget.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation.MovementGroups;

internal readonly struct MovementGroupTarget
{
    public MovementGroupTarget(MovementGroupTravelMode travelMode, Vector3d destination)
    {
        TravelMode = travelMode;
        Destination = destination;
    }

    public MovementGroupTravelMode TravelMode { get; }

    public Vector3d Destination { get; }
}
