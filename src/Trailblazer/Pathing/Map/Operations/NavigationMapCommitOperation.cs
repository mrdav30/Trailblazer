//=======================================================================
// NavigationMapCommitOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;

namespace Trailblazer.Pathing;

/// <summary>Describes one deterministic prepared-map install or replacement operation.</summary>
public readonly struct NavigationMapCommitOperation
{
    /// <summary>Initializes an immutable prepared-map operation and its pending receipt.</summary>
    public NavigationMapCommitOperation(
        PreparedNavigationMap preparedMap,
        OverlayReplacementPolicy overlayReplacementPolicy,
        long operationSequence,
        int effectiveFrame)
    {
        SwiftThrowHelper.ThrowIfNull(preparedMap, nameof(preparedMap));
        SwiftThrowHelper.ThrowIfArgument(
            overlayReplacementPolicy is < OverlayReplacementPolicy.PreserveAndRevalidate or > OverlayReplacementPolicy.Clear,
            nameof(overlayReplacementPolicy));
        SwiftThrowHelper.ThrowIfArgument(
            preparedMap.CheckpointStamp.HasValue && overlayReplacementPolicy != OverlayReplacementPolicy.Clear,
            nameof(overlayReplacementPolicy),
            "A checkpoint-stamped map can only commit with the Clear replacement policy.");
        NavigationOperationValidation.ValidateSchedule(operationSequence, effectiveFrame);

        PreparedMap = preparedMap;
        OverlayReplacementPolicy = overlayReplacementPolicy;
        OperationSequence = operationSequence;
        EffectiveFrame = effectiveFrame;
        Receipt = new NavigationOperationReceipt(operationSequence, effectiveFrame);
    }

    /// <summary>Gets the prepared inert map.</summary>
    public PreparedNavigationMap PreparedMap { get; }

    /// <summary>Gets the overlay replacement policy.</summary>
    public OverlayReplacementPolicy OverlayReplacementPolicy { get; }

    /// <summary>Gets the unique host-supplied operation sequence.</summary>
    public long OperationSequence { get; }

    /// <summary>Gets the earliest eligible fixed-step publication frame.</summary>
    public int EffectiveFrame { get; }

    /// <summary>Gets the operation's deterministic pending or terminal receipt.</summary>
    public NavigationOperationReceipt Receipt { get; }
}
