//=======================================================================
// NavigationMapRemoveOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;

namespace Trailblazer.Pathing;

/// <summary>Describes one deterministic map-and-overlay removal operation.</summary>
public readonly struct NavigationMapRemoveOperation
{
    /// <summary>Initializes an immutable map-removal operation and its pending receipt.</summary>
    public NavigationMapRemoveOperation(string mapId, long operationSequence, int effectiveFrame)
    {
        SwiftThrowHelper.ThrowIfNull(mapId, nameof(mapId));
        SwiftThrowHelper.ThrowIfArgument(string.IsNullOrWhiteSpace(mapId), nameof(mapId));
        NavigationOperationValidation.ValidateSchedule(operationSequence, effectiveFrame);

        MapId = mapId;
        OperationSequence = operationSequence;
        EffectiveFrame = effectiveFrame;
        Receipt = new NavigationOperationReceipt(operationSequence, effectiveFrame);
    }

    /// <summary>Gets the stable map identifier to remove.</summary>
    public string MapId { get; }

    /// <summary>Gets the unique host-supplied operation sequence.</summary>
    public long OperationSequence { get; }

    /// <summary>Gets the earliest eligible fixed-step publication frame.</summary>
    public int EffectiveFrame { get; }

    /// <summary>Gets the operation's deterministic pending or terminal receipt.</summary>
    public NavigationOperationReceipt Receipt { get; }
}
