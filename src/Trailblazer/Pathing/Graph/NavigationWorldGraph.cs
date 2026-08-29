//=======================================================================
// NavigationWorldGraph.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable context-owned navigation graph root.</summary>
internal sealed partial class NavigationWorldGraph
{
    internal const long BaseRetainedBytes = 128L;

    private readonly NavigationInstanceDirectory _instances;
    private readonly PersistentGridConfigurationMap<string> _mapIndex;
    private readonly NavigationSurfaceComponentKeySet _closedStructuralComponents;
    private readonly NavigationSurfaceComponentKeySet _additionalClosedStructuralComponents;
    private readonly bool _allStructuralComponentsClosed;
    private readonly NavigationExplicitConnectionIndex _explicitConnections;
    private readonly NavigationAutomaticSeamIndex _automaticSeams;
    private readonly NavigationTransitionPageRoot _transitionPages;
    private int _leaseCount;

    internal NavigationWorldGraph(
        long graphVersion,
        NavigationMapInstance[] instances,
        NavigationAreaCatalog? areaCatalog = null,
        PersistentGridConfigurationMap<string>? mapIndex = null,
        NavigationExplicitConnectionIndex? explicitConnections = null,
        NavigationAutomaticSeamIndex? automaticSeams = null,
        NavigationSurfaceComponentIndex? surfaceComponents = null)
    {
        GraphVersion = graphVersion;
        _instances = NavigationInstanceDirectory.Create(instances);
        AreaCatalog = areaCatalog ?? NavigationAreaCatalog.Empty;
        _mapIndex = mapIndex ?? BuildMapIndex(instances);
        _explicitConnections = explicitConnections ?? NavigationExplicitConnectionIndex.Empty;
        _automaticSeams = automaticSeams ?? NavigationAutomaticSeamIndex.Empty;
        SurfaceComponents = surfaceComponents ?? NavigationSurfaceComponentIndex.Empty;
        _transitionPages = NavigationTransitionPageRoot.Empty;
        TransitionRules = NavigationTransitionRuleTable.Empty;
        _closedStructuralComponents = NavigationSurfaceComponentKeySet.Empty;
        _additionalClosedStructuralComponents = NavigationSurfaceComponentKeySet.Empty;
        _allStructuralComponentsClosed = false;
        long bytes = checked(
            BaseRetainedBytes
            + _instances.RetainedBytes
            + _mapIndex.RetainedBytes
            + _closedStructuralComponents.RetainedBytes
            + SurfaceComponents.RetainedBytes
            + _explicitConnections.RetainedBytes
            + _automaticSeams.RetainedBytes
            + _transitionPages.RetainedBytes
            + TransitionRules.RetainedBytes
            + AreaCatalog.RetainedBytes);
        PersistentPageCount = _instances.PersistentPageCount
            + 1 + _mapIndex.Count
            + _closedStructuralComponents.PersistentPageCount
            + SurfaceComponents.PersistentPageCount
            + _explicitConnections.PersistentPageCount
            + _automaticSeams.PersistentPageCount
            + _transitionPages.PersistentPageCount
            + TransitionRules.PersistentPageCount
            + AreaCatalog.PersistentPageCount;
        for (int i = 0; i < instances.Length; i++)
        {
            bytes = checked(bytes + instances[i].RetainedBytes);
            PersistentPageCount += instances[i].PersistentPageCount;
        }
        RetainedBytes = bytes;
    }

    private NavigationWorldGraph(
        long graphVersion,
        NavigationInstanceDirectory instances,
        NavigationAreaCatalog areaCatalog,
        PersistentGridConfigurationMap<string> mapIndex,
        NavigationSurfaceComponentIndex surfaceComponents,
        NavigationExplicitConnectionIndex explicitConnections,
        NavigationAutomaticSeamIndex automaticSeams,
        NavigationTransitionPageRoot transitionPages,
        NavigationTransitionRuleTable transitionRules,
        NavigationSurfaceComponentKeySet closedStructuralComponents,
        NavigationSurfaceComponentKeySet additionalClosedStructuralComponents,
        bool allStructuralComponentsClosed,
        long retainedBytes,
        int persistentPageCount)
    {
        GraphVersion = graphVersion;
        _instances = instances;
        AreaCatalog = areaCatalog;
        _mapIndex = mapIndex;
        SurfaceComponents = surfaceComponents;
        _explicitConnections = explicitConnections;
        _automaticSeams = automaticSeams;
        _transitionPages = transitionPages;
        TransitionRules = transitionRules;
        _closedStructuralComponents = closedStructuralComponents;
        _additionalClosedStructuralComponents = additionalClosedStructuralComponents;
        _allStructuralComponentsClosed = allStructuralComponentsClosed;
        RetainedBytes = retainedBytes;
        PersistentPageCount = persistentPageCount;
    }

    internal static NavigationWorldGraph Empty { get; } = new(0, Array.Empty<NavigationMapInstance>());

    internal static NavigationWorldGraph CreateEmpty(long graphVersion) =>
        new(graphVersion, Array.Empty<NavigationMapInstance>());

    internal long GraphVersion { get; }

    internal NavigationAreaCatalog AreaCatalog { get; }

    internal NavigationSurfaceComponentIndex SurfaceComponents { get; }

    internal NavigationExplicitConnectionIndex ExplicitConnections => _explicitConnections;

    internal NavigationAutomaticSeamIndex AutomaticSeams => _automaticSeams;

    internal NavigationTransitionRuleTable TransitionRules { get; }

    internal NavigationTransitionPageRoot TransitionPages => _transitionPages;

    internal bool TryGetMapId(GridConfigurationKey key, out string mapId) =>
        _mapIndex.TryGetValue(key, out mapId!);

    internal bool TryGetSeamPrism(
        NavigationCellAddress address,
        out GridCellPrism prism)
    {
        if (_instances.TryGet(address.MapId, out NavigationMapInstance instance)
            && instance.Map.GridBinding.TryGetCellPrism(address.Index, out prism))
        {
            return true;
        }
        prism = default;
        return false;
    }

