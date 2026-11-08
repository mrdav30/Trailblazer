//=======================================================================
// MovementGroupSession.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>Stores one navigator's active movement group slot and owner identity.</summary>
internal sealed class MovementGroupSession
{
    public int GroupId = -1;

    public int GroupIndex = -1;

    public Guid OwnerId;

    public bool HasOwnerId;

    public void Reset()
    {
        GroupId = -1;
        GroupIndex = -1;
        OwnerId = Guid.Empty;
        HasOwnerId = false;
    }
}
