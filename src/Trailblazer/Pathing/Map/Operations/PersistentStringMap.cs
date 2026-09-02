//=======================================================================
// PersistentStringMap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Provides an ordinal string map whose updates copy only one balanced-tree path.</summary>
internal sealed class PersistentStringMap<T>
{
    private readonly Node? _root;

    internal static PersistentStringMap<T> Empty { get; } = new(null);

    private PersistentStringMap(Node? root)
    {
        _root = root;
    }

    internal int Count => CountOf(_root);

    internal int PersistentNodeCount => Count;

    internal long RetainedBytes => checked(32L + ((long)Count * 64L));

    internal bool ContainsKey(string key) => TryGetValue(key, out _);

    internal bool TryGetValue(string key, out T value)
    {
        Node? node = _root;
        while (node != null)
        {
            int comparison = string.CompareOrdinal(key, node.Key);
            if (comparison == 0)
            {
                value = node.Value;
                return true;
            }
            node = comparison < 0 ? node.Left : node.Right;
        }
        value = default!;
        return false;
    }

    internal PersistentStringMap<T> Set(string key, T value) =>
        new(Set(_root, key, value));

    internal PersistentStringMap<T> Set(string key, T value, out int copiedNodeCount)
    {
        copiedNodeCount = 0;
        return new PersistentStringMap<T>(Set(_root, key, value, ref copiedNodeCount));
    }

    internal PersistentStringMap<T> Remove(string key, out bool removed)
    {
        Node? root = Remove(_root, key, out removed);
        return removed ? new PersistentStringMap<T>(root) : this;
    }

    internal PersistentStringMap<T> Remove(
        string key,
        out bool removed,
        out int copiedNodeCount)
    {
        copiedNodeCount = 0;
        Node? root = Remove(_root, key, out removed, ref copiedNodeCount);
        return removed ? new PersistentStringMap<T>(root) : this;
    }

    internal T GetValueAt(int ordinal)
        => GetNodeAt(ordinal).Value;

    internal string GetKeyAt(int ordinal)
        => GetNodeAt(ordinal).Key;

