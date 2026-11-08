//=======================================================================
// NavigationSeamEditTree.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Provides a seam-local immutable AVL with exact token-owned edit accounting.</summary>
internal sealed partial class NavigationSeamEditTree<TKey, TValue>
    where TKey : struct, IComparable<TKey>
    where TValue : class
{
    private readonly Node? _root;
    private readonly int _nodeBytes;

    internal NavigationSeamEditTree(int nodeBytes)
        : this(null, 0, nodeBytes)
    {
    }

    private NavigationSeamEditTree(Node? root, int count, int nodeBytes)
    {
        _root = root;
        Count = count;
        _nodeBytes = nodeBytes;
    }

    internal int Count { get; }

    internal long RetainedBytes => checked(32L + ((long)Count * _nodeBytes));

    internal int PersistentPageCount => checked(1 + Count);

    internal bool TryGetValue(TKey key, out TValue value)
    {
        Node? node = _root;
        while (node != null)
        {
            int comparison = key.CompareTo(node.Key);
            if (comparison == 0)
            {
                value = node.Value;
                return true;
            }
            node = comparison < 0 ? node.Left : node.Right;
        }
        value = null!;
        return false;
    }

    internal Editor Edit(NavigationSeamEditToken ownershipToken) => new(this, ownershipToken);

    internal Cursor CreateCursor(int maximumHeight, int shellBytes) =>
        new(maximumHeight, shellBytes);

    private static Node FindMinimum(Node node)
    {
        while (node.Left != null)
            node = node.Left;
        return node;
    }

    private static void Update(Node node)
    {
        node.Height = Math.Max(HeightOf(node.Left), HeightOf(node.Right)) + 1;
        node.Count = CountOf(node.Left) + CountOf(node.Right) + 1;
    }

    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private static int CountOf(Node? node) => node?.Count ?? 0;

    private sealed class Node
    {
        internal Node(
            TKey key,
            TValue value,
            Node? left,
            Node? right,
            long ownerToken)
        {
            Key = key;
            Value = value;
            Left = left;
            Right = right;
            OwnerToken = ownerToken;
            Update(this);
        }

        internal TKey Key;
        internal TValue Value;
        internal Node? Left;
        internal Node? Right;
        internal long OwnerToken;
        internal int Height;
        internal int Count;
    }
}