    internal bool HasEffectiveCell(NavigationCellAddress address) =>
        _instances.TryGet(address.MapId, out NavigationMapInstance instance)
        && instance.TryGetSlot(address.Index, out int slot)
        && instance.TryGetEffectiveCell(slot, out _);

    internal bool TryGetSemanticState(
        NavigationCellAddress address,
        out NavigationCellSemanticSource source,
        out bool hasCell,
        out NavigationCell cell)
    {
        if (_instances.TryGet(address.MapId, out NavigationMapInstance instance))
        {
            return instance.TryGetSemanticState(
                address.Index,
                out source,
                out hasCell,
                out cell);
        }
        source = default;
        hasCell = false;
        cell = default;
        return false;
    }

    internal NavigationTransitionPage.Enumerator EnumerateOutgoingTransitions(
        NavigationMediumStateRef source)
    {
        if (!source.IsValid)
            return default;
        NavigationMapInstance instance = _instances.Get(source.Node.MapOrdinal);
        _transitionPages.TryGet(
            new NavigationTransitionPageAddress(
                instance.MapId,
                source.Node.CellSlot / NavigationSemanticPage.SlotCount),
            out NavigationTransitionPage page);
        return page?.GetOutgoingEnumerator(this, source) ?? default;
    }

    internal NavigationTransitionPage.Enumerator EnumerateOutgoingTransitionCandidates(
        NavigationMediumStateRef source)
    {
        if (!source.IsValid)
            return default;
        NavigationMapInstance instance = _instances.Get(source.Node.MapOrdinal);
        _transitionPages.TryGet(
            new NavigationTransitionPageAddress(
                instance.MapId,
                source.Node.CellSlot / NavigationSemanticPage.SlotCount),
            out NavigationTransitionPage page);
        return page?.GetOutgoingCandidateEnumerator(this, source) ?? default;
    }

    internal NavigationTransitionPage.Enumerator EnumerateIncomingTransitions(
        NavigationMediumStateRef destination)
    {
        if (!destination.IsValid)
            return default;
        NavigationMapInstance instance = _instances.Get(destination.Node.MapOrdinal);
        _transitionPages.TryGet(
            new NavigationTransitionPageAddress(
                instance.MapId,
                destination.Node.CellSlot / NavigationSemanticPage.SlotCount),
            out NavigationTransitionPage page);
        return page?.GetIncomingEnumerator(this, destination) ?? default;
    }

    internal NavigationTransitionPage.Enumerator EnumerateIncomingTransitionCandidates(
        NavigationMediumStateRef destination)
    {
        if (!destination.IsValid)
            return default;
        NavigationMapInstance instance = _instances.Get(destination.Node.MapOrdinal);
        _transitionPages.TryGet(
            new NavigationTransitionPageAddress(
                instance.MapId,
                destination.Node.CellSlot / NavigationSemanticPage.SlotCount),
            out NavigationTransitionPage page);
        return page?.GetIncomingCandidateEnumerator(this, destination) ?? default;
    }

    internal bool TryGetPublishedTransition(
        NavigationIncomingTransitionRef incoming,
        out NavigationPublishedTransition transition)
    {
        if (_transitionPages.TryGet(
                incoming.SourcePage,
                out NavigationTransitionPage page)
            && page.TryGetOutgoing(incoming.Owner, out transition))
        {
            return true;
        }
        transition = default;
        return false;
    }

    internal bool IsTransitionActive(
        NavigationPublishedTransition transition,
        NavigationMediumStateRef state,
        bool outgoing)
    {
        if (!TryGetMediumStateRef(
                transition.SourceAddress,
                transition.Definition.SourceMedium,
                out NavigationMediumStateRef source)
            || !TryGetMediumStateRef(
                transition.Definition.Destination,
                transition.Definition.DestinationMedium,
                out NavigationMediumStateRef destination))
        {
            return false;
        }
        return outgoing ? source.Equals(state) : destination.Equals(state);
    }

    internal bool IsTransitionEndpoint(
        NavigationPublishedTransition transition,
        NavigationMediumStateRef state,
        bool outgoing)
    {
        if (!TryGetNodeAddress(state.Node, out NavigationCellAddress address))
            return false;
        return outgoing
            ? state.Medium == transition.Definition.SourceMedium
                && address.Equals(transition.SourceAddress)
            : state.Medium == transition.Definition.DestinationMedium
                && address.Equals(transition.Definition.Destination);
    }

    internal int MapCount => _instances.Count;

    internal bool TryGetCoveredAddressGeneration(
        int configurationOrdinal,
        out string mapId,
        out GridCoveredAddressGeneration generation)
    {
        if ((uint)configurationOrdinal >= (uint)_mapIndex.Count)
        {
            mapId = string.Empty;
            generation = default;
            return false;
        }
        mapId = _mapIndex.GetValueAt(configurationOrdinal);
        return TryGetCoveredAddressGeneration(mapId, out generation);
    }

    internal bool TryGetCoveredAddressGeneration(
        string mapId,
        out GridCoveredAddressGeneration generation)
    {
        if (_instances.TryGet(mapId, out NavigationMapInstance instance)
            && instance.IsMaterialized)
        {
            NavigationGridGenerationIdentity identity = instance.GridIdentity;
            generation = new GridCoveredAddressGeneration(
                identity.ConfigurationKey,
                identity.GridIndex,
                identity.GridSpawnToken,
                instance.GridLastChangeSequence);
            return true;
        }
        generation = default;
        return false;
    }

    internal bool TryGetSurfaceComponent(
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationSurfaceComponentKey key,
        out long version)
    {
        if (SurfaceComponents.TryGet(address, medium, out NavigationSurfaceComponent component))
        {
            key = component.Key;
            version = component.Version;
            return true;
        }
        key = default;
        version = 0;
        return false;
    }

