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
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable context-owned navigation graph root.</summary>
internal sealed partial class NavigationWorldGraph
{
    internal const long BaseRetainedBytes = 128L;

    private readonly NavigationInstanceDirectory _instances;
    private readonly PersistentGridConfigurationMap<string> _mapIndex;
    private readonly PersistentStringMap<bool> _closedStructuralComponents;
    private int _leaseCount;

    internal NavigationWorldGraph(
        long graphVersion,
        NavigationMapInstance[] instances,
        NavigationAreaCatalog? areaCatalog = null,
        PersistentGridConfigurationMap<string>? mapIndex = null,
        NavigationCompositionIndex? composition = null)
    {
        GraphVersion = graphVersion;
        _instances = NavigationInstanceDirectory.Create(instances);
        AreaCatalog = areaCatalog ?? NavigationAreaCatalog.Empty;
        _mapIndex = mapIndex ?? BuildMapIndex(instances);
        Composition = composition ?? NavigationCompositionIndex.Build(instances, graphVersion);
        _closedStructuralComponents = PersistentStringMap<bool>.Empty;
        long bytes = checked(
            BaseRetainedBytes
            + _instances.RetainedBytes
            + _mapIndex.RetainedBytes
            + _closedStructuralComponents.RetainedBytes
            + Composition.RetainedBytes
            + AreaCatalog.RetainedBytes);
        PersistentPageCount = _instances.PersistentPageCount
            + 1 + _mapIndex.Count
            + _closedStructuralComponents.PersistentNodeCount
            + Composition.PersistentPageCount
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
        NavigationCompositionIndex composition,
        PersistentStringMap<bool> closedStructuralComponents,
        long retainedBytes,
        int persistentPageCount)
    {
        GraphVersion = graphVersion;
        _instances = instances;
        AreaCatalog = areaCatalog;
        _mapIndex = mapIndex;
        Composition = composition;
        _closedStructuralComponents = closedStructuralComponents;
        RetainedBytes = retainedBytes;
        PersistentPageCount = persistentPageCount;
    }

    internal static NavigationWorldGraph Empty { get; } = new(0, Array.Empty<NavigationMapInstance>());

    internal static NavigationWorldGraph CreateEmpty(long graphVersion) =>
        new(graphVersion, Array.Empty<NavigationMapInstance>());

    internal long GraphVersion { get; }

    internal NavigationAreaCatalog AreaCatalog { get; }

    internal NavigationCompositionIndex Composition { get; }

    internal int MapCount => _instances.Count;

    internal long RetainedBytes { get; }

    internal int PersistentPageCount { get; }

    internal static long EmptyMapIndexRetainedBytes =>
        PersistentGridConfigurationMap<string>.Empty.RetainedBytes;

    internal static int EmptyMapIndexPersistentPageCount => 1;

    internal static long EmptyClosedStructuralComponentsRetainedBytes =>
        PersistentStringMap<bool>.Empty.RetainedBytes;

    internal static int EmptyClosedStructuralComponentsPersistentPageCount =>
        PersistentStringMap<bool>.Empty.PersistentNodeCount;

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

    internal bool IsStructuralScopeClosed(string mapId) =>
        Composition.TryGetComponentKey(mapId, out string componentKey)
        && _closedStructuralComponents.ContainsKey(componentKey);

    internal static NavigationWorldGraph Compose(
        NavigationOperationCandidate candidate,
        NavigationWorldGraph previous,
        long graphVersion,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        if (previous.TryComposeCellOverlay(
                candidate,
                graphVersion,
                changes,
                changeCount,
                out NavigationWorldGraph? local))
        {
            return local!;
        }
        if (previous.TryComposeStructuralChanges(
                candidate,
                graphVersion,
                changes,
                changeCount,
                updateComposition: true,
                out local))
        {
            return local!;
        }
        NavigationOperationCandidate.MapState[] states = candidate.CaptureStates();
        var instances = new NavigationMapInstance[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            previous.TryGetMap(states[i].Map.MapId, out NavigationMapInstance? prior);
            instances[i] = NavigationMapInstance.ComposeDetached(states[i], prior, graphVersion);
        }
        return new NavigationWorldGraph(graphVersion, instances, previous.AreaCatalog);
    }

