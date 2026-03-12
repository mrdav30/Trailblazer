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
