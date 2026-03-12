using FixedMathSharp;

namespace Trailblazer.Navigation.MovementGroups;

internal sealed class MovementGroupMembership
{
    public int GroupId;

    public Vector3d RequestedDestination;

    public int LastSeenFrame;
}
