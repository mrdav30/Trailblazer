//=======================================================================
// NavigationSeamEditTree.Editor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Editor companion type.</content>
internal sealed partial class NavigationSeamEditTree<TKey, TValue>
{
    /// <summary>Applies staged edits to the persistent seam tree.</summary>
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
            _root = Remove(_root!, key);
            Count--;
            IsChanged = true;
            return true;
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

        private Node? Remove(Node node, TKey key)
        {
            int comparison = key.CompareTo(node.Key);
            if (comparison == 0)
            {
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
                node.Left = Remove(node.Left!, key);
            else
                node.Right = Remove(node.Right!, key);
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
            ChronicleGuard.ThrowIfTrue(
                _sealed,
                message: "The seam edit session is already sealed.");
        }
    }
}