    internal static NavigationWorldGraph PrepareStructuralCandidate(
        NavigationOperationCandidate candidate,
        NavigationWorldGraph previous,
        long graphVersion,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        bool prepared = previous.TryComposeStructuralChanges(
            candidate,
            graphVersion,
            changes,
            changeCount,
            updateComposition: false,
            out NavigationWorldGraph? graph);
        SwiftCollections.Diagnostics.SwiftThrowHelper.ThrowIfTrue(
            !prepared || graph == null,
            nameof(NavigationWorldGraph),
            "A structural candidate requires at least one structural change.");
        return graph!;
    }

    internal NavigationCompositionIndex.UpdateWork BeginCompositionUpdate(
        NavigationWorldGraph source,
        ReadOnlySpan<string> changedMapIds,
        long version) => new(source.Composition, _instances, changedMapIds, version);

    internal NavigationWorldGraph WithComposition(NavigationCompositionIndex composition)
    {
        PersistentStringMap<bool> openComponents = PersistentStringMap<bool>.Empty;
        long bytes = checked(
            RetainedBytes
            - Composition.RetainedBytes
            + composition.RetainedBytes
            - _closedStructuralComponents.RetainedBytes
            + openComponents.RetainedBytes);
        int pages = PersistentPageCount - Composition.PersistentPageCount
            + composition.PersistentPageCount
            - _closedStructuralComponents.PersistentNodeCount;
        return new NavigationWorldGraph(
            GraphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            composition,
            openComponents,
            bytes,
            pages);
    }