    internal bool AreInSameSurfaceComponent(
        NavigationCellAddress left,
        TraversalMedium leftMedium,
        NavigationCellAddress right,
        TraversalMedium rightMedium) =>
        SurfaceComponents.TryGet(left, leftMedium, out NavigationSurfaceComponent leftComponent)
        && SurfaceComponents.TryGet(right, rightMedium, out NavigationSurfaceComponent rightComponent)
        && leftComponent.Key == rightComponent.Key;

    internal long RetainedBytes { get; }

    internal int PersistentPageCount { get; }

    internal static long EmptyMapIndexRetainedBytes =>
        PersistentGridConfigurationMap<string>.Empty.RetainedBytes;

    internal static int EmptyMapIndexPersistentPageCount => 1;

    internal static long EmptyClosedStructuralComponentsRetainedBytes =>
        NavigationSurfaceComponentKeySet.Empty.RetainedBytes;

    internal static int EmptyClosedStructuralComponentsPersistentPageCount =>
        NavigationSurfaceComponentKeySet.Empty.PersistentPageCount;

    internal int LeaseCount => Volatile.Read(ref _leaseCount);

    internal NavigationMapInstance GetInstance(int mapOrdinal) => _instances.Get(mapOrdinal);

    internal bool IsWithinDynamicSlotCapacity(int maximumPerMap, int maximumTotal)
    {
        int total = 0;
        for (int i = 0; i < _instances.Count; i++)
        {
            int count = _instances.Get(i).DynamicSlotCount;
            if (count > maximumPerMap || count > maximumTotal - total)
                return false;
            total += count;
        }
        return true;
    }

    internal void Checkout() => Interlocked.Increment(ref _leaseCount);

    internal void Return() => Interlocked.Decrement(ref _leaseCount);

    internal bool TryGetMap(string mapId, out NavigationMapInstance? instance)
    {
        bool found = _instances.TryGet(mapId, out NavigationMapInstance foundInstance);
        instance = found ? foundInstance : null;
        return found;
    }

    internal bool IsSurfaceAddressClosed(
        NavigationCellAddress address,
        TraversalMedium medium) =>
        _allStructuralComponentsClosed
        || (SurfaceComponents.TryGet(
                address,
                medium,
                out NavigationSurfaceComponent component)
            && (_closedStructuralComponents.Contains(component.Key)
                || _additionalClosedStructuralComponents.Contains(component.Key)));

    internal bool IsSurfaceComponentClosed(NavigationSurfaceComponentKey key) =>
        _allStructuralComponentsClosed
        || _closedStructuralComponents.Contains(key)
        || _additionalClosedStructuralComponents.Contains(key);

    internal NavigationWorldGraph WithSurfaceComponents(
        NavigationSurfaceComponentIndex surfaceComponents)
    {
        if (ReferenceEquals(surfaceComponents, SurfaceComponents))
            return this;
        return new NavigationWorldGraph(
            GraphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            surfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            _closedStructuralComponents,
            _additionalClosedStructuralComponents,
            _allStructuralComponentsClosed,
            checked(RetainedBytes - SurfaceComponents.RetainedBytes
                + surfaceComponents.RetainedBytes),
            PersistentPageCount - SurfaceComponents.PersistentPageCount
                + surfaceComponents.PersistentPageCount);
    }

    internal NavigationWorldGraph WithAutomaticSeams(NavigationAutomaticSeamIndex seams)
    {
        if (ReferenceEquals(seams, _automaticSeams))
            return this;
        return new NavigationWorldGraph(
            GraphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            seams,
            _transitionPages,
            TransitionRules,
            _closedStructuralComponents,
            _additionalClosedStructuralComponents,
            _allStructuralComponentsClosed,
            checked(RetainedBytes - _automaticSeams.RetainedBytes + seams.RetainedBytes),
            PersistentPageCount - _automaticSeams.PersistentPageCount
                + seams.PersistentPageCount);
    }

    internal NavigationWorldGraph WithTransitionPublication(
        NavigationTransitionPageRoot pages,
        NavigationTransitionRuleTable rules)
    {
        if (ReferenceEquals(pages, _transitionPages)
            && ReferenceEquals(rules, TransitionRules))
        {
            return this;
        }
        long priorBytes = checked(
            _transitionPages.RetainedBytes
            + TransitionRules.RetainedBytes);
        long nextBytes = checked(
            pages.RetainedBytes
            + rules.RetainedBytes);
        int priorPages = checked(
            _transitionPages.PersistentPageCount
            + TransitionRules.PersistentPageCount);
        int nextPages = checked(
            pages.PersistentPageCount
            + rules.PersistentPageCount);
        return new NavigationWorldGraph(
            GraphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            pages,
            rules,
            _closedStructuralComponents,
            _additionalClosedStructuralComponents,
            _allStructuralComponentsClosed,
            checked(RetainedBytes - priorBytes + nextBytes),
            PersistentPageCount - priorPages + nextPages);
    }

