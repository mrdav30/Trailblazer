//=======================================================================
// MovementGroupMember.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