    internal static bool HasStructuralChanges(
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
                return true;
            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                change.PreparedOverlay!.Transaction.MapSpan;
            for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
            {
                if (!maps[mapIndex].ConnectionSpan.IsEmpty
                    || !maps[mapIndex].TransitionSpan.IsEmpty)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal NavigationWorldGraph FailClosedStructuralScope(
        ReadOnlySpan<string> changedMapIds,
        long graphVersion)
    {
        if (changedMapIds.IsEmpty)
            return this;

        PersistentStringMap<bool> closed = _closedStructuralComponents;
        for (int i = 0; i < changedMapIds.Length; i++)
        {
            if (Composition.TryGetComponentKey(changedMapIds[i], out string componentKey))
                closed = closed.Set(componentKey, true);
        }
        return ReferenceEquals(closed, _closedStructuralComponents)
            ? this
            : new NavigationWorldGraph(
                graphVersion,
                _instances,
                AreaCatalog,
                _mapIndex,
                Composition,
                closed,
                checked(RetainedBytes - _closedStructuralComponents.RetainedBytes + closed.RetainedBytes),
                PersistentPageCount - _closedStructuralComponents.PersistentNodeCount
                    + closed.PersistentNodeCount);
    }

    internal NavigationWorldGraph ReopenStructuralScopes(long graphVersion)
    {
        if (_closedStructuralComponents.Count == 0)
            return this;
        PersistentStringMap<bool> open = PersistentStringMap<bool>.Empty;
        return new NavigationWorldGraph(
            graphVersion,
            _instances,
            AreaCatalog,
            _mapIndex,
            Composition,
            open,
            checked(RetainedBytes - _closedStructuralComponents.RetainedBytes + open.RetainedBytes),
            PersistentPageCount - _closedStructuralComponents.PersistentNodeCount);
    }

    private bool TryComposeStructuralChanges(
        NavigationOperationCandidate candidate,
        long graphVersion,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        bool updateComposition,
        out NavigationWorldGraph? graph)
    {
        if (changeCount == 0)
        {
            graph = null;
            return false;
        }

        NavigationInstanceDirectory directory = _instances;
        PersistentGridConfigurationMap<string> mapIndex = _mapIndex;
        NavigationCompositionIndex composition = Composition;
        long retainedBytes = RetainedBytes;
        int persistentPages = PersistentPageCount;
        int changedCapacity = 0;
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            changedCapacity = checked(changedCapacity +
                (change.Kind == NavigationOperationFrameChangeKind.Overlay
                    ? change.PreparedOverlay!.Transaction.MapSpan.Length
                    : 1));
        }
        var changedMapIds = new string[changedCapacity];
        int changedMapCount = 0;
        for (int i = 0; i < changeCount; i++)
        {
            NavigationOperationFrameChange change = changes[i];
            if (change.Kind == NavigationOperationFrameChangeKind.MapRemove)
            {
                if (!directory.TryGet(change.MapId!, out NavigationMapInstance current))
                    continue;
                NavigationInstanceDirectory nextDirectory =
                    directory.Remove(change.MapId!, out bool removed);
                if (!removed)
                    continue;
                retainedBytes = checked(
                    retainedBytes
                    - directory.RetainedBytes
                    + nextDirectory.RetainedBytes
                    - current.RetainedBytes);
                persistentPages +=
                    nextDirectory.PersistentPageCount
                    - directory.PersistentPageCount
                    - current.PersistentPageCount;
                directory = nextDirectory;

                PersistentGridConfigurationMap<string> nextMapIndex =
                    mapIndex.Remove(current.Map.GridBinding.Key);
                retainedBytes = checked(
                    retainedBytes - mapIndex.RetainedBytes + nextMapIndex.RetainedBytes);
                persistentPages += nextMapIndex.Count - mapIndex.Count;
                mapIndex = nextMapIndex;
                changedMapIds[changedMapCount++] = change.MapId!;
                continue;
            }

            if (change.Kind == NavigationOperationFrameChangeKind.MapCommit)
            {
                if (!candidate.TryGetState(change.MapId!, out NavigationOperationCandidate.MapState? state)
                    || state == null)
                {
                    graph = null;
                    return false;
                }
                bool hadPrior = directory.TryGet(
                    change.MapId!,
                    out NavigationMapInstance prior);
                NavigationMapInstance next = NavigationMapInstance.ComposeDetached(
                    state,
                    hadPrior ? prior : null,
                    graphVersion);
                NavigationInstanceDirectory nextDirectory = directory.Set(change.MapId!, next);
                retainedBytes = checked(
                    retainedBytes
                    - directory.RetainedBytes
                    + nextDirectory.RetainedBytes
                    + next.RetainedBytes
                    - (hadPrior ? prior.RetainedBytes : 0));
                persistentPages +=
                    nextDirectory.PersistentPageCount
                    - directory.PersistentPageCount
                    + next.PersistentPageCount
                    - (hadPrior ? prior.PersistentPageCount : 0);
                directory = nextDirectory;

                PersistentGridConfigurationMap<string> priorMapIndex = mapIndex;
                if (hadPrior && !prior.Map.GridBinding.Key.Equals(next.Map.GridBinding.Key))
                    mapIndex = mapIndex.Remove(prior.Map.GridBinding.Key);
                PersistentGridConfigurationMap<string> nextMapIndex =
                    mapIndex.Set(next.Map.GridBinding.Key, next.MapId);
                retainedBytes = checked(
                    retainedBytes - priorMapIndex.RetainedBytes + nextMapIndex.RetainedBytes);
                persistentPages += nextMapIndex.Count - priorMapIndex.Count;
                mapIndex = nextMapIndex;
                changedMapIds[changedMapCount++] = change.MapId!;
                continue;
            }

            ReadOnlySpan<NavigationMapOverlayDelta> deltas = change.PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                if (!candidate.TryGetState(deltas[delta].MapId, out NavigationOperationCandidate.MapState? state)
                    || state == null
                    || !directory.TryGet(deltas[delta].MapId, out NavigationMapInstance current))
                {
                    graph = null;
                    return false;
                }
                NavigationMapInstance next = NavigationMapInstance.ComposeDetached(
                    state,
                    current,
                    graphVersion);
                if (ReferenceEquals(current, next))
                    continue;
                NavigationInstanceDirectory nextDirectory =
                    directory.Set(deltas[delta].MapId, next);
                retainedBytes = checked(
                    retainedBytes
                    - directory.RetainedBytes
                    + nextDirectory.RetainedBytes
                    - current.RetainedBytes
                    + next.RetainedBytes);
                persistentPages +=
                    nextDirectory.PersistentPageCount
                    - directory.PersistentPageCount
                    - current.PersistentPageCount
                    + next.PersistentPageCount;
                directory = nextDirectory;
                changedMapIds[changedMapCount++] = deltas[delta].MapId;
            }
        }

        if (changedMapCount == 0)
        {
            graph = this;
            return true;
        }

        if (updateComposition)
        {
            composition = Composition.Update(
                directory,
                changedMapIds.AsSpan(0, changedMapCount),
                graphVersion);
        }
        retainedBytes = checked(
            retainedBytes - Composition.RetainedBytes + composition.RetainedBytes);
        persistentPages += composition.PersistentPageCount - Composition.PersistentPageCount;
        graph = new NavigationWorldGraph(
            graphVersion,
            directory,
            AreaCatalog,
            mapIndex,
            composition,
            _closedStructuralComponents,
            retainedBytes,
            persistentPages);
        return true;
    }

