//=======================================================================
// NavigationOperationProcessor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;
using SwiftCollections;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Phase 1 pure candidate executor. Phase 2 replaces its mutable candidate with persistent graph roots.
/// </summary>
internal sealed class NavigationOperationProcessor
{
    private const int BaseCoalescingScratchBytes = 2_048;
    private const int FixedScratchBytesPerOperation = 8;
    private const int MapCoverageScratchBytes = 64;

    private readonly NavigationOperationLimits _limits;
    private readonly int _maxBakedCellsPerMap;
    private readonly int _navigationAreaCount;
    private readonly SwiftList<PendingOperation> _pending = new();
    private NavigationOperationCandidate _candidate;
    private readonly GridCellPrism[] _corridorPrisms;
    private readonly Vector3d[] _corridorWaypoints;
    private readonly NavigationCellAddress[] _corridorAddresses;
    private readonly NavigationAddressStampSet _corridorAddressSet;
    private readonly MaintenanceWorkMeter _unboundedMaintenanceMeter = new(
        new MaintenanceWorkBudget(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue));
    private readonly NavigationOperationRejection[] _outcomes;
    private readonly bool[] _superseded;
    private readonly NavigationOperationFrameChange[] _changes;
    private readonly SwiftHashSet<string> _mapOverwriters;
    private readonly SwiftHashSet<NavigationCellAddress> _coveredCells;
    private readonly SwiftHashSet<OverlayIdKey> _coveredConnections;
    private readonly SwiftHashSet<OverlayIdKey> _coveredTransitions;
    private readonly long _coverageScratchBytes;

    private long _pendingDescriptorBytes;
    private long _pendingPreparedMapBytes;
    private long _sequenceHighWater;
    private int _effectiveFrameHighWater = -1;
    private int _lastProcessedFrame = -1;
    private int _deferredPrefixCount;
    private int _foldOperationIndex;
    private NavigationMapFoldWork? _mapFoldWork;
    private NavigationOverlayFoldWork? _overlayFoldWork;
    private NavigationOperationCandidate? _sourceCandidateForDeferred;
    private NavigationOperationCandidate? _deferredCandidate;
    private NavigationOperationCandidate? _activeFoldSourceCandidate;
    private bool _supersedenceActive;
    private int _supersedenceIndex;
    private int _supersedenceMapIndex;
    private int _supersedenceKind;
    private int _supersedenceItemIndex;
    private bool _supersedenceCovered;
    private bool _supersedenceComplete;

    internal NavigationOperationProcessor(
        NavigationOperationLimits limits,
        int maxBakedCellsPerMap = int.MaxValue,
        int navigationAreaCount = ushort.MaxValue + 1)
    {
        SwiftThrowHelper.ThrowIfArgument(
            GetFixedScratchBytes(limits.MaxBatchItems) > limits.MaxBatchSortScratchBytes,
            nameof(limits),
            "Batch scratch capacity is smaller than the processor's fixed batch storage.");
        _limits = limits;
        _maxBakedCellsPerMap = maxBakedCellsPerMap;
        _navigationAreaCount = navigationAreaCount;
        _candidate = new NavigationOperationCandidate(navigationAreaCount);
        _corridorPrisms = new GridCellPrism[limits.MaxCorridorCells];
        _corridorWaypoints = new Vector3d[checked((limits.MaxCorridorCells - 1) * 2)];
        _corridorAddresses = new NavigationCellAddress[limits.MaxCorridorCells];
        _corridorAddressSet = new NavigationAddressStampSet(limits.MaxCorridorCells);
        _outcomes = new NavigationOperationRejection[limits.MaxBatchItems];
        _superseded = new bool[limits.MaxBatchItems];
        _changes = new NavigationOperationFrameChange[limits.MaxBatchItems];
        _mapOverwriters = new SwiftHashSet<string>(
            limits.MaxMaps,
            SwiftHashTools.GetDeterministicStringEqualityComparer());
        _coveredCells = new SwiftHashSet<NavigationCellAddress>(
            Math.Max(SwiftHashSet<NavigationCellAddress>.DefaultCapacity, limits.MaxOverlayCells));
        _coveredConnections = new SwiftHashSet<OverlayIdKey>(
            Math.Max(SwiftHashSet<OverlayIdKey>.DefaultCapacity, limits.MaxOverlayConnections));
        _coveredTransitions = new SwiftHashSet<OverlayIdKey>(
            Math.Max(SwiftHashSet<OverlayIdKey>.DefaultCapacity, limits.MaxOverlayTransitions));
        _coverageScratchBytes = checked(
            ((long)SwiftHashTools.NextPowerOfTwo(Math.Max(8, limits.MaxMaps)) * 32L)
            + ((long)SwiftHashTools.NextPowerOfTwo(Math.Max(8, limits.MaxOverlayCells)) * 40L)
            + ((long)SwiftHashTools.NextPowerOfTwo(Math.Max(8, limits.MaxOverlayConnections)) * 48L)
            + ((long)SwiftHashTools.NextPowerOfTwo(Math.Max(8, limits.MaxOverlayTransitions)) * 48L));
    }

