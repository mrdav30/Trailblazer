//=======================================================================
// NavigationSeamEditTree.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Provides a seam-local immutable AVL with exact token-owned edit accounting.</summary>
internal sealed class NavigationSeamEditTree<TKey, TValue>
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

    internal sealed class Editor
    {
        private readonly int _nodeBytes;
        private readonly long _token;
        private readonly NavigationSeamEditTree<TKey, TValue> _source;
        private Node? _root;
        private bool _sealed;

        internal Editor(
            NavigationSeamEditTree<TKey, TValue> source,
            NavigationSeamEditToken ownershipToken)
        {
            _root = source._root;
            _source = source;
            _nodeBytes = source._nodeBytes;
            _token = ownershipToken.Value;
            Count = source.Count;
        }

        internal int Count { get; private set; }

        internal int OwnedNodeCount { get; private set; }

        internal bool IsChanged { get; private set; }

        internal long RetainedBytes => checked(56L + ((long)OwnedNodeCount * _nodeBytes));

        internal int PersistentPageCount => checked(1 + OwnedNodeCount);

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

        internal void Set(TKey key, TValue value)
        {
            EnsureWritable();
            if (TryGetValue(key, out TValue prior) && ReferenceEquals(prior, value))
                return;
            bool added = false;
            _root = Set(_root, key, value, ref added);
            if (added)
                Count = checked(Count + 1);
            IsChanged = true;
        }

        internal bool Remove(TKey key)
        {
            EnsureWritable();
            if (!TryGetValue(key, out _))
                return false;
            bool removed = false;
            _root = Remove(_root, key, ref removed);
            if (removed)
                Count--;
            IsChanged |= removed;
            return removed;
        }

        internal NavigationSeamEditTree<TKey, TValue> Seal()
        {
            EnsureWritable();
            _sealed = true;
            return IsChanged
                ? new NavigationSeamEditTree<TKey, TValue>(_root, Count, _nodeBytes)
                : _source;
        }

        private Node Set(Node? node, TKey key, TValue value, ref bool added)
        {
            if (node == null)
            {
                added = true;
                return NewNode(key, value, null, null);
            }
            node = Own(node);
            int comparison = key.CompareTo(node.Key);
            if (comparison == 0)
            {
                node.Value = value;
                return node;
            }
            if (comparison < 0)
                node.Left = Set(node.Left, key, value, ref added);
            else
                node.Right = Set(node.Right, key, value, ref added);
            Update(node);
            return Balance(node);
        }

        private Node? Remove(Node? node, TKey key, ref bool removed)
        {
            if (node == null)
                return null;
            int comparison = key.CompareTo(node.Key);
            if (comparison == 0)
            {
                removed = true;
                if (node.Left == null || node.Right == null)
                {
                    Node? replacement = node.Left ?? node.Right;
                    ReleaseIfOwned(node);
                    return replacement;
                }
                node = Own(node);
                Node successor = FindMinimum(node.Right!);
                node.Key = successor.Key;
                node.Value = successor.Value;
                node.Right = RemoveMinimum(node.Right!);
                Update(node);
                return Balance(node);
            }
            node = Own(node);
            if (comparison < 0)
                node.Left = Remove(node.Left, key, ref removed);
            else
                node.Right = Remove(node.Right, key, ref removed);
            if (!removed)
                return node;
            Update(node);
            return Balance(node);
        }

        private Node? RemoveMinimum(Node node)
        {
            if (node.Left == null)
            {
                Node? replacement = node.Right;
                ReleaseIfOwned(node);
                return replacement;
            }
            node = Own(node);
            node.Left = RemoveMinimum(node.Left!);
            Update(node);
            return Balance(node);
        }

        private Node Balance(Node node)
        {
            int balance = HeightOf(node.Left) - HeightOf(node.Right);
            if (balance > 1)
            {
                node.Left = Own(node.Left!);
                if (HeightOf(node.Left.Left) < HeightOf(node.Left.Right))
                node.Left = RotateLeft(node.Left!);
                return RotateRight(node);
            }
            if (balance < -1)
            {
                node.Right = Own(node.Right!);
                if (HeightOf(node.Right.Right) < HeightOf(node.Right.Left))
                node.Right = RotateRight(node.Right!);
                return RotateLeft(node);
            }
            return node;
        }

        private Node RotateLeft(Node node)
        {
            Node right = Own(node.Right!);
            node.Right = right.Left;
            Update(node);
            right.Left = node;
            Update(right);
            return right;
        }

        private Node RotateRight(Node node)
        {
            Node left = Own(node.Left!);
            node.Left = left.Right;
            Update(node);
            left.Right = node;
            Update(left);
            return left;
        }

        private Node Own(Node node)
        {
            if (node.OwnerToken == _token)
                return node;
            return NewNode(node.Key, node.Value, node.Left, node.Right);
        }

        private Node NewNode(TKey key, TValue value, Node? left, Node? right)
        {
            OwnedNodeCount = checked(OwnedNodeCount + 1);
            return new Node(key, value, left, right, _token);
        }

        private void ReleaseIfOwned(Node node)
        {
            if (node.OwnerToken == _token)
                OwnedNodeCount--;
        }

        private void EnsureWritable()
        {
            if (_sealed)
                throw new InvalidOperationException("The seam edit session is already sealed.");
        }
    }

    internal sealed class Cursor
    {
        private readonly Node?[] _stack;
        private readonly int _shellBytes;
        private int _count;
        private TKey _maximum;
        private bool _hasMaximum;

        internal Cursor(int maximumHeight, int shellBytes)
        {
            SwiftThrowHelper.ThrowIfArgumentOutOfRange(
                maximumHeight <= 0,
                maximumHeight,
                nameof(maximumHeight));
            _stack = new Node?[maximumHeight];
            _shellBytes = shellBytes;
        }

        internal long RetainedBytes => checked(
            _shellBytes + 24L + ((long)_stack.Length * 8L));

        internal int PersistentPageCount => 2;

        internal TValue Current { get; private set; } = null!;

        internal TKey CurrentKey { get; private set; }

        internal bool HasNext => _count != 0;

        internal void Begin(
            NavigationSeamEditTree<TKey, TValue> tree,
            TKey minimum,
            TKey maximum)
        {
            ClearStack();
            _maximum = maximum;
            _hasMaximum = true;
            PushLowerBound(tree._root, minimum);
            Current = null!;
            CurrentKey = default;
        }

        internal void BeginAll(NavigationSeamEditTree<TKey, TValue> tree)
        {
            ClearStack();
            _hasMaximum = false;
            PushLeft(tree._root);
            Current = null!;
            CurrentKey = default;
        }

        internal void BeginAtLeast(
            NavigationSeamEditTree<TKey, TValue> tree,
            TKey minimum)
        {
            ClearStack();
            _hasMaximum = false;
            PushLowerBound(tree._root, minimum);
            Current = null!;
            CurrentKey = default;
        }

        internal bool MoveNext()
        {
            if (_count == 0)
            {
                Current = null!;
                CurrentKey = default;
                return false;
            }
            Node node = _stack[--_count]!;
            _stack[_count] = null;
            if (_hasMaximum && node.Key.CompareTo(_maximum) >= 0)
            {
                ClearStack();
                Current = null!;
                CurrentKey = default;
                return false;
            }
            PushLeft(node.Right);
            CurrentKey = node.Key;
            Current = node.Value;
            return true;
        }

        private void PushLowerBound(Node? node, TKey minimum)
        {
            while (node != null)
            {
                if (node.Key.CompareTo(minimum) < 0)
                {
                    node = node.Right;
                    continue;
                }
                Push(node);
                node = node.Left;
            }
        }

        private void PushLeft(Node? node)
        {
            while (node != null)
            {
                Push(node);
                node = node.Left;
            }
        }

        private void Push(Node node)
        {
            if (_count == _stack.Length)
                throw new InvalidOperationException("The configured seam tree height was exceeded.");
            _stack[_count++] = node;
        }

        private void ClearStack()
        {
            while (_count != 0)
                _stack[--_count] = null;
        }
    }

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
