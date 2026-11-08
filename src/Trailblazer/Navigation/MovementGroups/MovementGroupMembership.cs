//=======================================================================
// MovementGroupMembership.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>Tracks an occupant's current movement group and its most recent destination request.</summary>
internal sealed class MovementGroupMembership
{
    public int GroupId;

    public Vector3d RequestedDestination;

    public long LastSeenFrame;
}
