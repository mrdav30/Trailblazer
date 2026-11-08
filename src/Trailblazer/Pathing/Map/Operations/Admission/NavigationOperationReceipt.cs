//=======================================================================
// NavigationOperationReceipt.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Exposes one thread-safe terminal result for a deterministic navigation operation.
/// </summary>
public sealed class NavigationOperationReceipt
{
    private int _status;
    private int _rejection;
    private long _publishedFrame = -1;
    private int _admissionClaimed;
    private int _completionClaimed;

    internal NavigationOperationReceipt(long operationSequence, long effectiveFrame)
    {
        OperationSequence = operationSequence;
        EffectiveFrame = effectiveFrame;
    }

    /// <summary>Gets the host-supplied operation sequence.</summary>
    public long OperationSequence { get; }

    /// <summary>Gets the earliest fixed-step frame at which this operation may become visible.</summary>
    public long EffectiveFrame { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public NavigationOperationStatus Status => (NavigationOperationStatus)Volatile.Read(ref _status);

    /// <summary>Gets the rejection reason when <see cref="Status"/> is Rejected.</summary>
    public NavigationOperationRejection Rejection =>
        (NavigationOperationRejection)Volatile.Read(ref _rejection);

    /// <summary>Gets the actual publication frame, or -1 before publication.</summary>
    public long PublishedFrame => Volatile.Read(ref _publishedFrame);

    internal bool TryClaimAdmission() =>
        Interlocked.CompareExchange(ref _admissionClaimed, 1, 0) == 0;

    internal void CompleteApplied(long publishedFrame)
    {
        Complete(NavigationOperationStatus.Applied, NavigationOperationRejection.None, publishedFrame);
    }

    internal void CompleteRejected(NavigationOperationRejection rejection)
    {
        ChronicleGuard.ThrowIfArgument(
            rejection == NavigationOperationRejection.None,
            nameof(rejection),
            "A rejected receipt requires a reason.");
        Complete(NavigationOperationStatus.Rejected, rejection, publishedFrame: -1);
    }

    internal void CompleteSuperseded() =>
        Complete(NavigationOperationStatus.Superseded, NavigationOperationRejection.None, publishedFrame: -1);

    private void Complete(
        NavigationOperationStatus status,
        NavigationOperationRejection rejection,
        long publishedFrame)
    {
        ChronicleGuard.ThrowIfTrue(
            Interlocked.CompareExchange(ref _completionClaimed, 1, 0) != 0,
            nameof(NavigationOperationReceipt),
            "A navigation operation receipt may complete only once.");

        Volatile.Write(ref _rejection, (int)rejection);
        Volatile.Write(ref _publishedFrame, publishedFrame);
        Volatile.Write(ref _status, (int)status);
    }
}
