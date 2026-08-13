//=======================================================================
// NavigationOverlayCommitOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;

namespace Trailblazer.Pathing;

/// <summary>Describes one deterministic atomic overlay-transaction operation.</summary>
public readonly struct NavigationOverlayCommitOperation
{
    /// <summary>Initializes an immutable overlay operation and its pending receipt.</summary>
    public NavigationOverlayCommitOperation(
        PreparedNavigationOverlay preparedOverlay,
        long operationSequence,
        int effectiveFrame)
    {
        SwiftThrowHelper.ThrowIfNull(preparedOverlay, nameof(preparedOverlay));
        NavigationOperationValidation.ValidateSchedule(operationSequence, effectiveFrame);

        PreparedOverlay = preparedOverlay;
        OperationSequence = operationSequence;
        EffectiveFrame = effectiveFrame;
        Receipt = new NavigationOperationReceipt(operationSequence, effectiveFrame);
    }

    /// <summary>Gets the prepared immutable overlay transaction.</summary>
    public PreparedNavigationOverlay PreparedOverlay { get; }

    /// <summary>Gets the unique host-supplied operation sequence.</summary>
    public long OperationSequence { get; }

    /// <summary>Gets the earliest eligible fixed-step publication frame.</summary>
    public int EffectiveFrame { get; }

    /// <summary>Gets the operation's deterministic pending or terminal receipt.</summary>
    public NavigationOperationReceipt Receipt { get; }
}
