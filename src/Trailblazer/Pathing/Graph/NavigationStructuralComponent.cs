//=======================================================================
// NavigationStructuralComponent.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable flat weak structural component.</summary>
internal sealed class NavigationStructuralComponent
{
    private readonly NavigationPagedSequence<string> _members;

    private NavigationStructuralComponent(
        string key,
        long version,
        NavigationPagedSequence<string> members)
    {
        Key = key;
        Id = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(key);
        Version = version;
        _members = members;
        RetainedBytes = checked(48L + members.RetainedBytes);
    }

    internal string Key { get; }

    internal int Id { get; }

    internal long Version { get; }

    internal int MemberCount => _members.Count;

    internal long RetainedBytes { get; }

    internal int PersistentPageCount => _members.PersistentPageCount;

    internal NavigationPagedSequence<string> FlatMembers => _members;

    internal static NavigationStructuralComponent CreateFlat(
        NavigationPagedSequence<string> members,
        string identityKey,
        long version) => new(identityKey, version, members);
}
