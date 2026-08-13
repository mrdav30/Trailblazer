//=======================================================================
// PreparedNavigationOverlay.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;

namespace Trailblazer.Pathing;

/// <summary>Wraps an immutable validated overlay transaction before deterministic admission.</summary>
public sealed class PreparedNavigationOverlay
{
    /// <summary>Creates an inert prepared-overlay descriptor from an immutable transaction.</summary>
    public PreparedNavigationOverlay(NavigationOverlayTransaction transaction)
    {
        SwiftThrowHelper.ThrowIfNull(transaction, nameof(transaction));
        Transaction = transaction;
    }

    /// <summary>Gets the immutable overlay transaction.</summary>
    public NavigationOverlayTransaction Transaction { get; }

    /// <summary>Gets the deterministic descriptor-byte reservation.</summary>
    public long DescriptorBytes => Transaction.EstimatedDescriptorBytes;
}
