//=======================================================================
// NavigationOperationProcessor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;
using SwiftCollections.Diagnostics;
using System;
using GridForge.Grids.Topology;
using FixedMathSharp;
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
    private readonly SwiftList<PendingOperation> _pending = new();
    private readonly NavigationOperationCandidate _candidate = new();
    private readonly GridCellPrism[] _corridorPrisms;
    private readonly Vector3d[] _corridorWaypoints;
    private readonly NavigationOperationRejection[] _outcomes;
    private readonly bool[] _superseded;
    private readonly SwiftHashSet<string> _mapOverwriters = new(
        SwiftHashTools.GetDeterministicStringEqualityComparer());
    private readonly SwiftHashSet<NavigationCellAddress> _coveredCells = new();
    private readonly SwiftHashSet<OverlayIdKey> _coveredConnections = new();
    private readonly SwiftHashSet<OverlayIdKey> _coveredTransitions = new();

    private long _pendingDescriptorBytes;
    private long _pendingPreparedMapBytes;
    private long _sequenceHighWater;
    private int _effectiveFrameHighWater = -1;
    private int _lastProcessedFrame = -1;

    internal NavigationOperationProcessor(NavigationOperationLimits limits)
    {
        SwiftThrowHelper.ThrowIfArgument(
            GetFixedScratchBytes(limits.MaxBatchItems) > limits.MaxBatchSortScratchBytes,
            nameof(limits),
            "Batch scratch capacity is smaller than the processor's fixed batch storage.");
        _limits = limits;
        _corridorPrisms = new GridCellPrism[limits.MaxCorridorCells];
        _corridorWaypoints = new Vector3d[checked((limits.MaxCorridorCells - 1) * 2)];
        _outcomes = new NavigationOperationRejection[limits.MaxBatchItems];
        _superseded = new bool[limits.MaxBatchItems];
    }

    internal NavigationOperationCandidate Candidate => _candidate;

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
        SwiftThrowHelper.ThrowIfArgument(frame <= _lastProcessedFrame, nameof(frame));
        _lastProcessedFrame = frame;

        int eligibleCount = 0;
        long batchDescriptorBytes = 0;
        long batchScratchBytes = GetFixedScratchBytes(_limits.MaxBatchItems);
        while (eligibleCount < _pending.Count && _pending[eligibleCount].EffectiveFrame <= frame)
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

        for (int i = 0; i < eligibleCount; i++)
        {
            _outcomes[i] = Apply(_pending[i]);
            _superseded[i] = false;
        }

        MarkConservativeSuperseded(eligibleCount);
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
            || WouldExceed(fixedScratchBytes, operation.CoalescingScratchBytes, _limits.MaxBatchSortScratchBytes)
            || _pending.Count >= _limits.MaxPendingOperations
            || WouldExceed(_pendingDescriptorBytes, operation.DescriptorBytes, _limits.MaxPendingDescriptorBytes)
            || WouldExceed(_pendingPreparedMapBytes, operation.PreparedMapBytes, _limits.MaxPreparedMapBytes))
        {
            return NavigationOperationRejection.CapacityExceeded;
        }

        return NavigationOperationRejection.None;
    }

    private NavigationOperationRejection Apply(PendingOperation operation)
    {
        return operation.Kind switch
        {
            PendingOperationKind.MapCommit => _candidate.ApplyMap(
                operation.PreparedMap!,
                operation.ReplacementPolicy,
                _limits,
                _corridorPrisms,
                _corridorWaypoints),
            PendingOperationKind.MapRemove => _candidate.RemoveMap(operation.MapId!),
            PendingOperationKind.Overlay => _candidate.ApplyOverlay(
                operation.PreparedOverlay!.Transaction,
                operation.OperationSequence,
                _limits,
                _corridorPrisms,
                _corridorWaypoints),
            _ => NavigationOperationRejection.InvalidOperation
        };
    }

    private void MarkConservativeSuperseded(int eligibleCount)
    {
        _mapOverwriters.Clear();
        _coveredCells.Clear();
        _coveredConnections.Clear();
        _coveredTransitions.Clear();

        for (int index = eligibleCount - 1; index >= 0; index--)
        {
            if (_outcomes[index] != NavigationOperationRejection.None)
                continue;

            PendingOperation operation = _pending[index];
            if (operation.IsMapOperation)
            {
                _superseded[index] = _mapOverwriters.Contains(operation.MapId!);
                if (operation.Kind == PendingOperationKind.MapRemove
                    || (operation.Kind == PendingOperationKind.MapCommit
                        && operation.ReplacementPolicy == OverlayReplacementPolicy.Clear))
                {
                    _mapOverwriters.Add(operation.MapId!);
                }
                continue;
            }

            NavigationOverlayTransaction transaction = operation.PreparedOverlay!.Transaction;
            _superseded[index] = IsOverlayCovered(transaction);
            AddOverlayCoverage(transaction);
        }
    }

    private bool IsOverlayCovered(NavigationOverlayTransaction transaction)
    {
        ReadOnlySpan<NavigationMapOverlayDelta> maps = transaction.MapSpan;
        for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
        {
            NavigationMapOverlayDelta map = maps[mapIndex];
            if (_mapOverwriters.Contains(map.MapId))
                continue;

            for (int i = 0; i < map.Cells.Count; i++)
            {
                if (!_coveredCells.Contains(new NavigationCellAddress(map.MapId, map.Cells[i].Index)))
                    return false;
            }
            for (int i = 0; i < map.Connections.Count; i++)
            {
                if (!_coveredConnections.Contains(new OverlayIdKey(map.MapId, map.Connections[i].Id)))
                    return false;
            }
            for (int i = 0; i < map.Transitions.Count; i++)
            {
                if (!_coveredTransitions.Contains(new OverlayIdKey(map.MapId, map.Transitions[i].Id)))
                    return false;
            }
        }

        return true;
    }

    private void AddOverlayCoverage(NavigationOverlayTransaction transaction)
    {
        ReadOnlySpan<NavigationMapOverlayDelta> maps = transaction.MapSpan;
        for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
        {
            NavigationMapOverlayDelta map = maps[mapIndex];
            for (int i = 0; i < map.Cells.Count; i++)
                _coveredCells.Add(new NavigationCellAddress(map.MapId, map.Cells[i].Index));
            for (int i = 0; i < map.Connections.Count; i++)
                _coveredConnections.Add(new OverlayIdKey(map.MapId, map.Connections[i].Id));
            for (int i = 0; i < map.Transitions.Count; i++)
                _coveredTransitions.Add(new OverlayIdKey(map.MapId, map.Transitions[i].Id));
        }
    }

    private static bool WouldExceed(long current, long increment, long maximum) =>
        increment > maximum - current;

    private static long GetFixedScratchBytes(int maxBatchItems) =>
        BaseCoalescingScratchBytes + ((long)maxBatchItems * FixedScratchBytesPerOperation);

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
