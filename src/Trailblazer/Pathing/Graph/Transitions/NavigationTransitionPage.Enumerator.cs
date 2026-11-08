//=======================================================================
// NavigationTransitionPage.Enumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Enumerator companion type.</content>
internal sealed partial class NavigationTransitionPage
{
    /// <summary>Traverses the occupied transition entries in page order.</summary>
    internal struct Enumerator
    {
        private readonly NavigationWorldGraph? _graph;
        private readonly NavigationMediumStateRef _state;
        private readonly NavigationPublishedTransition[]? _outgoing;
        private readonly NavigationIncomingTransitionRef[]? _incoming;
        private readonly bool _activeOnly;
        private int _index;

        internal Enumerator(
            NavigationWorldGraph graph,
            NavigationMediumStateRef state,
            NavigationPublishedTransition[]? outgoing,
            NavigationIncomingTransitionRef[]? incoming,
            bool activeOnly)
        {
            _graph = graph;
            _state = state;
            _outgoing = outgoing;
            _incoming = incoming;
            _activeOnly = activeOnly;
            _index = 0;
            Current = default;
        }

        internal NavigationPublishedTransition Current { get; private set; }

        internal bool MoveNext()
        {
            if (_outgoing != null)
            {
                while (_index < _outgoing.Length)
                {
                    NavigationPublishedTransition candidate = _outgoing[_index++];
                    if (_activeOnly
                        ? _graph!.IsTransitionActive(candidate, _state, outgoing: true)
                        : _graph!.IsTransitionEndpoint(candidate, _state, outgoing: true))
                    {
                        Current = candidate;
                        return true;
                    }
                }
                return false;
            }
            while (_incoming != null && _index < _incoming.Length)
            {
                NavigationIncomingTransitionRef candidate = _incoming[_index++];
                NavigationPublishedTransition record =
                    _graph!.GetPublishedTransition(candidate);
                if (_activeOnly
                        ? _graph.IsTransitionActive(record, _state, outgoing: false)
                        : _graph.IsTransitionEndpoint(record, _state, outgoing: false))
                {
                    Current = record;
                    return true;
                }
            }
            Current = default;
            return false;
        }
    }
}