    internal NavigationOperationCandidate Candidate => _candidate;

    internal long CoverageScratchBytes => _coverageScratchBytes;

    internal long RetainedOperationWorkBytes => checked(
        GetActiveAdditionalRetainedBytes()
        + (_supersedenceActive ? _coverageScratchBytes : 0));

    internal int RetainedOperationWorkPageCount => GetActiveAdditionalPersistentPages();

    internal int RetainedOperationWorkCount =>
        _mapFoldWork != null
        || _overlayFoldWork != null
        || _deferredCandidate != null
            ? 1
            : 0;

    internal void RejectDeferredCapacity()
    {
        int count = Math.Min(_deferredPrefixCount, _pending.Count);
        _candidate = _sourceCandidateForDeferred ?? _candidate;
        for (int i = 0; i < count; i++)
            _pending[i].Receipt.CompleteRejected(NavigationOperationRejection.CapacityExceeded);
        RemovePrefix(count);
        _deferredPrefixCount = 0;
        _foldOperationIndex = 0;
        _mapFoldWork = null;
        _overlayFoldWork = null;
        _sourceCandidateForDeferred = null;
        _deferredCandidate = null;
        _activeFoldSourceCandidate = null;
        ResetSupersedence();
    }

    internal void Reset()
    {
        for (int i = 0; i < _pending.Count; i++)
            _pending[i].Receipt.CompleteSuperseded();
        _pending.Clear();
        _candidate = new NavigationOperationCandidate(_navigationAreaCount);
        _pendingDescriptorBytes = 0;
        _pendingPreparedMapBytes = 0;
        _sequenceHighWater = 0;
        _effectiveFrameHighWater = -1;
        _lastProcessedFrame = -1;
        _deferredPrefixCount = 0;
        _foldOperationIndex = 0;
        _mapFoldWork = null;
        _overlayFoldWork = null;
        _sourceCandidateForDeferred = null;
        _deferredCandidate = null;
        _activeFoldSourceCandidate = null;
        ResetSupersedence();
        _mapOverwriters.Clear();
        _coveredCells.Clear();
        _coveredConnections.Clear();
        _coveredTransitions.Clear();
    }

    internal bool Admit(NavigationMapCommitOperation operation)
    {
        SwiftThrowHelper.ThrowIfNull(operation.PreparedMap, nameof(operation));
        return Admit(PendingOperation.ForMapCommit(operation));
    }

    internal bool Admit(NavigationMapRemoveOperation operation)
    {
        SwiftThrowHelper.ThrowIfNull(operation.MapId, nameof(operation));
        return Admit(PendingOperation.ForMapRemove(operation));
    }

    internal bool Admit(NavigationOverlayCommitOperation operation)
    {
        SwiftThrowHelper.ThrowIfNull(operation.PreparedOverlay, nameof(operation));
        return Admit(PendingOperation.ForOverlay(operation));
    }

    internal void ProcessFrame(int frame)
    {
        ProcessFrame(frame, static (_, _, _, _) => NavigationCandidatePublication.Published);
    }

