//=======================================================================
// NavigationOperationReceipt.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Identifies the deterministic lifecycle state of a submitted navigation operation.</summary>
public enum NavigationOperationStatus
{
    /// <summary>The operation is admitted but has not published or terminated.</summary>
    Pending = 0,

    /// <summary>The operation was included in an immutable candidate publication.</summary>
    Applied = 1,

    /// <summary>The operation failed validation or admission without changing the candidate.</summary>
    Rejected = 2,

    /// <summary>A later operation made the complete operation observably redundant.</summary>
    Superseded = 3
}

/// <summary>Identifies why a navigation operation was rejected.</summary>
public enum NavigationOperationRejection
{
    /// <summary>The receipt is not rejected.</summary>
    None = 0,

    /// <summary>The operation descriptor failed structural validation.</summary>
    InvalidOperation = 1,

    /// <summary>The supplied operation sequence was already admitted.</summary>
    DuplicateSequence = 2,

    /// <summary>The supplied operation sequence was below the admitted high-water mark.</summary>
    RegressingSequence = 3,

    /// <summary>The effective frame regressed below the admitted frame high-water mark.</summary>
    RegressingEffectiveFrame = 4,

    /// <summary>The effective-frame boundary had already begun when the operation was submitted.</summary>
    LateEffectiveFrame = 5,

    /// <summary>A finite count or byte capacity would be exceeded.</summary>
    CapacityExceeded = 6,

    /// <summary>The operation referenced a map that was not present in the candidate.</summary>
    MissingMap = 7,

    /// <summary>The operation was based on a stale checkpoint stamp.</summary>
    Stale = 8,

    /// <summary>The complete candidate failed validation.</summary>
    ValidationFailed = 9
}

/// <summary>
/// Exposes one thread-safe terminal result for a deterministic navigation operation.
/// </summary>
public sealed class NavigationOperationReceipt
{
    private int _status;
    private int _rejection;
    private int _publishedFrame = -1;
    private int _admissionClaimed;
    private int _completionClaimed;

    internal NavigationOperationReceipt(long operationSequence, int effectiveFrame)
    {
        OperationSequence = operationSequence;
        EffectiveFrame = effectiveFrame;
    }

    /// <summary>Gets the host-supplied operation sequence.</summary>
    public long OperationSequence { get; }

    /// <summary>Gets the earliest fixed-step frame at which this operation may become visible.</summary>
    public int EffectiveFrame { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public NavigationOperationStatus Status => (NavigationOperationStatus)Volatile.Read(ref _status);

    /// <summary>Gets the rejection reason when <see cref="Status"/> is Rejected.</summary>
    public NavigationOperationRejection Rejection =>
        (NavigationOperationRejection)Volatile.Read(ref _rejection);

    /// <summary>Gets the actual publication frame, or -1 before publication.</summary>
    public int PublishedFrame => Volatile.Read(ref _publishedFrame);

    internal bool TryClaimAdmission() =>
        Interlocked.CompareExchange(ref _admissionClaimed, 1, 0) == 0;

    internal void CompleteApplied(int publishedFrame)
    {
        Complete(NavigationOperationStatus.Applied, NavigationOperationRejection.None, publishedFrame);
    }

    internal void CompleteRejected(NavigationOperationRejection rejection)
    {
        SwiftThrowHelper.ThrowIfArgument(
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
        int publishedFrame)
    {
        SwiftThrowHelper.ThrowIfTrue(
            Interlocked.CompareExchange(ref _completionClaimed, 1, 0) != 0,
            nameof(NavigationOperationReceipt),
            "A navigation operation receipt may complete only once.");

        Volatile.Write(ref _rejection, (int)rejection);
        Volatile.Write(ref _publishedFrame, publishedFrame);
        Volatile.Write(ref _status, (int)status);
    }
}
