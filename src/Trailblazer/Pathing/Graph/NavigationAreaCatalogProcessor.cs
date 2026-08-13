//=======================================================================
// NavigationAreaCatalogProcessor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace Trailblazer.Pathing;

internal sealed class NavigationAreaCatalogProcessor
{
    private readonly SwiftQueue<NavigationAreaPolicyCommitOperation> _pending = new();
    private readonly int _maxPending;
    private readonly int _maxBatchItems;
    private readonly int _maxPolicies;
    private readonly int _maxRulesPerPolicy;
    private readonly int _maxRules;
    private readonly int _navigationAreaCount;
    private readonly long _maxPendingBytes;
    private readonly NavigationOperationRejection[] _outcomes;
    private int _pendingRuleCount;
    private long _pendingRetainedBytes;
    private long _sequenceHighWater;
    private int _effectiveFrameHighWater = -1;
    private int _lastFrame = -1;

    internal NavigationAreaCatalogProcessor(TrailblazerWorldContextSettings settings)
    {
        _maxPending = settings.OperationLimits.MaxPendingOperations;
        _maxBatchItems = settings.OperationLimits.MaxBatchItems;
        _maxPolicies = settings.MaxAreaPolicies;
        _maxRulesPerPolicy = settings.MaxAreaRulesPerPolicy;
        _maxRules = settings.MaxAreaRules;
        _navigationAreaCount = settings.NavigationAreaCount;
        long maximumRuleBytes = (long)settings.MaxAreaRules * 24L;
        _maxPendingBytes = settings.OperationLimits.MaxPendingDescriptorBytes
            > long.MaxValue - maximumRuleBytes
            ? long.MaxValue
            : settings.OperationLimits.MaxPendingDescriptorBytes + maximumRuleBytes;
        _outcomes = new NavigationOperationRejection[_maxPending];
    }

    internal int PendingCount => _pending.Count;

    internal int PendingRuleCount => _pendingRuleCount;

    internal long PendingRetainedBytes => _pendingRetainedBytes;

    internal void Reset()
    {
        for (int i = 0; i < _pending.Count; i++)
            _pending[i].Receipt.CompleteSuperseded();
        _pending.Clear();
        _pendingRuleCount = 0;
        _pendingRetainedBytes = 0;
        _sequenceHighWater = 0;
        _effectiveFrameHighWater = -1;
        _lastFrame = -1;
    }

    internal bool Admit(NavigationAreaPolicyCommitOperation operation)
    {
        if (!operation.Receipt.TryClaimAdmission())
            return false;

        NavigationOperationRejection rejection = NavigationOperationRejection.None;
        if (operation.PublicationSequence == _sequenceHighWater)
            rejection = NavigationOperationRejection.DuplicateSequence;
        else if (operation.PublicationSequence < _sequenceHighWater)
            rejection = NavigationOperationRejection.RegressingSequence;
        else if (operation.EffectiveFrame < _effectiveFrameHighWater)
            rejection = NavigationOperationRejection.RegressingEffectiveFrame;
        else if (operation.EffectiveFrame <= _lastFrame)
            rejection = NavigationOperationRejection.LateEffectiveFrame;
        else if (operation.Policy.RuleCount != _navigationAreaCount)
            rejection = NavigationOperationRejection.ValidationFailed;
        else if (_pending.Count >= _maxPending)
            rejection = NavigationOperationRejection.CapacityExceeded;
        else if (operation.Policy.RuleCount > _maxRulesPerPolicy
            || operation.Policy.RuleCount > _maxRules - _pendingRuleCount
            || operation.Policy.RetainedBytes > _maxPendingBytes - _pendingRetainedBytes)
            rejection = NavigationOperationRejection.CapacityExceeded;

        if (operation.PublicationSequence > _sequenceHighWater)
        {
            _sequenceHighWater = operation.PublicationSequence;
            if (operation.EffectiveFrame > _effectiveFrameHighWater)
                _effectiveFrameHighWater = operation.EffectiveFrame;
        }

        if (rejection != NavigationOperationRejection.None)
        {
            operation.Receipt.CompleteRejected(rejection);
            return false;
        }
        _pending.Enqueue(operation);
        _pendingRuleCount += operation.Policy.RuleCount;
        _pendingRetainedBytes += operation.Policy.RetainedBytes;
        return true;
    }

    internal PreparedFrame Prepare(
        int frame,
        NavigationAreaCatalog current,
        MaintenanceWorkMeter meter,
        long maxCatalogBytes,
        int maxCatalogPages)
    {
        _lastFrame = frame;
        NavigationAreaCatalog candidate = current;
        int eligible = 0;
        while (eligible < _pending.Count
            && eligible < _maxBatchItems
            && _pending[eligible].EffectiveFrame <= frame)
        {
            NavigationAreaPolicy policy = _pending[eligible].Policy;
            if (!meter.TryConsumeDependencyEntries(candidate.GetPublishWork(
                    policy,
                    _maxPolicies,
                    _maxRules)))
                break;
            _outcomes[eligible] = candidate.TryPublish(
                policy,
                _maxPolicies,
                _navigationAreaCount,
                _maxRulesPerPolicy,
                _maxRules,
                out NavigationAreaCatalog next);
            if (_outcomes[eligible] == NavigationOperationRejection.None
                && (next.RetainedBytes > maxCatalogBytes
                    || next.PersistentPageCount > maxCatalogPages))
            {
                _outcomes[eligible] = NavigationOperationRejection.CapacityExceeded;
            }
            else if (_outcomes[eligible] == NavigationOperationRejection.None)
                candidate = next;
            eligible++;
        }
        return new PreparedFrame(this, candidate, _outcomes, eligible);
    }

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

        internal void Complete(int frame)
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

    private void RemovePrefix(int count)
    {
        for (int i = 0; i < count; i++)
        {
            NavigationAreaPolicyCommitOperation operation = _pending.Dequeue();
            _pendingRuleCount -= operation.Policy.RuleCount;
            _pendingRetainedBytes -= operation.Policy.RetainedBytes;
        }
    }
}
