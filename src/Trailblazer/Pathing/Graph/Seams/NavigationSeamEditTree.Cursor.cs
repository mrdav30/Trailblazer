//=======================================================================
// NavigationSeamEditTree.Cursor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the cursor for ordered seam tree traversal.</content>
internal sealed partial class NavigationSeamEditTree<TKey, TValue>
{
    /// <summary>Traverses the seam tree in key order.</summary>
    internal sealed class Cursor
    {
        private readonly Node?[] _stack;
        private readonly int _shellBytes;
        private int _count;
        private TKey _maximum;
        private bool _hasMaximum;

        internal Cursor(int maximumHeight, int shellBytes)
        {
            ChronicleGuard.ThrowIfArgumentOutOfRange(
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
            ChronicleGuard.ThrowIfTrue(
                _count == _stack.Length,
                message: "The configured seam tree height was exceeded.");
            _stack[_count++] = node;
        }

        private void ClearStack()
        {
            while (_count != 0)
                _stack[--_count] = null;
        }
    }
}
