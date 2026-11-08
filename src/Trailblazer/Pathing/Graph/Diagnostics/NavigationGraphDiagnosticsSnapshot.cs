//=======================================================================
// NavigationGraphDiagnosticsSnapshot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trailblazer.Pathing;

/// <summary>Copies a bounded immutable navigation graph diagnostic view.</summary>
public sealed class NavigationGraphDiagnosticsSnapshot
{
    private readonly ReadOnlyCollection<NavigationGraphMapDiagnostic> _maps;

    internal NavigationGraphDiagnosticsSnapshot(
        long graphVersion,
        long areaCatalogVersion,
        long activeSnapshotBytes,
        int activeSnapshotCount,
        int activeSnapshotLeaseCount,
        int persistentGraphPageCount,
        int retiredGenerationCount,
        long retiredSnapshotBytes,
        int pendingAreaPolicyCount,
        int pendingAreaRuleCount,
        long pendingAreaPolicyBytes,
        int baselineRebuildCount,
        int baselineCapacityBlockedCount,
        long baselineRebuildBytes,
        int baselineRebuildPageCount,
        bool isTruncated,
        NavigationGraphMapDiagnostic[] maps)
    {
        GraphVersion = graphVersion;
        AreaCatalogVersion = areaCatalogVersion;
        ActiveSnapshotBytes = activeSnapshotBytes;
        ActiveSnapshotCount = activeSnapshotCount;
        ActiveSnapshotLeaseCount = activeSnapshotLeaseCount;
        PersistentGraphPageCount = persistentGraphPageCount;
        RetiredGenerationCount = retiredGenerationCount;
        RetiredSnapshotBytes = retiredSnapshotBytes;
        PendingAreaPolicyCount = pendingAreaPolicyCount;
        PendingAreaRuleCount = pendingAreaRuleCount;
        PendingAreaPolicyBytes = pendingAreaPolicyBytes;
        BaselineRebuildCount = baselineRebuildCount;
        BaselineCapacityBlockedCount = baselineCapacityBlockedCount;
        BaselineRebuildBytes = baselineRebuildBytes;
        BaselineRebuildPageCount = baselineRebuildPageCount;
        IsTruncated = isTruncated;
        _maps = System.Array.AsReadOnly(maps);
    }

    /// <summary>Gets the immutable graph generation.</summary>
    public long GraphVersion { get; }
    /// <summary>Gets the immutable area-catalog generation.</summary>
    public long AreaCatalogVersion { get; }
    /// <summary>Gets conservative current-root plus unpublished retained-work bytes.</summary>
    public long ActiveSnapshotBytes { get; }
    /// <summary>Gets the current plus leased retired snapshot-generation count.</summary>
    public int ActiveSnapshotCount { get; }
    /// <summary>Gets the number of checked-out snapshot leases.</summary>
    public int ActiveSnapshotLeaseCount { get; }
    /// <summary>Gets conservative current-root plus unpublished retained-work page count.</summary>
    public int PersistentGraphPageCount { get; }
    /// <summary>Gets leased retired generation count.</summary>
    public int RetiredGenerationCount { get; }
    /// <summary>Gets conservative leased retired-root bytes.</summary>
    public long RetiredSnapshotBytes { get; }
    /// <summary>Gets the number of admitted area-policy revisions awaiting terminal publication.</summary>
    public int PendingAreaPolicyCount { get; }
    /// <summary>Gets direct-indexed rules retained by pending area-policy revisions.</summary>
    public int PendingAreaRuleCount { get; }
    /// <summary>Gets conservative bytes retained by pending area-policy revisions.</summary>
    public long PendingAreaPolicyBytes { get; }
    /// <summary>Gets the number of fail-closed map baselines being rebuilt across frames.</summary>
    public int BaselineRebuildCount { get; }
    /// <summary>Gets terminal fail-closed baselines whose configured active cap cannot fit another page.</summary>
    public int BaselineCapacityBlockedCount { get; }
    /// <summary>Gets conservatively retained unpublished baseline-rebuild bytes.</summary>
    public long BaselineRebuildBytes { get; }
    /// <summary>Gets conservatively retained unpublished baseline-rebuild pages.</summary>
    public int BaselineRebuildPageCount { get; }
    /// <summary>Gets whether the context diagnostic cell ceiling truncated output.</summary>
    public bool IsTruncated { get; }
    /// <summary>Gets map diagnostics in ordinal MapId order.</summary>
    public IReadOnlyList<NavigationGraphMapDiagnostic> Maps => _maps;
}
