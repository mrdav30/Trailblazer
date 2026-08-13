//=======================================================================
// NavigationStructuralComponent.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Stores an immutable flat or persistent-union weak structural component.</summary>
internal sealed class NavigationStructuralComponent
{
    private readonly string[]? _members;
    private readonly NavigationStructuralComponent? _left;
    private readonly NavigationStructuralComponent? _right;
    private readonly string _identityKey;

    private NavigationStructuralComponent(
        string key,
        string identityKey,
        long version,
        int memberCount,
        long retainedBytes,
        string[]? members,
        NavigationStructuralComponent? left,
        NavigationStructuralComponent? right)
    {
        Key = key;
        _identityKey = identityKey;
        Id = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(identityKey);
        Version = version;
        MemberCount = memberCount;
        RetainedBytes = retainedBytes;
        _members = members;
        _left = left;
        _right = right;
    }

    internal string Key { get; }

    internal int Id { get; }

    internal long Version { get; }

    internal int MemberCount { get; }

    internal long RetainedBytes { get; }

    internal string[]? FlatMembers => _members;

    internal NavigationStructuralComponent? Left => _left;

    internal NavigationStructuralComponent? Right => _right;

    internal string IdentityKey => _identityKey;

    internal static NavigationStructuralComponent CreateFlat(string[] members, long version)
    {
        string identityKey = members[0];
        for (int i = 1; i < members.Length; i++)
        {
            if (string.CompareOrdinal(members[i], identityKey) < 0)
                identityKey = members[i];
        }
        return new NavigationStructuralComponent(
            identityKey,
            identityKey,
            version,
            members.Length,
            checked(56L + ((long)members.Length * IntPtr.Size)),
            members,
            null,
            null);
    }

    internal static NavigationStructuralComponent Merge(
        NavigationStructuralComponent first,
        NavigationStructuralComponent second,
        long version)
    {
        NavigationStructuralComponent winner = first.MemberCount > second.MemberCount
            || (first.MemberCount == second.MemberCount
                && string.CompareOrdinal(first.Key, second.Key) <= 0)
            ? first
            : second;
        NavigationStructuralComponent loser = ReferenceEquals(winner, first) ? second : first;
        NavigationStructuralComponent left =
            string.CompareOrdinal(first._identityKey, second._identityKey) <= 0 ? first : second;
        NavigationStructuralComponent right = ReferenceEquals(left, first) ? second : first;
        string identityKey = string.CompareOrdinal(first._identityKey, second._identityKey) <= 0
            ? first._identityKey
            : second._identityKey;
        return new NavigationStructuralComponent(
            winner.Key,
            identityKey,
            version,
            checked(winner.MemberCount + loser.MemberCount),
            checked(56L + left.RetainedBytes + right.RetainedBytes),
            null,
            left,
            right);
    }

    internal NavigationStructuralComponent WithVersion(long version) => version == Version
        ? this
        : new NavigationStructuralComponent(
            Key,
            _identityKey,
            version,
            MemberCount,
            RetainedBytes,
            _members,
            _left,
            _right);

    internal void CopyMembersTo(string[] destination, ref int offset)
    {
        var stack = new NavigationStructuralComponent[MemberCount];
        int count = 1;
        stack[0] = this;
        while (count > 0)
        {
            NavigationStructuralComponent current = stack[--count];
            if (current._members != null)
            {
                Array.Copy(current._members, 0, destination, offset, current._members.Length);
                offset += current._members.Length;
                continue;
            }

            // Push right first so the original deterministic left-to-right order is preserved.
            stack[count++] = current._right!;
            stack[count++] = current._left!;
        }
    }

    internal void CollectTreeKeys(SwiftCollections.SwiftHashSet<string> keys)
    {
        var stack = new NavigationStructuralComponent[MemberCount];
        int count = 1;
        stack[0] = this;
        while (count > 0)
        {
            NavigationStructuralComponent current = stack[--count];
            keys.Add(current.Key);
            if (current._right != null)
                stack[count++] = current._right;
            if (current._left != null)
                stack[count++] = current._left;
        }
    }
}
