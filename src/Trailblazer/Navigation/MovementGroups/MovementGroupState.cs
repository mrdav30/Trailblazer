//=======================================================================
// MovementGroupState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>Owns the active member slots for one movement group.</summary>
internal sealed class MovementGroupState
{
    public SwiftSlotMap<MovementGroupMember> Members { get; } = new();
}
