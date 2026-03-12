using System;

namespace Trailblazer.Navigation.MovementGroups;

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