    private bool TryComposeCellOverlay(
        NavigationOperationCandidate candidate,
        long graphVersion,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        out NavigationWorldGraph? graph)
    {
        if (changeCount == 0)
        {
            graph = null;
            return false;
        }
        for (int i = 0; i < changeCount; i++)
        {
            if (changes[i].Kind != NavigationOperationFrameChangeKind.Overlay)
            {
                graph = null;
                return false;
            }
            ReadOnlySpan<NavigationMapOverlayDelta> deltas =
                changes[i].PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                if (!deltas[delta].ConnectionSpan.IsEmpty
                    || !deltas[delta].TransitionSpan.IsEmpty)
                {
                    graph = null;
                    return false;
                }
            }
        }

        NavigationInstanceDirectory directory = _instances;
        long retainedBytes = RetainedBytes;
        int persistentPages = PersistentPageCount;
        for (int i = 0; i < changeCount; i++)
        {
            ReadOnlySpan<NavigationMapOverlayDelta> deltas =
                changes[i].PreparedOverlay!.Transaction.MapSpan;
            for (int delta = 0; delta < deltas.Length; delta++)
            {
                int ordinal = FindMapOrdinal(deltas[delta].MapId);
                if (ordinal < 0
                    || !candidate.TryGetState(
                        deltas[delta].MapId,
                        out NavigationOperationCandidate.MapState? state)
                    || state == null)
                {
                    graph = null;
                    return false;
                }
                NavigationMapInstance current = directory.Get(ordinal);
                NavigationMapInstance next = current.ApplyCellOverlayDetached(
                    state,
                    deltas[delta].CellSpan,
                    graphVersion);
                if (ReferenceEquals(current, next))
                    continue;
                retainedBytes = checked(retainedBytes - current.RetainedBytes + next.RetainedBytes);
                persistentPages += next.PersistentPageCount - current.PersistentPageCount;
                directory = directory.With(ordinal, next);
            }
        }
        graph = ReferenceEquals(directory, _instances)
            ? this
            : new NavigationWorldGraph(
                graphVersion,
                directory,
                AreaCatalog,
                _mapIndex,
                Composition,
                _closedStructuralComponents,
                retainedBytes,
                persistentPages);
        return true;
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
                || EventsRequireBaseline(events, configurationKey);
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
            int addressCount;
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
            else if (rebuild == null)
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
                && (rebuild != null || current.AddressCount > remainingBaselineAddresses);
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
                    if (rebuild != null)
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
                || EventsRequireBaseline(events, configurationKey);
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
        return ReferenceEquals(changed, _instances)
            ? this
            : new NavigationWorldGraph(
                graphVersion,
                changed,
                AreaCatalog,
                _mapIndex,
                Composition,
                _closedStructuralComponents,
                retainedBytes,
                persistentPages);
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
            Composition,
            _closedStructuralComponents,
            retainedBytes,
            persistentPages);
    }

    internal bool TryGetDependencyStamp(
        NavigationAreaPolicyKey areaPolicy,
        ReadOnlySpan<string> componentRepresentativeMapIds,
        ReadOnlySpan<GraphPageDependencyAddress> pageAddresses,
        out GraphDependencyStamp stamp)
    {
        if (!AreaCatalog.TryGet(areaPolicy, out _))
        {
            stamp = null!;
            return false;
        }

        var components = new GraphComponentDependency[componentRepresentativeMapIds.Length];
        string? priorComponent = null;
        for (int i = 0; i < componentRepresentativeMapIds.Length; i++)
        {
            string representative = componentRepresentativeMapIds[i];
            if (string.IsNullOrEmpty(representative)
                || IsStructuralScopeClosed(representative)
                || (priorComponent != null
                    && string.CompareOrdinal(priorComponent, representative) >= 0)
                || !Composition.TryGetComponentRecord(
                    representative,
                    out NavigationStructuralComponent component)
                || !string.Equals(component.Key, representative, StringComparison.Ordinal))
            {
                stamp = null!;
                return false;
            }
            components[i] = new GraphComponentDependency(representative, component.Version);
            priorComponent = representative;
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
                || !TryGetMap(address.MapId, out NavigationMapInstance? instance)
                || instance == null
                || !Composition.TryGetComponentRecord(
                    address.MapId,
                    out NavigationStructuralComponent pageComponent)
                || !ContainsOrdinal(
                    componentRepresentativeMapIds,
                    pageComponent.Key))
            {
                stamp = null!;
                return false;
            }
            pages[i] = instance.GetPageDependency(address.PageIndex);
            priorMapId = address.MapId;
            priorPageIndex = address.PageIndex;
        }

        stamp = new GraphDependencyStamp(
            areaPolicy,
            components,
            pages);
        return true;
    }

    internal bool IsDependencyCurrent(GraphDependencyStamp stamp)
    {
        if (stamp == null || !AreaCatalog.TryGet(stamp.AreaPolicy, out _))
            return false;
        for (int component = 0; component < stamp.Components.Length; component++)
        {
            GraphComponentDependency dependency = stamp.Components[component];
            if (IsStructuralScopeClosed(dependency.RepresentativeMapId)
                || !Composition.TryGetComponentRecord(
                    dependency.RepresentativeMapId,
                    out NavigationStructuralComponent current)
                || !string.Equals(
                    current.Key,
                    dependency.RepresentativeMapId,
                    StringComparison.Ordinal)
                || current.Version != dependency.Version)
                return false;
        }
        for (int page = 0; page < stamp.Pages.Length; page++)
        {
            GraphPageDependency dependency = stamp.Pages[page];
            if (!TryGetMap(dependency.MapId, out NavigationMapInstance? instance)
                || instance == null
                || !instance.GetPageDependency(dependency.PageIndex).Equals(dependency))
                return false;
        }
        return true;
    }

    private static bool ContainsOrdinal(ReadOnlySpan<string> values, string value)
    {
        int low = 0;
        int high = values.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(values[middle], value);
            if (comparison == 0)
                return true;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
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
        GridConfigurationKey key)
    {
        for (int i = 0; i < events.Length; i++)
        {
            if ((events[i].ChangeKind == GridEventKind.GridAdded
                    || events[i].ChangeKind == GridEventKind.GridChanged)
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
