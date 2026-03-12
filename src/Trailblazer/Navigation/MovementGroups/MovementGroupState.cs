using SwiftCollections;

namespace Trailblazer.Navigation.MovementGroups;

internal sealed class MovementGroupState
{
    public SwiftBucket<MovementGroupMember> Members { get; } = new();
}