    internal NavigationOperationFrameResult ProcessFrame(
        int frame,
        NavigationCandidatePublisher publishCandidate,
        MaintenanceWorkMeter? maintenanceMeter = null,
        NavigationRetainedWorkGuard? retainedWorkGuard = null)
    {
        SwiftThrowHelper.ThrowIfNull(publishCandidate, nameof(publishCandidate));
        SwiftThrowHelper.ThrowIfArgument(frame <= _lastProcessedFrame, nameof(frame));
        _lastProcessedFrame = frame;
        if (maintenanceMeter == null)
        {
            _unboundedMaintenanceMeter.Reset();
            maintenanceMeter = _unboundedMaintenanceMeter;
        }
        int eligibleCount = 0;
        long batchDescriptorBytes = 0;
        long batchScratchBytes = GetFixedScratchBytes(_limits.MaxBatchItems);
        int maximumEligible = _deferredPrefixCount == 0
            ? _pending.Count
            : Math.Min(_deferredPrefixCount, _pending.Count);
        while (eligibleCount < maximumEligible && _pending[eligibleCount].EffectiveFrame <= frame)
        {
            PendingOperation operation = _pending[eligibleCount];
            if (eligibleCount >= _limits.MaxBatchItems
                || WouldExceed(batchDescriptorBytes, operation.DescriptorBytes, _limits.MaxBatchDescriptorBytes)
                || WouldExceed(batchScratchBytes, operation.CoalescingScratchBytes, _limits.MaxBatchSortScratchBytes))
            {
                break;
            }

            batchDescriptorBytes += operation.DescriptorBytes;
            batchScratchBytes += operation.CoalescingScratchBytes;
            eligibleCount++;
        }

        NavigationOperationCandidate published = _sourceCandidateForDeferred ?? _candidate;
        NavigationOperationCandidate working = _deferredCandidate
            ?? (eligibleCount > 0 ? _candidate.Clone() : _candidate);
        _candidate = working;
        int foldStart = _deferredPrefixCount == 0 ? 0 : _foldOperationIndex;
        if (_mapFoldWork != null)
            _candidate = _mapFoldWork.Candidate;
        else if (_overlayFoldWork != null)
            _candidate = _overlayFoldWork.Candidate;
        for (int i = foldStart; i < eligibleCount; i++)
        {
            if (maintenanceMeter != null
                && _pending[i].Kind is PendingOperationKind.MapCommit or PendingOperationKind.MapRemove)
            {
                _mapFoldWork ??= _pending[i].Kind == PendingOperationKind.MapCommit
                    ? new NavigationMapFoldWork(
                        _candidate,
                        _pending[i].PreparedMap!,
                        _pending[i].ReplacementPolicy,
                        _limits,
                        _corridorPrisms,
                        _corridorWaypoints,
                        _corridorAddresses,
                        _corridorAddressSet)
                    : new NavigationMapFoldWork(
                        _candidate,
                        _pending[i].MapId!,
                        _corridorPrisms,
                        _corridorWaypoints,
                        _corridorAddresses,
                        _corridorAddressSet);
                _activeFoldSourceCandidate ??= _candidate;
                _foldOperationIndex = i;
                if (retainedWorkGuard != null
                    && !retainedWorkGuard(
                        GetAdditionalRetainedBytes(_mapFoldWork, published),
                        GetAdditionalPersistentPages(_mapFoldWork, published)))
                {
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
                    _candidate = _activeFoldSourceCandidate;
                    _mapFoldWork = null;
                    _activeFoldSourceCandidate = null;
                    _foldOperationIndex = i + 1;
                    _superseded[i] = false;
                    continue;
                }
                if (!_mapFoldWork.Advance(maintenanceMeter, out _outcomes[i]))
                {
                    _sourceCandidateForDeferred ??= published;
                    _deferredCandidate = _mapFoldWork.Candidate;
                    _candidate = published;
                    _deferredPrefixCount = eligibleCount;
                    return NavigationOperationFrameResult.Deferred;
                }
                if (_outcomes[i] == NavigationOperationRejection.None
                    && retainedWorkGuard != null
                    && !retainedWorkGuard(
                        GetAdditionalRetainedBytes(_mapFoldWork, published),
                        GetAdditionalPersistentPages(_mapFoldWork, published)))
                {
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
                }
                if (_outcomes[i] == NavigationOperationRejection.None)
                    _candidate = _mapFoldWork.Candidate;
                else
                    _candidate = _activeFoldSourceCandidate!;
                _mapFoldWork = null;
                _activeFoldSourceCandidate = null;
                _foldOperationIndex = i + 1;
            }
            else if (_pending[i].Kind == PendingOperationKind.Overlay
                && maintenanceMeter != null)
            {
                _overlayFoldWork ??= new NavigationOverlayFoldWork(
                    _candidate,
                    _pending[i].PreparedOverlay!.Transaction,
                    _pending[i].OperationSequence,
                    _limits,
                    _corridorPrisms,
                    _corridorWaypoints,
                    _corridorAddresses,
                    _corridorAddressSet);
                _activeFoldSourceCandidate ??= _candidate;
                _foldOperationIndex = i;
                if (retainedWorkGuard != null
                    && !retainedWorkGuard(
                        GetAdditionalRetainedBytes(_overlayFoldWork, published),
                        GetAdditionalPersistentPages(_overlayFoldWork, published)))
                {
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
                    _candidate = _activeFoldSourceCandidate;
                    _overlayFoldWork = null;
                    _activeFoldSourceCandidate = null;
                    _foldOperationIndex = i + 1;
                    _superseded[i] = false;
                    continue;
                }
                if (!_overlayFoldWork.Advance(maintenanceMeter, out _outcomes[i]))
                {
                    _sourceCandidateForDeferred ??= published;
                    _deferredCandidate = _overlayFoldWork.Candidate;
                    _candidate = published;
                    _deferredPrefixCount = eligibleCount;
                    return NavigationOperationFrameResult.Deferred;
                }
                if (_outcomes[i] == NavigationOperationRejection.None
                    && retainedWorkGuard != null
                    && !retainedWorkGuard(
                        GetAdditionalRetainedBytes(_overlayFoldWork, published),
                        GetAdditionalPersistentPages(_overlayFoldWork, published)))
                {
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
                }
                if (_outcomes[i] == NavigationOperationRejection.None)
                    _candidate = _overlayFoldWork.Candidate;
                else
                    _candidate = _activeFoldSourceCandidate!;
                _overlayFoldWork = null;
                _activeFoldSourceCandidate = null;
                _foldOperationIndex = i + 1;
            }
            else
            {
                _outcomes[i] = NavigationOperationRejection.InvalidOperation;
            }
            _foldOperationIndex = i + 1;
            _superseded[i] = false;
        }

        if (maintenanceMeter != null
            && retainedWorkGuard != null
            && !retainedWorkGuard(
                checked(GetPositiveRetainedDelta(_candidate, published) + _coverageScratchBytes),
                GetPositivePersistentPageDelta(_candidate, published)))
        {
            _candidate = published;
            for (int i = 0; i < eligibleCount; i++)
            {
                if (_outcomes[i] == NavigationOperationRejection.None)
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
            }
            ResetSupersedence();
        }
        else if (maintenanceMeter != null
            && !AdvanceSupersedence(eligibleCount, maintenanceMeter))
        {
            _sourceCandidateForDeferred ??= published;
            _deferredCandidate = _candidate;
            _candidate = published;
            _deferredPrefixCount = eligibleCount;
            return NavigationOperationFrameResult.Deferred;
        }

        int changeCount = 0;
        for (int i = 0; i < eligibleCount; i++)
        {
            if (_outcomes[i] == NavigationOperationRejection.None)
                _changes[changeCount++] = _pending[i].ToFrameChange();
        }
        NavigationCandidatePublication publication = eligibleCount > 0
            ? publishCandidate(_candidate, frame, _changes, changeCount)
            : NavigationCandidatePublication.Published;
        if (publication == NavigationCandidatePublication.Deferred)
        {
            _sourceCandidateForDeferred ??= published;
            _deferredCandidate = _candidate;
            if (_deferredPrefixCount == 0)
                _deferredPrefixCount = eligibleCount;
            _candidate = published;
            return NavigationOperationFrameResult.Deferred;
        }
        if (publication == NavigationCandidatePublication.PermanentCapacity)
        {
            _candidate = published;
            for (int i = 0; i < eligibleCount; i++)
            {
                if (_outcomes[i] == NavigationOperationRejection.None)
                    _outcomes[i] = NavigationOperationRejection.CapacityExceeded;
            }
        }

        for (int i = 0; i < eligibleCount; i++)
        {
            PendingOperation operation = _pending[i];
            if (_outcomes[i] != NavigationOperationRejection.None)
                operation.Receipt.CompleteRejected(_outcomes[i]);
            else if (_superseded[i])
                operation.Receipt.CompleteSuperseded();
            else
                operation.Receipt.CompleteApplied(frame);
        }

        RemovePrefix(eligibleCount);
        if (publication == NavigationCandidatePublication.Published)
            _candidate.ResetWorkCopiedPersistentOwnership();
        _deferredPrefixCount = 0;
        _foldOperationIndex = 0;
        _mapFoldWork = null;
        _overlayFoldWork = null;
        _sourceCandidateForDeferred = null;
        _deferredCandidate = null;
        _activeFoldSourceCandidate = null;
        ResetSupersedence();
        if (eligibleCount == 0)
            return NavigationOperationFrameResult.None;
        return publication == NavigationCandidatePublication.Published
            ? NavigationOperationFrameResult.Published
            : NavigationOperationFrameResult.Rejected;
    }

