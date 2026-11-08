//=======================================================================
// NavigationGraphMapDiagnostic.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Collections.Generic;
using System.Collections.ObjectModel;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

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
