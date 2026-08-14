//=======================================================================
// NavigationGraphDiagnosticsSnapshot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Collections.Generic;
using System.Collections.ObjectModel;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Identifies the semantic source of one effective graph cell.</summary>
public enum NavigationCellSemanticSource
{
    /// <summary>The effective value comes from the immutable map bake.</summary>
    Baked = 0,
    /// <summary>A semantic overlay supplies the effective payload.</summary>
    OverlaySet = 1,
    /// <summary>A semantic overlay tombstones traversal at the address.</summary>
    OverlaySuppressed = 2,
    /// <summary>A semantic overlay owns an unbaked dynamic cell slot.</summary>
    DynamicOverlaySet = 3,
    /// <summary>An unbaked dynamic slot remains reserved but has no effective payload.</summary>
    DynamicInactive = 4
}

/// <summary>Copies one addressed composed cell for bounded diagnostics.</summary>
public readonly struct NavigationGraphCellDiagnostic
{
    internal NavigationGraphCellDiagnostic(
        VoxelIndex index,
        int slot,
        NavigationCellSemanticSource semanticSource,
        bool hasCell,
        NavigationCell cell,
        bool isPresent,
        byte obstacleCount)
    {
        Index = index;
        Slot = slot;
        SemanticSource = semanticSource;
        HasCell = hasCell;
        Cell = cell;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
    }

    /// <summary>Gets the topology-local address.</summary>
    public VoxelIndex Index { get; }
    /// <summary>Gets the stable map-local baked or dynamic slot.</summary>
    public int Slot { get; }
    /// <summary>Gets the baked, override, tombstone, or dynamic source.</summary>
    public NavigationCellSemanticSource SemanticSource { get; }
    /// <summary>Gets whether the address has an effective semantic cell.</summary>
    public bool HasCell { get; }
    /// <summary>Gets the effective cell payload when <see cref="HasCell"/> is true.</summary>
    public NavigationCell Cell { get; }
    /// <summary>Gets the mirrored GridForge presence state.</summary>
    public bool IsPresent { get; }
    /// <summary>Gets the mirrored exact obstacle count.</summary>
    public byte ObstacleCount { get; }
    /// <summary>Gets whether physical blockage suppresses traversal.</summary>
    public bool IsBlocked => ObstacleCount > 0;
}

/// <summary>Copies one map instance and its bounded addressed diagnostics.</summary>
public sealed class NavigationGraphMapDiagnostic
{
    private readonly ReadOnlyCollection<NavigationGraphCellDiagnostic> _cells;

    internal NavigationGraphMapDiagnostic(
        string mapId,
        long bakeVersion,
        long instanceVersion,
        long overlayHighWaterSequence,
        long physicalVersion,
        int componentId,
        long componentVersion,
        int incidentExplicitEdgeCount,
        bool isMaterialized,
        long worldSpawnToken,
        long gridSpawnToken,
        GridConfigurationKey configurationKey,
        GridTopologyKind topologyKind,
        GridStorageKind storageKind,
        NavigationCellLookupKind lookupKind,
        int bakedSlotCount,
        int dynamicSlotCount,
        long retainedBytes,
        int lastBaselineAddressCount,
        int lastCopiedSemanticPages,
        int lastCopiedPhysicalPages,
        NavigationGraphCellDiagnostic[] cells)
    {
        MapId = mapId;
        BakeVersion = bakeVersion;
        InstanceVersion = instanceVersion;
        OverlayHighWaterSequence = overlayHighWaterSequence;
        PhysicalVersion = physicalVersion;
        ComponentId = componentId;
        ComponentVersion = componentVersion;
        IncidentExplicitEdgeCount = incidentExplicitEdgeCount;
        IsMaterialized = isMaterialized;
        WorldSpawnToken = worldSpawnToken;
        GridSpawnToken = gridSpawnToken;
        ConfigurationKey = configurationKey;
        TopologyKind = topologyKind;
        StorageKind = storageKind;
        LookupKind = lookupKind;
        BakedSlotCount = bakedSlotCount;
        DynamicSlotCount = dynamicSlotCount;
        RetainedBytes = retainedBytes;
        LastBaselineAddressCount = lastBaselineAddressCount;
        LastCopiedSemanticPages = lastCopiedSemanticPages;
        LastCopiedPhysicalPages = lastCopiedPhysicalPages;
        _cells = System.Array.AsReadOnly(cells);
    }

    /// <summary>Gets the stable map ID.</summary>
    public string MapId { get; }
    /// <summary>Gets the immutable bake version.</summary>
    public long BakeVersion { get; }
    /// <summary>Gets the last graph generation that changed this instance.</summary>
    public long InstanceVersion { get; }
    /// <summary>Gets the semantic overlay high-water sequence.</summary>
    public long OverlayHighWaterSequence { get; }
    /// <summary>Gets the last immutable physical-page publication version.</summary>
    public long PhysicalVersion { get; }
    /// <summary>Gets the deterministic weak structural component ID.</summary>
    public int ComponentId { get; }
    /// <summary>Gets the component/composition generation.</summary>
    public long ComponentVersion { get; }
    /// <summary>Gets the source and incoming explicit dependency count.</summary>
    public int IncidentExplicitEdgeCount { get; }
    /// <summary>Gets whether an exact GridForge generation is active.</summary>
    public bool IsMaterialized { get; }
    /// <summary>Gets the exact owning GridWorld generation.</summary>
    public long WorldSpawnToken { get; }
    /// <summary>Gets the exact runtime grid generation.</summary>
    public long GridSpawnToken { get; }
    /// <summary>Gets the normalized authored grid address.</summary>
    public GridConfigurationKey ConfigurationKey { get; }
    /// <summary>Gets the topology kind.</summary>
    public GridTopologyKind TopologyKind { get; }
    /// <summary>Gets the storage kind of the normalized binding.</summary>
    public GridStorageKind StorageKind { get; }
    /// <summary>Gets the density-selected local lookup representation.</summary>
    public NavigationCellLookupKind LookupKind { get; }
    /// <summary>Gets the stable baked slot count.</summary>
    public int BakedSlotCount { get; }
    /// <summary>Gets the non-reused dynamic slot count.</summary>
    public int DynamicSlotCount { get; }
    /// <summary>Gets conservative retained instance bytes.</summary>
    public long RetainedBytes { get; }
    /// <summary>Gets the addresses read by this instance's latest baseline capture.</summary>
    public int LastBaselineAddressCount { get; }
    /// <summary>Gets the semantic leaf pages copied by this instance's latest update.</summary>
    public int LastCopiedSemanticPages { get; }
    /// <summary>Gets the physical leaf pages copied by this instance's latest update.</summary>
    public int LastCopiedPhysicalPages { get; }
    /// <summary>Gets copied addressed cell diagnostics.</summary>
    public IReadOnlyList<NavigationGraphCellDiagnostic> Cells => _cells;
}

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
