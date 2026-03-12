using FixedMathSharp;
using System;

namespace Trailblazer.Navigation.MovementGroups;

internal sealed class MovementGroupMember
{
    public Guid OccupantId;

    public bool HasOccupantId;

    public Vector3d Position;

    public Fixed64 Radius;

    public Vector3d RequestedDestination;

    public Vector3d FormationOffset;

    public bool HasFormationOffset;

    public int LastSeenFrame;
}
