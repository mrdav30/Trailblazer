//=======================================================================
// NavigationContextCacheGate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns the context lock shared by query reservations and result-cache state.</summary>
internal sealed class NavigationContextCacheGate
{
    private readonly long _maxResultBytes;
    private long _reservedResultBytes;
    private long _payloadResultBytes;
    private long _safetyEpoch;
    private bool _safetyPending;

    internal NavigationContextCacheGate(long maxResultBytes) =>
        _maxResultBytes = maxResultBytes;

    internal object SyncRoot { get; } = new();

    internal long ReservedResultBytes
    {
        get { lock (SyncRoot) return _reservedResultBytes; }
    }

    internal long PayloadResultBytes
    {
        get { lock (SyncRoot) return _payloadResultBytes; }
    }

    internal long TotalResultBytes
    {
        get { lock (SyncRoot) return checked(_reservedResultBytes + _payloadResultBytes); }
    }

    internal bool IsSafetyPending
    {
        get { lock (SyncRoot) return _safetyPending; }
    }

    internal long SafetyEpoch
    {
        get { lock (SyncRoot) return _safetyEpoch; }
    }

    internal void MarkSafetyPending()
    {
        lock (SyncRoot)
        {
            if (_safetyPending)
                return;
            _safetyEpoch = checked(_safetyEpoch + 1);
            _safetyPending = true;
        }
    }

    internal void ClearSafetyPending()
    {
        lock (SyncRoot)
            _safetyPending = false;
    }

    internal bool IsSafetyEpochCurrent(long safetyEpoch)
    {
        lock (SyncRoot)
            return !_safetyPending && safetyEpoch == _safetyEpoch;
    }

    internal bool CanReserveResultBytesUnderGate(long resultBytes) =>
        resultBytes <= _maxResultBytes - _reservedResultBytes - _payloadResultBytes;

    internal void ReserveResultBytesUnderGate(long resultBytes) =>
        _reservedResultBytes = checked(_reservedResultBytes + resultBytes);

    internal void ReleaseReservedResultBytesUnderGate(long resultBytes) =>
        _reservedResultBytes -= resultBytes;

    internal void TransferReservedResultToPayloadUnderGate(long resultBytes)
    {
        _reservedResultBytes -= resultBytes;
        _payloadResultBytes = checked(_payloadResultBytes + resultBytes);
    }

    internal void ReleasePayloadResultBytesUnderGate(long resultBytes) =>
        _payloadResultBytes -= resultBytes;
}