    internal static bool HasStructuralChanges(
        NavigationOperationFrameChange[] changes,
        int changeCount,
        NavigationOperationCandidate candidate,
        NavigationWorldGraph current)
    {
        if (!ReferenceEquals(candidate.ExplicitConnections, current._explicitConnections))
            return true;
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                return true;
            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                change.PreparedOverlay!.Transaction.MapSpan;
            for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
            {
                if (!maps[mapIndex].CellSpan.IsEmpty
                    || !maps[mapIndex].ConnectionSpan.IsEmpty
                    || !maps[mapIndex].TransitionSpan.IsEmpty)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal NavigationWorldGraph WithClosedStructuralComponents(
        NavigationSurfaceComponentKeySet closed,
        bool closeAllStructuralComponents,
        long graphVersion)
    {
        if (ReferenceEquals(closed, _closedStructuralComponents)
            && ReferenceEquals(
                _additionalClosedStructuralComponents,
                NavigationSurfaceComponentKeySet.Empty)
            && closeAllStructuralComponents == _allStructuralComponentsClosed
            && graphVersion == GraphVersion)
        {
            return this;
        }
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            closed,
            NavigationSurfaceComponentKeySet.Empty,
            closeAllStructuralComponents,
            checked(RetainedBytes - GetClosedRootRetainedBytes()
                + closed.RetainedBytes),
            PersistentPageCount - GetClosedRootPersistentPages()
                + closed.PersistentPageCount);
    }

    internal NavigationWorldGraph WithOwnedStructuralClosure(
        NavigationSurfaceComponentKeySet baseline,
        NavigationSurfaceComponentKeySet affected,
        bool closeAllStructuralComponents,
        long graphVersion)
    {
        if (closeAllStructuralComponents)
        {
            return WithClosedStructuralComponents(
                NavigationSurfaceComponentKeySet.Empty,
                true,
                graphVersion);
        }
        NavigationSurfaceComponentKeySet additional = ReferenceEquals(baseline, affected)
            ? NavigationSurfaceComponentKeySet.Empty
            : affected;
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            baseline,
            additional,
            false,
            checked(RetainedBytes - GetClosedRootRetainedBytes()
                + baseline.RetainedBytes
                + GetAdditionalClosedRootRetainedBytes(baseline, additional)),
            PersistentPageCount - GetClosedRootPersistentPages()
                + baseline.PersistentPageCount
                + GetAdditionalClosedRootPersistentPages(baseline, additional));
    }

    internal bool HasClosedStructuralScope =>
        _allStructuralComponentsClosed
        || _closedStructuralComponents.Count != 0
        || _additionalClosedStructuralComponents.Count != 0;

    internal NavigationSurfaceComponentKeySet ClosedStructuralComponents =>
        _closedStructuralComponents;

    internal bool RetainsClosedComponentRoot(NavigationSurfaceComponentKeySet root) =>
        ReferenceEquals(root, _closedStructuralComponents)
        || ReferenceEquals(root, _additionalClosedStructuralComponents);

    private long GetClosedRootRetainedBytes() => checked(
        _closedStructuralComponents.RetainedBytes
        + GetAdditionalClosedRootRetainedBytes(
            _closedStructuralComponents,
            _additionalClosedStructuralComponents));

    private int GetClosedRootPersistentPages() => checked(
        _closedStructuralComponents.PersistentPageCount
        + GetAdditionalClosedRootPersistentPages(
            _closedStructuralComponents,
            _additionalClosedStructuralComponents));

    private static long GetAdditionalClosedRootRetainedBytes(
        NavigationSurfaceComponentKeySet baseline,
        NavigationSurfaceComponentKeySet additional) =>
        ReferenceEquals(additional, NavigationSurfaceComponentKeySet.Empty)
        || ReferenceEquals(additional, baseline)
            ? 0L
            : additional.RetainedBytes;

    private static int GetAdditionalClosedRootPersistentPages(
        NavigationSurfaceComponentKeySet baseline,
        NavigationSurfaceComponentKeySet additional) =>
        ReferenceEquals(additional, NavigationSurfaceComponentKeySet.Empty)
        || ReferenceEquals(additional, baseline)
            ? 0
            : additional.PersistentPageCount;

    internal bool AreAllStructuralComponentsClosed => _allStructuralComponentsClosed;