    private Node GetNodeAt(int ordinal)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)ordinal >= (uint)Count,
            ordinal,
            nameof(ordinal));
        Node node = _root!;
        while (true)
        {
            int leftCount = CountOf(node.Left);
            if (ordinal < leftCount)
                node = node.Left!;
            else if (ordinal == leftCount)
                return node;
            else
            {
                ordinal -= leftCount + 1;
                node = node.Right!;
            }
        }
    }

    private static Node Set(Node? node, string key, T value)
    {
        if (node == null)
            return new Node(key, value, null, null);
        int comparison = string.CompareOrdinal(key, node.Key);
        if (comparison == 0)
            return new Node(key, value, node.Left, node.Right);
        return comparison < 0
            ? Balance(new Node(node.Key, node.Value, Set(node.Left, key, value), node.Right))
            : Balance(new Node(node.Key, node.Value, node.Left, Set(node.Right, key, value)));
    }

    private static Node Set(Node? node, string key, T value, ref int copiedNodeCount)
    {
        if (node == null)
            return NewNode(key, value, null, null, ref copiedNodeCount);
        int comparison = string.CompareOrdinal(key, node.Key);
        if (comparison == 0)
            return NewNode(key, value, node.Left, node.Right, ref copiedNodeCount);
        return comparison < 0
            ? Balance(
                NewNode(
                    node.Key,
                    node.Value,
                    Set(node.Left, key, value, ref copiedNodeCount),
                    node.Right,
                    ref copiedNodeCount),
                ref copiedNodeCount)
            : Balance(
                NewNode(
                    node.Key,
                    node.Value,
                    node.Left,
                    Set(node.Right, key, value, ref copiedNodeCount),
                    ref copiedNodeCount),
                ref copiedNodeCount);
    }

    private static Node? Remove(Node? node, string key, out bool removed)
    {
        if (node == null)
        {
            removed = false;
            return null;
        }
        int comparison = string.CompareOrdinal(key, node.Key);
        if (comparison < 0)
        {
            Node? left = Remove(node.Left, key, out removed);
            return removed ? Balance(new Node(node.Key, node.Value, left, node.Right)) : node;
        }
        if (comparison > 0)
        {
            Node? right = Remove(node.Right, key, out removed);
            return removed ? Balance(new Node(node.Key, node.Value, node.Left, right)) : node;
        }

        removed = true;
        if (node.Left == null)
            return node.Right;
        if (node.Right == null)
            return node.Left;
        Node successor = FindMinimum(node.Right);
        Node? successorRight = RemoveMinimum(node.Right);
        return Balance(new Node(successor.Key, successor.Value, node.Left, successorRight));
    }

    private static Node? Remove(
        Node? node,
        string key,
        out bool removed,
        ref int copiedNodeCount)
    {
        if (node == null)
        {
            removed = false;
            return null;
        }
        int comparison = string.CompareOrdinal(key, node.Key);
        if (comparison < 0)
        {
            Node? left = Remove(node.Left, key, out removed, ref copiedNodeCount);
            return removed
                ? Balance(
                    NewNode(node.Key, node.Value, left, node.Right, ref copiedNodeCount),
                    ref copiedNodeCount)
                : node;
        }
        if (comparison > 0)
        {
            Node? right = Remove(node.Right, key, out removed, ref copiedNodeCount);
            return removed
                ? Balance(
                    NewNode(node.Key, node.Value, node.Left, right, ref copiedNodeCount),
                    ref copiedNodeCount)
                : node;
        }

        removed = true;
        if (node.Left == null)
            return node.Right;
        if (node.Right == null)
            return node.Left;
        Node successor = FindMinimum(node.Right);
        Node? successorRight = RemoveMinimum(node.Right, ref copiedNodeCount);
        return Balance(
            NewNode(successor.Key, successor.Value, node.Left, successorRight, ref copiedNodeCount),
            ref copiedNodeCount);
    }

    private static Node FindMinimum(Node node)
    {
        while (node.Left != null)
            node = node.Left;
        return node;
    }

    private static Node? RemoveMinimum(Node node) => node.Left == null
        ? node.Right
        : Balance(new Node(node.Key, node.Value, RemoveMinimum(node.Left), node.Right));

    private static Node? RemoveMinimum(Node node, ref int copiedNodeCount) => node.Left == null
        ? node.Right
        : Balance(
            NewNode(
                node.Key,
                node.Value,
                RemoveMinimum(node.Left, ref copiedNodeCount),
                node.Right,
                ref copiedNodeCount),
            ref copiedNodeCount);

    private static Node Balance(Node node)
    {
        int balance = HeightOf(node.Left) - HeightOf(node.Right);
        if (balance > 1)
        {
            if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right))
                node = new Node(node.Key, node.Value, RotateLeft(node.Left), node.Right);
            return RotateRight(node);
        }
        if (balance < -1)
        {
            if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left))
                node = new Node(node.Key, node.Value, node.Left, RotateRight(node.Right));
            return RotateLeft(node);
        }
        return node;
    }

    private static Node Balance(Node node, ref int copiedNodeCount)
    {
        int balance = HeightOf(node.Left) - HeightOf(node.Right);
        if (balance > 1)
        {
            if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right))
            {
                node = NewNode(
                    node.Key,
                    node.Value,
                    RotateLeft(node.Left, ref copiedNodeCount),
                    node.Right,
                    ref copiedNodeCount);
            }
            return RotateRight(node, ref copiedNodeCount);
        }
        if (balance < -1)
        {
            if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left))
            {
                node = NewNode(
                    node.Key,
                    node.Value,
                    node.Left,
                    RotateRight(node.Right, ref copiedNodeCount),
                    ref copiedNodeCount);
            }
            return RotateLeft(node, ref copiedNodeCount);
        }
        return node;
    }

    private static Node RotateLeft(Node node)
    {
        Node right = node.Right!;
        return new Node(
            right.Key,
            right.Value,
            new Node(node.Key, node.Value, node.Left, right.Left),
            right.Right);
    }

    private static Node RotateRight(Node node)
    {
        Node left = node.Left!;
        return new Node(
            left.Key,
            left.Value,
            left.Left,
            new Node(node.Key, node.Value, left.Right, node.Right));
    }

    private static Node RotateLeft(Node node, ref int copiedNodeCount)
    {
        Node right = node.Right!;
        Node left = NewNode(
            node.Key,
            node.Value,
            node.Left,
            right.Left,
            ref copiedNodeCount);
        return NewNode(right.Key, right.Value, left, right.Right, ref copiedNodeCount);
    }

    private static Node RotateRight(Node node, ref int copiedNodeCount)
    {
        Node left = node.Left!;
        Node right = NewNode(
            node.Key,
            node.Value,
            left.Right,
            node.Right,
            ref copiedNodeCount);
        return NewNode(left.Key, left.Value, left.Left, right, ref copiedNodeCount);
    }

    private static Node NewNode(
        string key,
        T value,
        Node? left,
        Node? right,
        ref int copiedNodeCount)
    {
        copiedNodeCount = checked(copiedNodeCount + 1);
        return new Node(key, value, left, right);
    }

    private static int CountOf(Node? node) => node?.Count ?? 0;
    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private sealed class Node
    {
        internal Node(string key, T value, Node? left, Node? right)
        {
            Key = key;
            Value = value;
            Left = left;
            Right = right;
            Height = Math.Max(HeightOf(left), HeightOf(right)) + 1;
            Count = CountOf(left) + CountOf(right) + 1;
        }

        internal string Key { get; }
        internal T Value { get; }
        internal Node? Left { get; }
        internal Node? Right { get; }
        internal int Height { get; }
        internal int Count { get; }
    }
}