    private bool Admit(PendingOperation operation)
    {
        if (!operation.Receipt.TryClaimAdmission())
            return false;

        NavigationOperationRejection rejection = GetAdmissionRejection(operation);
        bool advancesSequence = operation.OperationSequence > _sequenceHighWater;
        if (advancesSequence)
        {
            _sequenceHighWater = operation.OperationSequence;
            if (operation.EffectiveFrame > _effectiveFrameHighWater)
                _effectiveFrameHighWater = operation.EffectiveFrame;
        }

        if (rejection != NavigationOperationRejection.None)
        {
            operation.Receipt.CompleteRejected(rejection);
            return false;
        }

        _pending.Add(operation);
        _pendingDescriptorBytes += operation.DescriptorBytes;
        _pendingPreparedMapBytes += operation.PreparedMapBytes;
        return true;
    }

    private NavigationOperationRejection GetAdmissionRejection(PendingOperation operation)
    {
        if (operation.OperationSequence == _sequenceHighWater)
            return NavigationOperationRejection.DuplicateSequence;
        if (operation.OperationSequence < _sequenceHighWater)
            return NavigationOperationRejection.RegressingSequence;
        if (operation.EffectiveFrame < _effectiveFrameHighWater)
            return NavigationOperationRejection.RegressingEffectiveFrame;
        if (operation.EffectiveFrame <= _lastProcessedFrame)
            return NavigationOperationRejection.LateEffectiveFrame;
        long fixedScratchBytes = GetFixedScratchBytes(_limits.MaxBatchItems);
        if (operation.DescriptorBytes > _limits.MaxBatchDescriptorBytes
            || (operation.Kind == PendingOperationKind.MapCommit
                && operation.PreparedMap!.Map.Cells.Count > _maxBakedCellsPerMap)
            || (operation.Kind == PendingOperationKind.MapCommit
                && operation.PreparedMap!.Map.TransitionRuleSpan.Length
                    > _limits.MaxTransitionRulesPerMap)
            || WouldExceed(fixedScratchBytes, operation.CoalescingScratchBytes, _limits.MaxBatchSortScratchBytes)
            || _pending.Count >= _limits.MaxPendingOperations
            || WouldExceed(_pendingDescriptorBytes, operation.DescriptorBytes, _limits.MaxPendingDescriptorBytes)
            || WouldExceed(_pendingPreparedMapBytes, operation.PreparedMapBytes, _limits.MaxPreparedMapBytes))
        {
            return NavigationOperationRejection.CapacityExceeded;
        }

        return NavigationOperationRejection.None;
    }