    internal NavigationWorldGraph ReopenStructuralScopes(long graphVersion)
    {
        if (!_allStructuralComponentsClosed
            && _closedStructuralComponents.Count == 0
            && _additionalClosedStructuralComponents.Count == 0)
            return this;
        NavigationSurfaceComponentKeySet open = NavigationSurfaceComponentKeySet.Empty;
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            open,
            NavigationSurfaceComponentKeySet.Empty,
            false,
            checked(RetainedBytes - GetClosedRootRetainedBytes() + open.RetainedBytes),
            PersistentPageCount - GetClosedRootPersistentPages()
                + open.PersistentPageCount);
    }

    internal int CaptureMaintenanceSnapshot(
        GridWorld world,
        NavigationWorldGraph previousGraph,
        ReadOnlySpan<GridEventInfo> events,
        ReadOnlySpan<NavigationGridChangeScope> resnapshotScopes,
        bool resnapshotAll,
        ReadOnlySpan<NavigationGridChangeScope> blockedScopes,
        bool blockAll,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        MaintenanceWorkMeter maintenanceMeter,
        long maximumActiveSnapshotBytes,
        int maximumPersistentGraphPages,
        NavigationGridBaselineCapture[] baselineCaptures,
        ref PersistentStringMap<NavigationBaselineRebuild> baselineRebuilds,
        Span<VoxelIndex> baselineAddressScratch,
        Span<GridCoveredAddress> baselineCoveredAddressScratch,
        Span<NavigationGridChangeScope> deferredScopes,
        out int deferredScopeCount,
        out bool deferAll,
        int[] affectedOrdinals,
        int[] affectedStamps,
        ref int affectedStamp,
        out int affectedCollectionCount)
    {
        deferredScopeCount = 0;
        deferAll = false;
        affectedCollectionCount = 0;
        for (int changeIndex = 0; changeIndex < changeCount; changeIndex++)
        {
            if (changes[changeIndex].Kind == NavigationOperationFrameChangeKind.MapRemove)
                baselineRebuilds = baselineRebuilds.Remove(changes[changeIndex].MapId!, out _);
        }
        if (_instances.Count == 0)
            return 0;

        int affectedCount = CollectAffectedMaps(
            events,
            resnapshotScopes,
            resnapshotAll,
            blockedScopes,
            blockAll,
            changes,
            changeCount,
            affectedOrdinals,
            affectedStamps,
            ref affectedStamp);
        affectedCollectionCount = affectedCount;
        NavigationBaselineRebuild.GetRetainedTotals(
            baselineRebuilds,
            out long rebuildRetainedBytes,
            out int rebuildPersistentPages);
        int remainingBaselineAddresses = maintenanceMeter.RemainingBaselineAddresses;
        for (int affectedIndex = 0; affectedIndex < affectedCount; affectedIndex++)
        {
            int mapIndex = affectedOrdinals[affectedIndex];
            baselineCaptures[mapIndex] = default;
            NavigationMapInstance current = _instances.Get(mapIndex);
            GridConfigurationKey configurationKey = current.Map.GridBinding.Key;
            bool isBlocked = blockAll || ContainsScope(blockedScopes, configurationKey);
            bool requiresRecoveryBaseline = resnapshotAll
                || ContainsScope(resnapshotScopes, configurationKey)
                || EventsRequireBaseline(
                    events,
                    configurationKey,
                    current.Map.DefaultCell.HasValue);
            bool operationChanged = !current.IsMaterialized
                && IsOperationChanged(changes, changeCount, current.MapId);
            bool requiresBaseline = requiresRecoveryBaseline || operationChanged;
            if (isBlocked || !requiresBaseline)
                continue;
            NavigationMapInstance? prior = null;
            bool isDelta = !requiresRecoveryBaseline
                && IsCellOverlayOnlyChanged(changes, changeCount, current.MapId)
                && previousGraph.TryGetMap(current.MapId, out prior)
                && prior != null
                && prior.IsMaterialized;
            baselineRebuilds.TryGetValue(
                current.MapId,
                out NavigationBaselineRebuild? rebuild);
            if (rebuild != null && !rebuild.Matches(current))
            {
                RemoveBaselineRebuild(
                    ref baselineRebuilds,
                    current.MapId,
                    rebuild,
                    ref rebuildRetainedBytes,
                    ref rebuildPersistentPages);
                rebuild = null;
            }
            if (rebuild != null
                && rebuild.TryGetCompletedCapture(
                    out NavigationGridBaselineCapture readyCapture))
            {
                baselineCaptures[mapIndex] = readyCapture;
                continue;
            }
            int addressCount;
            bool requiresDefaultDiscovery = !isDelta && current.Map.DefaultCell.HasValue;
            if (isDelta)
            {
                if (rebuild != null)
                {
                    RemoveBaselineRebuild(
                        ref baselineRebuilds,
                        current.MapId,
                        rebuild,
                        ref rebuildRetainedBytes,
                        ref rebuildPersistentPages);
                    rebuild = null;
                }
                addressCount = current.CopyNewCanonicalAddresses(
                    prior!,
                    baselineAddressScratch);
            }
            else if (rebuild == null && !requiresDefaultDiscovery)
            {
                addressCount = current.AddressCount <= remainingBaselineAddresses
                    ? current.CopyCanonicalAddresses(baselineAddressScratch)
                    : 0;
            }
            else
            {
                addressCount = 0;
            }

            bool requiresChunkedBaseline = !isDelta
                && (requiresDefaultDiscovery
                    || rebuild != null
                    || current.AddressCount > remainingBaselineAddresses);
            if (!requiresChunkedBaseline && addressCount <= remainingBaselineAddresses)
            {
                if (!maintenanceMeter.TryConsumeBaselineAddresses(addressCount))
                    continue;
                remainingBaselineAddresses -= addressCount;
                GridNavigationBaseline? baseline = null;
                if (addressCount > 0 || !isDelta)
                {
                    world.TryCaptureNavigationBaseline(
                        configurationKey,
                        baselineAddressScratch.Slice(0, addressCount),
                        out baseline);
                }
                baselineCaptures[mapIndex] = new NavigationGridBaselineCapture(
                    addressCount,
                    baseline,
                    isDelta);
            }
            else
            {
                if (!isDelta)
                {
                    if (rebuild == null)
                    {
                        var proposed = new NavigationBaselineRebuild(current);
                        if (TryAddBaselineRebuild(
                                ref baselineRebuilds,
                                proposed,
                                RetainedBytes,
                                PersistentPageCount,
                                maximumActiveSnapshotBytes,
                                maximumPersistentGraphPages,
                                ref rebuildRetainedBytes,
                                ref rebuildPersistentPages))
                        {
                            rebuild = proposed;
                        }
                    }
                    if (rebuild != null && !rebuild.RequiresCoveredDiscovery)
                    {
                        long beforeBytes = rebuild.RetainedBytes;
                        int beforePages = rebuild.PersistentPageCount;
                        long otherBytes = rebuildRetainedBytes - beforeBytes;
                        int otherPages = rebuildPersistentPages - beforePages;
                        long maximumRebuildBytes = Math.Max(
                            0,
                            maximumActiveSnapshotBytes - RetainedBytes - otherBytes);
                        int maximumRebuildPages = Math.Max(
                            0,
                            maximumPersistentGraphPages - PersistentPageCount - otherPages);
                        int consumed = rebuild.Advance(
                            world,
                            current,
                            remainingBaselineAddresses,
                            maximumRebuildBytes,
                            maximumRebuildPages,
                            baselineAddressScratch,
                            baselineCoveredAddressScratch,
                            out NavigationGridBaselineCapture completedCapture,
                            out bool completed);
                        rebuildRetainedBytes = checked(
                            rebuildRetainedBytes - beforeBytes + rebuild.RetainedBytes);
                        rebuildPersistentPages = checked(
                            rebuildPersistentPages - beforePages + rebuild.PersistentPageCount);
                        maintenanceMeter.TryConsumeBaselineAddresses(consumed);
                        remainingBaselineAddresses -= consumed;
                        if (completed)
                        {
                            baselineCaptures[mapIndex] = completedCapture;
                            continue;
                        }
                    }
                }
                NavigationGridGenerationIdentity deferredIdentity = current.GridIdentity;
                if (!deferredIdentity.IsValid
                    && previousGraph.TryGetMap(current.MapId, out NavigationMapInstance? deferredPrior)
                    && deferredPrior != null)
                {
                    deferredIdentity = deferredPrior.GridIdentity;
                }
                if (deferredScopeCount < deferredScopes.Length)
                {
                    deferredScopes[deferredScopeCount++] = new NavigationGridChangeScope(
                        configurationKey,
                        deferredIdentity.WorldSpawnToken,
                        deferredIdentity.GridIndex,
                        deferredIdentity.GridSpawnToken);
                }
                else
                {
                    deferredScopeCount = 0;
                    deferAll = true;
                }
            }
        }
        return affectedCount;
    }

    private static bool TryAddBaselineRebuild(
        ref PersistentStringMap<NavigationBaselineRebuild> rebuilds,
        NavigationBaselineRebuild rebuild,
        long graphRetainedBytes,
        int graphPersistentPages,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        ref long rebuildRetainedBytes,
        ref int rebuildPersistentPages)
    {
        PersistentStringMap<NavigationBaselineRebuild> next = rebuilds.Set(
            rebuild.MapId,
            rebuild);
        long priorRegistryBytes = rebuilds.Count == 0 ? 0 : rebuilds.RetainedBytes;
        int priorRegistryPages = rebuilds.Count == 0
            ? 0
            : 1 + rebuilds.PersistentNodeCount;
        long nextRetainedBytes = checked(
            rebuildRetainedBytes
            - priorRegistryBytes
            + next.RetainedBytes
            + rebuild.RetainedBytes);
        int nextPersistentPages = checked(
            rebuildPersistentPages
            - priorRegistryPages
            + 1
            + next.PersistentNodeCount
            + rebuild.PersistentPageCount);
        if (graphRetainedBytes > maximumRetainedBytes - nextRetainedBytes
            || graphPersistentPages > maximumPersistentPages - nextPersistentPages)
        {
            return false;
        }

        rebuilds = next;
        rebuildRetainedBytes = nextRetainedBytes;
        rebuildPersistentPages = nextPersistentPages;
        return true;
    }

    private static void RemoveBaselineRebuild(
        ref PersistentStringMap<NavigationBaselineRebuild> rebuilds,
        string mapId,
        NavigationBaselineRebuild rebuild,
        ref long rebuildRetainedBytes,
        ref int rebuildPersistentPages)
    {
        long priorRegistryBytes = rebuilds.RetainedBytes;
        int priorRegistryPages = 1 + rebuilds.PersistentNodeCount;
        PersistentStringMap<NavigationBaselineRebuild> next = rebuilds.Remove(mapId, out bool removed);
        if (!removed)
            return;
        long nextRegistryBytes = next.Count == 0 ? 0 : next.RetainedBytes;
        int nextRegistryPages = next.Count == 0 ? 0 : 1 + next.PersistentNodeCount;
        rebuildRetainedBytes = checked(
            rebuildRetainedBytes
            - priorRegistryBytes
            - rebuild.RetainedBytes
            + nextRegistryBytes);
        rebuildPersistentPages = checked(
            rebuildPersistentPages
            - priorRegistryPages
            - rebuild.PersistentPageCount
            + nextRegistryPages);
        rebuilds = next;
    }

    internal NavigationWorldGraph ApplyMaintenanceSnapshot(
        long worldSpawnToken,
        NavigationWorldGraph previousGraph,
        ReadOnlySpan<GridEventInfo> events,
        ReadOnlySpan<NavigationGridChangeScope> resnapshotScopes,
        bool resnapshotAll,
        ReadOnlySpan<NavigationGridChangeScope> blockedScopes,
        bool blockAll,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        NavigationGridBaselineCapture[] baselineCaptures,
        int[] affectedOrdinals,
        int affectedCount,
        long graphVersion)
    {
        NavigationInstanceDirectory changed = _instances;
        long retainedBytes = RetainedBytes;
        int persistentPages = PersistentPageCount;
        for (int affectedIndex = 0; affectedIndex < affectedCount; affectedIndex++)
        {
            int mapIndex = affectedOrdinals[affectedIndex];
            NavigationMapInstance current = changed.Get(mapIndex);
            GridConfigurationKey configurationKey = current.Map.GridBinding.Key;
            bool isBlocked = blockAll || ContainsScope(blockedScopes, configurationKey);
            bool requiresRecoveryBaseline = resnapshotAll
                || ContainsScope(resnapshotScopes, configurationKey)
                || EventsRequireBaseline(
                    events,
                    configurationKey,
                    current.Map.DefaultCell.HasValue);
            bool requiresBaseline = requiresRecoveryBaseline
                || (!current.IsMaterialized && IsOperationChanged(changes, changeCount, current.MapId));
            NavigationMapInstance next;
            if (isBlocked)
            {
                next = current.FailClosed(graphVersion);
            }
            else if (!requiresBaseline)
            {
                next = current.ApplyBatch(worldSpawnToken, events, false, graphVersion);
            }
            else if (!requiresRecoveryBaseline
                && IsCellOverlayOnlyChanged(changes, changeCount, current.MapId)
                && previousGraph.TryGetMap(current.MapId, out NavigationMapInstance? prior)
                && prior != null)
            {
                next = current.MaterializeDelta(
                    prior,
                    baselineCaptures[mapIndex],
                    graphVersion).ApplyBatch(worldSpawnToken, events, false, graphVersion);
            }
            else
            {
                next = current.Materialize(baselineCaptures[mapIndex], graphVersion);
            }
            if (!ReferenceEquals(current, next))
            {
                retainedBytes = checked(retainedBytes - current.RetainedBytes + next.RetainedBytes);
                persistentPages += next.PersistentPageCount - current.PersistentPageCount;
                changed = changed.With(mapIndex, next);
            }
        }
        if (ReferenceEquals(changed, _instances))
            return this;
        var updated = new NavigationWorldGraph(
                graphVersion,
                changed,
                AreaCatalog,
                _mapIndex,
                SurfaceComponents,
                _explicitConnections,
                _automaticSeams,
                _transitionPages,
                TransitionRules,
                _closedStructuralComponents,
                _additionalClosedStructuralComponents,
                _allStructuralComponentsClosed,
                retainedBytes,
                persistentPages);
        return updated;
    }

    internal NavigationWorldGraph WithAreaCatalog(
        NavigationAreaCatalog catalog,
        long graphVersion)
    {
        if (ReferenceEquals(catalog, AreaCatalog))
            return this;
        long retainedBytes = checked(RetainedBytes - AreaCatalog.RetainedBytes + catalog.RetainedBytes);
        int persistentPages = PersistentPageCount - AreaCatalog.PersistentPageCount + catalog.PersistentPageCount;
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            catalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            _closedStructuralComponents,
            _additionalClosedStructuralComponents,
            _allStructuralComponentsClosed,
            retainedBytes,
            persistentPages);
    }

    internal NavigationWorldGraph WithGraphVersion(long graphVersion)
    {
        if (graphVersion == GraphVersion)
            return this;
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            SurfaceComponents,
            _explicitConnections,
            _automaticSeams,
            _transitionPages,
            TransitionRules,
            _closedStructuralComponents,
            _additionalClosedStructuralComponents,
            _allStructuralComponentsClosed,
            RetainedBytes,
            PersistentPageCount);
    }

    internal bool TryGetDependencyStamp(
        NavigationAreaPolicyKey areaPolicy,
        ReadOnlySpan<NavigationSurfaceComponentKey> componentKeys,
        ReadOnlySpan<GraphPageDependencyAddress> pageAddresses,
        out GraphDependencyStamp stamp) => TryGetDependencyStamp(
        areaPolicy,
        componentKeys,
        pageAddresses,
        includeTransitionRules: false,
        out stamp);

    internal bool TryGetDependencyStamp(
        NavigationAreaPolicyKey areaPolicy,
        ReadOnlySpan<NavigationSurfaceComponentKey> componentKeys,
        ReadOnlySpan<GraphPageDependencyAddress> pageAddresses,
        bool includeTransitionRules,
        out GraphDependencyStamp stamp)
    {
        if (!AreaCatalog.TryGet(areaPolicy, out _))
        {
            stamp = null!;
            return false;
        }

        var components = new GraphComponentDependency[componentKeys.Length];
        NavigationSurfaceComponentKey priorComponent = default;
        for (int i = 0; i < componentKeys.Length; i++)
        {
            NavigationSurfaceComponentKey key = componentKeys[i];
            if ((i > 0 && priorComponent.CompareTo(key) >= 0)
                || !TryGetComponentDependency(key, out components[i]))
            {
                stamp = null!;
                return false;
            }
            priorComponent = key;
        }

        var pages = new GraphPageDependency[pageAddresses.Length];
        string? priorMapId = null;
        int priorPageIndex = -1;
        for (int i = 0; i < pageAddresses.Length; i++)
        {
            GraphPageDependencyAddress address = pageAddresses[i];
            int mapComparison = priorMapId == null
                ? 1
                : string.CompareOrdinal(address.MapId, priorMapId);
            if (string.IsNullOrEmpty(address.MapId)
                || address.PageIndex < 0
                || mapComparison < 0
                || (mapComparison == 0 && address.PageIndex <= priorPageIndex)
                || !TryGetPageDependency(
                    address,
                    includeTransitionRules,
                    out pages[i]))
            {
                stamp = null!;
                return false;
            }
            priorMapId = address.MapId;
            priorPageIndex = address.PageIndex;
        }

        stamp = new GraphDependencyStamp(
            areaPolicy,
            components,
            pages,
            includeTransitionRules,
            TransitionRules.Version);
        return true;
    }

    internal bool TryGetComponentDependency(
        NavigationSurfaceComponentKey key,
        out GraphComponentDependency dependency)
    {
        if (!IsSurfaceComponentClosed(key)
            && SurfaceComponents.TryGet(key, out NavigationSurfaceComponent component)
            && component.Key == key)
        {
            dependency = new GraphComponentDependency(
                key,
                component.Version);
            return true;
        }
        dependency = default;
        return false;
    }

    internal bool TryGetPageDependency(
        GraphPageDependencyAddress address,
        out GraphPageDependency dependency) => TryGetPageDependency(
        address,
        includeTransitionPage: false,
        out dependency);

    internal bool TryGetPageDependency(
        GraphPageDependencyAddress address,
        bool includeTransitionPage,
        out GraphPageDependency dependency)
    {
        if (!string.IsNullOrEmpty(address.MapId)
            && address.PageIndex >= 0
            && TryGetMap(address.MapId, out NavigationMapInstance? instance)
            && instance != null)
        {
            long transitionVersion = includeTransitionPage && _transitionPages.TryGet(
                new NavigationTransitionPageAddress(address.MapId, address.PageIndex),
                out NavigationTransitionPage transitionPage)
                    ? transitionPage.Version
                    : 0;
            dependency = instance.GetPageDependency(address.PageIndex, transitionVersion);
            return true;
        }
        dependency = default;
        return false;
    }

    internal bool IsDependencyCurrent(GraphDependencyStamp stamp)
    {
        if (stamp == null
            || !AreaCatalog.TryGet(stamp.AreaPolicy, out _))
            return false;
        if (stamp.HasTransitionRuleDependency
            && stamp.TransitionRuleVersion != TransitionRules.Version)
        {
            return false;
        }
        for (int component = 0; component < stamp.Components.Length; component++)
        {
            GraphComponentDependency dependency = stamp.Components[component];
            if (IsSurfaceComponentClosed(dependency.Key)
                || !SurfaceComponents.TryGet(
                    dependency.Key,
                    out NavigationSurfaceComponent current)
                || current.Key != dependency.Key
                || current.Version != dependency.Version)
                return false;
        }
        for (int page = 0; page < stamp.Pages.Length; page++)
        {
            GraphPageDependency dependency = stamp.Pages[page];
            if (!TryGetMap(dependency.MapId, out NavigationMapInstance? instance)
                || instance == null
                || !TryGetPageDependency(
                    new GraphPageDependencyAddress(dependency.MapId, dependency.PageIndex),
                    stamp.HasTransitionRuleDependency,
                    out GraphPageDependency current)
                || !current.Equals(dependency))
                return false;
        }
        return true;
    }

    private static PersistentGridConfigurationMap<string> BuildMapIndex(
        NavigationMapInstance[] instances)
    {
        PersistentGridConfigurationMap<string> index = PersistentGridConfigurationMap<string>.Empty;
        for (int i = 0; i < instances.Length; i++)
            index = index.Set(instances[i].Map.GridBinding.Key, instances[i].MapId);
        return index;
    }

    private int FindMapOrdinal(string mapId)
    {
        int low = 0;
        int high = _instances.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(_instances.GetMapId(middle), mapId);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return -1;
    }

    private int CollectAffectedMaps(
        ReadOnlySpan<GridEventInfo> events,
        ReadOnlySpan<NavigationGridChangeScope> resnapshotScopes,
        bool resnapshotAll,
        ReadOnlySpan<NavigationGridChangeScope> blockedScopes,
        bool blockAll,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        int[] ordinals,
        int[] stamps,
        ref int stamp)
    {
        if (++stamp == 0)
        {
            Array.Clear(stamps, 0, stamps.Length);
            stamp = 1;
        }

        int count = 0;
        bool all = resnapshotAll || blockAll;
        for (int i = 0; i < events.Length && !all; i++)
        {
            if (events[i].ChangeKind == GridEventKind.WorldReset)
            {
                all = true;
                break;
            }
            MarkConfiguration(events[i].Configuration.ToGridKey(), ordinals, ref count, stamps, stamp);
        }

        for (int i = 0; i < resnapshotScopes.Length && !all; i++)
            MarkConfiguration(resnapshotScopes[i].ConfigurationKey, ordinals, ref count, stamps, stamp);
        for (int i = 0; i < blockedScopes.Length && !all; i++)
            MarkConfiguration(blockedScopes[i].ConfigurationKey, ordinals, ref count, stamps, stamp);
        MarkOperationChanges(changes, changeCount, ordinals, ref count, stamps, stamp);

        if (all)
        {
            count = _instances.Count;
            for (int i = 0; i < count; i++)
                ordinals[i] = i;
        }
        else
            Array.Sort(ordinals, 0, count);
        return count;
    }

    private void MarkOperationChanges(
        NavigationOperationFrameChange[] changes,
        int changeCount,
        int[] ordinals,
        ref int count,
        int[] stamps,
        int stamp)
    {
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind == NavigationOperationFrameChangeKind.MapCommit)
            {
                MarkMapId(change.MapId!, ordinals, ref count, stamps, stamp);
                continue;
            }
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                continue;
            ReadOnlySpan<NavigationMapOverlayDelta> deltas = change.PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
                MarkMapId(deltas[delta].MapId, ordinals, ref count, stamps, stamp);
        }
    }

    private void MarkMapId(
        string mapId,
        int[] ordinals,
        ref int count,
        int[] stamps,
        int stamp)
    {
        int ordinal = FindMapOrdinal(mapId);
        MarkOrdinal(ordinal, ordinals, ref count, stamps, stamp);
    }

    private void MarkConfiguration(
        GridConfigurationKey key,
        int[] ordinals,
        ref int count,
        int[] stamps,
        int stamp)
    {
        if (_mapIndex.TryGetValue(key, out string mapId))
        {
            int ordinal = FindMapOrdinal(mapId);
            MarkOrdinal(ordinal, ordinals, ref count, stamps, stamp);
        }
    }

    private static void MarkOrdinal(
        int ordinal,
        int[] ordinals,
        ref int count,
        int[] stamps,
        int stamp)
    {
        if (ordinal < 0 || stamps[ordinal] == stamp)
            return;
        stamps[ordinal] = stamp;
        ordinals[count++] = ordinal;
    }

    private static bool ContainsScope(
        ReadOnlySpan<NavigationGridChangeScope> scopes,
        GridConfigurationKey key)
    {
        for (int i = 0; i < scopes.Length; i++)
        {
            if (scopes[i].ConfigurationKey.Equals(key))
                return true;
        }
        return false;
    }

    private static bool EventsRequireBaseline(
        ReadOnlySpan<GridEventInfo> events,
        GridConfigurationKey key,
        bool discoverDefaultPresence)
    {
        for (int i = 0; i < events.Length; i++)
        {
            if ((events[i].ChangeKind == GridEventKind.GridAdded
                    || events[i].ChangeKind == GridEventKind.GridChanged
                    || discoverDefaultPresence
                        && (events[i].ChangeKind == GridEventKind.SparseVoxelAdded
                            || events[i].ChangeKind == GridEventKind.SparseVoxelRemoved))
                && events[i].Configuration.ToGridKey().Equals(key))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsOperationChanged(
        NavigationOperationFrameChange[] changes,
        int changeCount,
        string mapId)
    {
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind == NavigationOperationFrameChangeKind.MapCommit)
            {
                if (string.Equals(change.MapId, mapId, StringComparison.Ordinal))
                    return true;
                continue;
            }
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                continue;
            ReadOnlySpan<NavigationMapOverlayDelta> deltas = change.PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                if (string.Equals(deltas[delta].MapId, mapId, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static bool IsCellOverlayOnlyChanged(
        NavigationOperationFrameChange[] changes,
        int changeCount,
        string mapId)
    {
        bool found = false;
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind == NavigationOperationFrameChangeKind.MapCommit
                && string.Equals(change.MapId, mapId, StringComparison.Ordinal))
            {
                return false;
            }
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                continue;
            ReadOnlySpan<NavigationMapOverlayDelta> deltas = change.PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                if (string.Equals(deltas[delta].MapId, mapId, StringComparison.Ordinal))
                    found = true;
            }
        }
        return found;
    }
}
