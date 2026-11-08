//=======================================================================
// MovementGroupMember.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>Tracks an agent's position, requested destination, and formation offset within a movement group.</summary>
internal sealed class MovementGroupMember
{
    public Guid OccupantId;

    public bool HasOccupantId;

    public Vector3d Position;

    public Fixed64 Radius;

    public Vector3d RequestedDestination;

    public Vector3d FormationOffset;

    public bool HasFormationOffset;

    public long LastSeenFrame;
}
