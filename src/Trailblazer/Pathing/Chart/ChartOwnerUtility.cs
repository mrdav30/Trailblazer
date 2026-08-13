//=======================================================================
// ChartOwnerUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Copies chart-owner ids between pooled SwiftCollections without relying on bulk-add helpers.
/// </summary>
internal static class ChartOwnerUtility
{
    /// <summary>
    /// Copies chart-owner ids into the destination set using deterministic per-item insertion.
    /// </summary>
    internal static void AddOwners(SwiftHashSet<string> destination, SwiftHashSet<string>? source)
    {
        if (destination == null || source == null)
            return;

        foreach (string owner in source)
            destination.Add(owner);
    }
}