    private bool AdvanceSupersedence(int eligibleCount, MaintenanceWorkMeter meter)
    {
        if (_supersedenceComplete)
            return true;
        if (!_supersedenceActive)
        {
            _mapOverwriters.Clear();
            _coveredCells.Clear();
            _coveredConnections.Clear();
            _coveredTransitions.Clear();
            _supersedenceIndex = eligibleCount - 1;
            _supersedenceMapIndex = 0;
            _supersedenceKind = 0;
            _supersedenceItemIndex = 0;
            _supersedenceCovered = true;
            _supersedenceActive = true;
        }
        while (_supersedenceIndex >= 0)
        {
            if (_outcomes[_supersedenceIndex] != NavigationOperationRejection.None)
            {
                CompleteSupersedenceOperation();
                continue;
            }
            PendingOperation operation = _pending[_supersedenceIndex];
            if (operation.IsMapOperation)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _superseded[_supersedenceIndex] = _mapOverwriters.Contains(operation.MapId!);
                if (operation.Kind == PendingOperationKind.MapRemove
                    || (operation.Kind == PendingOperationKind.MapCommit
                        && operation.ReplacementPolicy == OverlayReplacementPolicy.Clear))
                {
                    _mapOverwriters.Add(operation.MapId!);
                }
                CompleteSupersedenceOperation();
                continue;
            }

            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                operation.PreparedOverlay!.Transaction.MapSpan;
            while (_supersedenceMapIndex < maps.Length)
            {
                NavigationMapOverlayDelta map = maps[_supersedenceMapIndex];
                if (_mapOverwriters.Contains(map.MapId))
                {
                    _supersedenceMapIndex++;
                    _supersedenceKind = 0;
                    _supersedenceItemIndex = 0;
                    continue;
                }
                while (_supersedenceKind < 6)
                {
                    int count = _supersedenceKind switch
                    {
                        0 or 3 => map.Cells.Count,
                        1 or 4 => map.Connections.Count,
                        _ => map.Transitions.Count
                    };
                    while (_supersedenceItemIndex < count)
                    {
                        if (!meter.TryConsumeOverlaySlots(1))
                            return false;
                        int index = _supersedenceItemIndex++;
                        if (_supersedenceKind < 3)
                        {
                            bool covered = _supersedenceKind switch
                            {
                                0 => _coveredCells.Contains(
                                    new NavigationCellAddress(map.MapId, map.Cells[index].Index)),
                                1 => _coveredConnections.Contains(
                                    new OverlayIdKey(map.MapId, map.Connections[index].Id)),
                                _ => _coveredTransitions.Contains(
                                    new OverlayIdKey(map.MapId, map.Transitions[index].Id))
                            };
                            _supersedenceCovered &= covered;
                        }
                        else if (_supersedenceKind == 3)
                        {
                            _coveredCells.Add(
                                new NavigationCellAddress(map.MapId, map.Cells[index].Index));
                        }
                        else if (_supersedenceKind == 4)
                            _coveredConnections.Add(new OverlayIdKey(map.MapId, map.Connections[index].Id));
                        else
                            _coveredTransitions.Add(new OverlayIdKey(map.MapId, map.Transitions[index].Id));
                    }
                    _supersedenceKind++;
                    _supersedenceItemIndex = 0;
                }
                _supersedenceMapIndex++;
                _supersedenceKind = 0;
            }
            _superseded[_supersedenceIndex] = _supersedenceCovered;
            CompleteSupersedenceOperation();
        }
        _supersedenceActive = false;
        _supersedenceComplete = true;
        return true;
    }

    private void CompleteSupersedenceOperation()
    {
        _supersedenceIndex--;
        _supersedenceMapIndex = 0;
        _supersedenceKind = 0;
        _supersedenceItemIndex = 0;
        _supersedenceCovered = true;
        _supersedenceComplete = false;
    }

    private void ResetSupersedence()
    {
        _supersedenceActive = false;
        _supersedenceComplete = false;
        _supersedenceIndex = -1;
        _supersedenceMapIndex = 0;
        _supersedenceKind = 0;
        _supersedenceItemIndex = 0;
        _supersedenceCovered = true;
    }

    private static bool WouldExceed(long current, long increment, long maximum) =>
        increment > maximum - current;

    private static long GetFixedScratchBytes(int maxBatchItems) =>
        BaseCoalescingScratchBytes + ((long)maxBatchItems * FixedScratchBytesPerOperation);

    private long GetActiveAdditionalRetainedBytes()
    {
        NavigationOperationCandidate published = _sourceCandidateForDeferred ?? _candidate;
        if (_mapFoldWork != null)
            return GetAdditionalRetainedBytes(_mapFoldWork, published);
        if (_overlayFoldWork != null)
            return GetAdditionalRetainedBytes(_overlayFoldWork, published);
        return _deferredCandidate == null
            ? 0
            : GetPositiveRetainedDelta(_deferredCandidate, published);
    }

    private int GetActiveAdditionalPersistentPages()
    {
        NavigationOperationCandidate published = _sourceCandidateForDeferred ?? _candidate;
        if (_mapFoldWork != null)
            return GetAdditionalPersistentPages(_mapFoldWork, published);
        if (_overlayFoldWork != null)
            return GetAdditionalPersistentPages(_overlayFoldWork, published);
        return _deferredCandidate == null
            ? 0
            : GetPositivePersistentPageDelta(_deferredCandidate, published);
    }

    private static long GetAdditionalRetainedBytes(
        NavigationMapFoldWork work,
        NavigationOperationCandidate published) => checked(
        work.RetainedBytes
        + GetPositiveNonPayloadRetainedDelta(work.Candidate, published)
        + GetAdditionalExplicitPayloadBytes(work.Candidate, published)
        + GetAdditionalMapStatePayloadBytes(work.Candidate, published)
        + work.DisplacedExplicitPayloadBytes
        + work.DisplacedMapStatePayloadBytes);

    private static int GetAdditionalPersistentPages(
        NavigationMapFoldWork work,
        NavigationOperationCandidate published) => checked(
        work.PersistentPageCount
        + GetPositiveNonPayloadPageDelta(work.Candidate, published)
        + GetAdditionalExplicitPayloadPages(work.Candidate, published)
        + GetAdditionalMapStatePayloadPages(work.Candidate, published)
        + work.DisplacedExplicitPayloadPages
        + work.DisplacedMapStatePayloadPages);

    private static long GetAdditionalRetainedBytes(
        NavigationOverlayFoldWork work,
        NavigationOperationCandidate published) => checked(
        work.RetainedBytes
        + GetPositiveNonPayloadRetainedDelta(work.Candidate, published)
        + GetAdditionalExplicitPayloadBytes(work.Candidate, published)
        + GetAdditionalMapStatePayloadBytes(work.Candidate, published)
        + work.DisplacedExplicitPayloadBytes
        + work.DisplacedMapStatePayloadBytes);

    private static int GetAdditionalPersistentPages(
        NavigationOverlayFoldWork work,
        NavigationOperationCandidate published) => checked(
        work.PersistentPageCount
        + GetPositiveNonPayloadPageDelta(work.Candidate, published)
        + GetAdditionalExplicitPayloadPages(work.Candidate, published)
        + GetAdditionalMapStatePayloadPages(work.Candidate, published)
        + work.DisplacedExplicitPayloadPages
        + work.DisplacedMapStatePayloadPages);

    private static long GetAdditionalExplicitPayloadBytes(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate published) => Math.Max(
            0L,
            candidate.WorkOwnedExplicitPayloadBytes
            - published.WorkOwnedExplicitPayloadBytes);

    private static int GetAdditionalExplicitPayloadPages(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate published) => Math.Max(
            0,
            candidate.WorkOwnedExplicitPayloadPages
            - published.WorkOwnedExplicitPayloadPages);

    private static long GetAdditionalMapStatePayloadBytes(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate published) => Math.Max(
            0L,
            candidate.WorkOwnedMapStatePayloadBytes
            - published.WorkOwnedMapStatePayloadBytes);

    private static int GetAdditionalMapStatePayloadPages(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate published) => Math.Max(
            0,
            candidate.WorkOwnedMapStatePayloadPages
            - published.WorkOwnedMapStatePayloadPages);

    private static long GetPositiveNonPayloadRetainedDelta(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate source) => Math.Max(
            0L,
            candidate.NonPayloadRetainedBytes - source.NonPayloadRetainedBytes);

    private static int GetPositiveNonPayloadPageDelta(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate source) => Math.Max(
            0,
            candidate.NonPayloadPersistentPageCount - source.NonPayloadPersistentPageCount);

    private static long GetPositiveRetainedDelta(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate source) =>
        checked(
            GetPositiveNonPayloadRetainedDelta(candidate, source)
            + GetAdditionalExplicitPayloadBytes(candidate, source)
            + GetAdditionalMapStatePayloadBytes(candidate, source)
            + candidate.WorkCopiedPersistentBytes);

    private static int GetPositivePersistentPageDelta(
        NavigationOperationCandidate candidate,
        NavigationOperationCandidate source) =>
        checked(
            GetPositiveNonPayloadPageDelta(candidate, source)
            + GetAdditionalExplicitPayloadPages(candidate, source)
            + GetAdditionalMapStatePayloadPages(candidate, source)
            + candidate.WorkCopiedPersistentPages);

    private void RemovePrefix(int count) => RemoveRange(0, count);

    private void RemoveRange(int index, int count)
    {
        if (count == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            PendingOperation operation = _pending[index + i];
            _pendingDescriptorBytes -= operation.DescriptorBytes;
            _pendingPreparedMapBytes -= operation.PreparedMapBytes;
        }

        for (int i = 0; i < count; i++)
            _pending.RemoveAt(index);
    }

    private enum PendingOperationKind
    {
        MapCommit,
        MapRemove,
        Overlay
    }

    private readonly struct OverlayIdKey : IEquatable<OverlayIdKey>
    {
        internal OverlayIdKey(string mapId, string id)
        {
            MapId = mapId;
            Id = id;
        }

        private string MapId { get; }
        private string Id { get; }

        public bool Equals(OverlayIdKey other) =>
            string.Equals(MapId, other.MapId, StringComparison.Ordinal)
            && string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is OverlayIdKey other && Equals(other);

        public override int GetHashCode()
        {
            var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
            return SwiftHashTools.CombineHashCodes(
                comparer.GetHashCode(MapId),
                comparer.GetHashCode(Id));
        }
    }

    private readonly struct PendingOperation
    {
        private PendingOperation(
            PendingOperationKind kind,
            string? mapId,
            PreparedNavigationMap? preparedMap,
            PreparedNavigationOverlay? preparedOverlay,
            OverlayReplacementPolicy replacementPolicy,
            long operationSequence,
            int effectiveFrame,
            NavigationOperationReceipt receipt,
            long descriptorBytes,
            long preparedMapBytes,
            long coalescingScratchBytes)
        {
            Kind = kind;
            MapId = mapId;
            PreparedMap = preparedMap;
            PreparedOverlay = preparedOverlay;
            ReplacementPolicy = replacementPolicy;
            OperationSequence = operationSequence;
            EffectiveFrame = effectiveFrame;
            Receipt = receipt;
            DescriptorBytes = descriptorBytes;
            PreparedMapBytes = preparedMapBytes;
            CoalescingScratchBytes = coalescingScratchBytes;
        }

        internal PendingOperationKind Kind { get; }
        internal string? MapId { get; }
        internal PreparedNavigationMap? PreparedMap { get; }
        internal PreparedNavigationOverlay? PreparedOverlay { get; }
        internal OverlayReplacementPolicy ReplacementPolicy { get; }
        internal long OperationSequence { get; }
        internal int EffectiveFrame { get; }
        internal NavigationOperationReceipt Receipt { get; }
        internal long DescriptorBytes { get; }
        internal long PreparedMapBytes { get; }
        internal long CoalescingScratchBytes { get; }
        internal bool IsMapOperation => Kind is PendingOperationKind.MapCommit or PendingOperationKind.MapRemove;

        internal NavigationOperationFrameChange ToFrameChange() => Kind switch
        {
            PendingOperationKind.MapCommit => NavigationOperationFrameChange.MapCommit(
                PreparedMap!,
                ReplacementPolicy,
                OperationSequence),
            PendingOperationKind.MapRemove => NavigationOperationFrameChange.MapRemove(
                MapId!,
                OperationSequence),
            _ => NavigationOperationFrameChange.Overlay(
                PreparedOverlay!,
                OperationSequence)
        };

        internal static PendingOperation ForMapCommit(NavigationMapCommitOperation operation) =>
            new(
                PendingOperationKind.MapCommit,
                operation.PreparedMap.Map.MapId,
                operation.PreparedMap,
                preparedOverlay: null,
                operation.OverlayReplacementPolicy,
                operation.OperationSequence,
                operation.EffectiveFrame,
                operation.Receipt,
                descriptorBytes: 64L + (operation.PreparedMap.Map.MapId.Length * sizeof(char)),
                preparedMapBytes: operation.PreparedMap.RetainedBytes,
                coalescingScratchBytes: MapCoverageScratchBytes);

        internal static PendingOperation ForMapRemove(NavigationMapRemoveOperation operation) =>
            new(
                PendingOperationKind.MapRemove,
                operation.MapId,
                preparedMap: null,
                preparedOverlay: null,
                OverlayReplacementPolicy.PreserveAndRevalidate,
                operation.OperationSequence,
                operation.EffectiveFrame,
                operation.Receipt,
                descriptorBytes: 48L + (operation.MapId.Length * sizeof(char)),
                preparedMapBytes: 0,
                coalescingScratchBytes: MapCoverageScratchBytes);

        internal static PendingOperation ForOverlay(NavigationOverlayCommitOperation operation) =>
            new(
                PendingOperationKind.Overlay,
                mapId: null,
                preparedMap: null,
                operation.PreparedOverlay,
                OverlayReplacementPolicy.PreserveAndRevalidate,
                operation.OperationSequence,
                operation.EffectiveFrame,
                operation.Receipt,
                operation.PreparedOverlay.DescriptorBytes,
                preparedMapBytes: 0,
                coalescingScratchBytes: GetOverlayCoverageScratchBytes(operation.PreparedOverlay));

        private static long GetOverlayCoverageScratchBytes(PreparedNavigationOverlay overlay) =>
            overlay.DescriptorBytes > long.MaxValue / 2
                ? long.MaxValue
                : overlay.DescriptorBytes * 2;

    }
}
