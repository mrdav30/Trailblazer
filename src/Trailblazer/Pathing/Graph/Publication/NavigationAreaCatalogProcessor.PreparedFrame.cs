//=======================================================================
// NavigationAreaCatalogProcessor.PreparedFrame.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the PreparedFrame companion type.</content>
internal sealed partial class NavigationAreaCatalogProcessor
{
    /// <summary>Holds staged area-policy catalog changes until frame publication.</summary>
    internal readonly struct PreparedFrame
    {
        private readonly NavigationAreaCatalogProcessor _owner;
        private readonly NavigationOperationRejection[] _outcomes;

        internal PreparedFrame(
            NavigationAreaCatalogProcessor owner,
            NavigationAreaCatalog candidate,
            NavigationOperationRejection[] outcomes,
            int count)
        {
            _owner = owner;
            Candidate = candidate;
            _outcomes = outcomes;
            Count = count;
        }

        internal NavigationAreaCatalog Candidate { get; }

        internal int Count { get; }

        internal void Complete(long frame)
        {
            for (int i = 0; i < Count; i++)
            {
                NavigationAreaPolicyCommitOperation operation = _owner._pending[i];
                if (_outcomes[i] == NavigationOperationRejection.None)
                    operation.Receipt.CompleteApplied(frame);
                else
                    operation.Receipt.CompleteRejected(_outcomes[i]);
            }
            _owner.RemovePrefix(Count);
        }

        internal void CompleteCapacityRejected()
        {
            for (int i = 0; i < Count; i++)
            {
                NavigationAreaPolicyCommitOperation operation = _owner._pending[i];
                NavigationOperationRejection rejection = _outcomes[i] == NavigationOperationRejection.None
                    ? NavigationOperationRejection.CapacityExceeded
                    : _outcomes[i];
                operation.Receipt.CompleteRejected(rejection);
            }
            _owner.RemovePrefix(Count);
        }
    }
}
