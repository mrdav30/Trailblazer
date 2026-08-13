//=======================================================================
// NavigationGraphRuntime.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Owns map operations, GridForge ingress, and immutable root publication for one context.</summary>
internal sealed class NavigationGraphRuntime : IDisposable
{
    private readonly GridWorld _world;
    private readonly NavigationOperationProcessor _operations;
    private readonly NavigationGridChangeIngress _ingress;
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationAreaCatalogProcessor _areaPolicies;
    private readonly PathQueryWorkspacePool _workspaces;
    private readonly NavigationQueryAdmissionGate _queryAdmission;
    private readonly MaintenanceWorkBudget _maintenanceBudget;
    private readonly MaintenanceWorkMeter _maintenanceMeter;
    private readonly int _maxDynamicSlotsPerMap;
    private readonly int _maxDynamicSlots;
    private readonly long _maxActiveSnapshotBytes;
    private readonly int _maxPersistentGraphPages;
    private readonly NavigationCandidatePublisher _publishCandidate;
    private readonly NavigationRetainedWorkGuard _retainedWorkGuard;
    private readonly Action _maintainSnapshot;
    private readonly GridEventInfo[] _maintenanceEvents;
    private readonly NavigationGridChangeScope[] _blockedScopes;
    private readonly NavigationGridChangeScope[] _resnapshotScopes;
    private readonly NavigationGridChangeScope[] _deferredScopes;
    private readonly NavigationGridChangeScope[] _priorBlockedScopes;
    private readonly NavigationGridBaselineCapture[] _baselineCaptures;
    private PersistentStringMap<NavigationBaselineRebuild> _baselineRebuilds =
        PersistentStringMap<NavigationBaselineRebuild>.Empty;
    private NavigationStructuralCompositionWork? _compositionWork;
    private readonly VoxelIndex[] _baselineAddressScratch;
    private int _publicationEventCount;
    private int _publicationBlockedScopeCount;
    private int _publicationResnapshotScopeCount;
    private NavigationAreaCatalog _publicationAreaCatalog = NavigationAreaCatalog.Empty;
    private bool _publicationResnapshotAll;
    private bool _publicationBlockAll;
    private NavigationWorldGraph _snapshotGraph = NavigationWorldGraph.Empty;
    private NavigationWorldGraph _snapshotPreviousGraph = NavigationWorldGraph.Empty;
    private NavigationOperationFrameChange[] _snapshotChanges = Array.Empty<NavigationOperationFrameChange>();
    private int _snapshotChangeCount;
    private int _snapshotAffectedCount;
    private int _snapshotDeferredScopeCount;
    private long _snapshotWorldSpawnToken;
    private bool _snapshotDeferAll;
    private int _priorBlockedScopeCount;
    private bool _priorBlockAll;
    private readonly int[] _affectedMapOrdinals;
    private readonly int[] _affectedMapStamps;
    private readonly string[] _operationStructuralMapIds;
    private int _affectedMapStamp;
    private int _lastAffectedMapCollectionCount;
    private int _lastCompletedCopiedNodes;
    private int _lastCompletedCopiedReverse;
    private int _lastCompletedCopiedComponents;
    private int _lastCompletedCopiedMemberships;
    private bool _publishedThisMaintenance;
    private bool _resetPending;
    private bool _disposed;

    internal NavigationGraphRuntime(GridWorld world, TrailblazerWorldContextSettings settings)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(settings, nameof(settings));
        _world = world;
        _operations = new NavigationOperationProcessor(
            settings.OperationLimits,
            int.MaxValue,
            settings.NavigationAreaCount);
        int ingressCapacity = NavigationGridChangeIngress.GetMaximumCapacity(
            settings.MaxIngressEntries,
            settings.MaxIngressBytes,
            settings.OperationLimits.MaxMaps);
        SwiftThrowHelper.ThrowIfArgument(
            ingressCapacity <= 0,
            nameof(settings),
            "Ingress byte capacity must fit at least one final-state event.");
        _ingress = new NavigationGridChangeIngress(
            ingressCapacity,
            settings.OperationLimits.MaxMaps);
        _store = new NavigationWorldGraphStore(
            settings.MaxActiveSnapshots,
            settings.MaxRetiredSnapshots,
            settings.MaxRetiredSnapshotBytes,
            settings.MaxActiveSnapshotBytes,
            settings.MaxPersistentGraphPages,
            settings.MaxConcurrentPathQueries,
            settings.MaxActiveQueryResultBytes);
        _areaPolicies = new NavigationAreaCatalogProcessor(settings);
        _workspaces = new PathQueryWorkspacePool(settings);
        _queryAdmission = new NavigationQueryAdmissionGate(
            _store,
            _workspaces,
            settings);
        _maintenanceBudget = settings.MaintenanceBudget;
        _maintenanceMeter = new MaintenanceWorkMeter(_maintenanceBudget);
        _maxDynamicSlotsPerMap = settings.MaxDynamicCellSlotsPerMap;
        _maxDynamicSlots = settings.MaxDynamicCellSlots;
        _maxActiveSnapshotBytes = settings.MaxActiveSnapshotBytes;
        _maxPersistentGraphPages = settings.MaxPersistentGraphPages;
        _maintenanceEvents = new GridEventInfo[_maintenanceBudget.MaxConsumedEnvelopes];
        _blockedScopes = new NavigationGridChangeScope[settings.OperationLimits.MaxMaps];
        _resnapshotScopes = new NavigationGridChangeScope[settings.OperationLimits.MaxMaps];
        _deferredScopes = new NavigationGridChangeScope[settings.OperationLimits.MaxMaps];
        _priorBlockedScopes = new NavigationGridChangeScope[settings.OperationLimits.MaxMaps];
        _baselineCaptures = new NavigationGridBaselineCapture[settings.OperationLimits.MaxMaps];
        _baselineAddressScratch = new VoxelIndex[_maintenanceBudget.MaxBaselineAddresses];
        _affectedMapOrdinals = new int[settings.OperationLimits.MaxMaps];
        _affectedMapStamps = new int[settings.OperationLimits.MaxMaps];
        _operationStructuralMapIds = new string[checked(
            settings.OperationLimits.MaxBatchItems * settings.OperationLimits.MaxMaps)];
        _publishCandidate = PublishPendingCandidate;
        _retainedWorkGuard = IsOperationWithinRetainedWorkCapacity;
        _maintainSnapshot = MaintainSnapshot;
    }

    internal NavigationWorldGraph Current => _store.Current;

    internal NavigationWorldGraphStore Store => _store;

    internal int RetainedBaselineCaptureCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _baselineCaptures.Length; i++)
            {
                if (_baselineCaptures[i].IsRequested)
                    count++;
            }
            return count;
        }
    }

    internal int RetainedCompositionWorkCount => _compositionWork == null ? 0 : 1;

    internal long RetainedCompositionWorkBytes => _compositionWork?.RetainedBytes ?? 0;

    internal long RetainedOperationWorkBytes => _operations.RetainedOperationWorkBytes;

    internal int RetainedOperationWorkCount => _operations.RetainedOperationWorkCount;

    internal int RetainedCompositionWorkPageCount =>
        _compositionWork?.PersistentPageCount ?? 0;

    internal int RetainedOperationWorkPageCount => _operations.RetainedOperationWorkPageCount;

    internal int CompositionCopiedNodeRecords =>
        _compositionWork?.CopiedNodeRecords ?? _lastCompletedCopiedNodes;

    internal int CompositionCopiedReverseRecords =>
        _compositionWork?.CopiedReverseRecords ?? _lastCompletedCopiedReverse;

    internal int CompositionCopiedComponentRecords =>
        _compositionWork?.CopiedComponentRecords ?? _lastCompletedCopiedComponents;

    internal int CompositionCopiedMembershipRecords =>
        _compositionWork?.CopiedMembershipRecords ?? _lastCompletedCopiedMemberships;

    internal MaintenanceWorkMeter MaintenanceMeter => _maintenanceMeter;

    internal int LastAffectedMapCollectionCount => _lastAffectedMapCollectionCount;

    internal bool Admit(NavigationMapCommitOperation operation)
    {
        EnsureUsable();
        return _operations.Admit(operation);
    }

    internal bool Admit(NavigationMapRemoveOperation operation)
    {
        EnsureUsable();
        return _operations.Admit(operation);
    }

    internal bool Admit(NavigationOverlayCommitOperation operation)
    {
        EnsureUsable();
        return _operations.Admit(operation);
    }

    internal bool Admit(NavigationAreaPolicyCommitOperation operation)
    {
        EnsureUsable();
        return _areaPolicies.Admit(operation);
    }

    internal NavigationWorldGraphLease? TryAcquire()
    {
        EnsureUsable();
        return _store.TryAcquire();
    }

    internal bool TryAdmitQuery(
        in NavigationQueryAdmissionRequest request,
        out NavigationQueryAdmissionLease? lease)
    {
        EnsureUsable();
        return _queryAdmission.TryAdmit(request, out lease);
    }

    internal int AdmitQueryBatch(
        ReadOnlySpan<NavigationQueryAdmissionRequest> requests,
        Span<NavigationQueryAdmissionLease?> leases)
    {
        EnsureUsable();
        return _queryAdmission.AdmitBatch(requests, leases);
    }

    internal void Maintain(int frame)
    {
        EnsureUsable();
        _maintenanceMeter.Reset();
        _publishedThisMaintenance = false;
        if (_resetPending)
        {
            NavigationCandidatePublication resetPublication = _store.TryPublish(
                NavigationWorldGraph.CreateEmpty(_store.Current.GraphVersion + 1));
            if (resetPublication != NavigationCandidatePublication.Published)
                return;
            _resetPending = false;
            _store.CacheGate.ClearSafetyPending();
        }
        if (!_store.CanPublish)
        {
            DrainSafetyPrefixUnderPressure();
            return;
        }

        NavigationWorldGraph before = _store.Current;
        bool graphWorkWasPending = _operations.RetainedOperationWorkCount != 0
            || _compositionWork != null;
        NavigationAreaCatalogProcessor.PreparedFrame policyFrame = default;
        bool policyPrepared = !graphWorkWasPending;
        _publicationAreaCatalog = before.AreaCatalog;
        if (policyPrepared)
        {
            policyFrame = _areaPolicies.Prepare(
                frame,
                before.AreaCatalog,
                _maintenanceMeter,
                checked(_maxActiveSnapshotBytes - before.RetainedBytes + before.AreaCatalog.RetainedBytes),
                checked(_maxPersistentGraphPages - before.PersistentPageCount + before.AreaCatalog.PersistentPageCount));
            _publicationAreaCatalog = policyFrame.Candidate;
        }
        NavigationOperationFrameResult operationResult = _operations.ProcessFrame(
            frame,
            _publishCandidate,
            _maintenanceMeter,
            _retainedWorkGuard);
        if (operationResult == NavigationOperationFrameResult.Published)
        {
            if (policyPrepared)
                policyFrame.Complete(frame);
            return;
        }
        if (operationResult == NavigationOperationFrameResult.Deferred)
        {
            _publicationAreaCatalog = before.AreaCatalog;
            if (!IsWithinRetainedWorkCapacity(
                    _operations.RetainedOperationWorkBytes,
                    _operations.RetainedOperationWorkPageCount))
            {
                _operations.RejectDeferredCapacity();
            }
            else if (!_publishedThisMaintenance)
            {
                int structuralCount = _operations.CopyDeferredStructuralMapIds(
                    _operationStructuralMapIds);
                NavigationWorldGraph publishedGraph = _store.Current;
                long safetyVersion = publishedGraph.GraphVersion + 1;
                NavigationWorldGraph closed = structuralCount > 0
                    ? publishedGraph.FailClosedStructuralScope(
                        _operationStructuralMapIds.AsSpan(0, structuralCount),
                        safetyVersion)
                    : publishedGraph;
                ReconcileAndPublish(
                    closed,
                    Array.Empty<NavigationOperationFrameChange>(),
                    0,
                    safetyVersion);
            }
            if (_compositionWork != null)
                RequeuePublicationEvents();
            return;
        }

        NavigationWorldGraph current = _store.Current;
        long nextVersion = current.GraphVersion + 1;
        NavigationAreaCatalog fallbackCatalog = policyPrepared
            ? policyFrame.Candidate
            : current.AreaCatalog;
        NavigationCandidatePublication fallbackPublication = ReconcileAndPublish(
            current.WithAreaCatalog(fallbackCatalog, nextVersion),
            Array.Empty<NavigationOperationFrameChange>(),
            0,
            nextVersion);
        if (policyPrepared && fallbackPublication == NavigationCandidatePublication.Published)
            policyFrame.Complete(frame);
        else if (policyPrepared
            && fallbackPublication == NavigationCandidatePublication.PermanentCapacity)
            policyFrame.CompleteCapacityRejected();
        _publicationAreaCatalog = _store.Current.AreaCatalog;
    }

    private void MaintainSnapshot()
    {
        _publicationEventCount = _ingress.DetachInto(
            _maintenanceEvents.AsSpan(0, _maintenanceMeter.RemainingEnvelopes),
            _blockedScopes,
            out _publicationBlockedScopeCount,
            out _publicationBlockAll);
        _maintenanceMeter.TryConsumeEnvelopes(_publicationEventCount);
        _publicationResnapshotScopeCount = PrepareResnapshotScopes(
            _publicationBlockedScopeCount,
            _publicationBlockAll);
        _publicationResnapshotAll = _priorBlockAll && !_publicationBlockAll;
        _snapshotWorldSpawnToken = _world.SpawnToken;
        _snapshotAffectedCount = _snapshotGraph.CaptureMaintenanceSnapshot(
            _world,
            _snapshotPreviousGraph,
            _maintenanceEvents.AsSpan(0, _publicationEventCount),
            _resnapshotScopes.AsSpan(0, _publicationResnapshotScopeCount),
            _publicationResnapshotAll,
            _blockedScopes.AsSpan(0, _publicationBlockedScopeCount),
            _publicationBlockAll,
            _snapshotChanges,
            _snapshotChangeCount,
            _maintenanceMeter,
            _maxActiveSnapshotBytes,
            _maxPersistentGraphPages,
            _baselineCaptures,
            ref _baselineRebuilds,
            _baselineAddressScratch,
            _deferredScopes,
            out _snapshotDeferredScopeCount,
            out _snapshotDeferAll,
            _affectedMapOrdinals,
            _affectedMapStamps,
            ref _affectedMapStamp,
            out _lastAffectedMapCollectionCount);
    }

    private NavigationCandidatePublication ReconcileAndPublish(
        NavigationWorldGraph graph,
        NavigationOperationFrameChange[] changes,
        int changeCount,
        long graphVersion)
    {
        _snapshotGraph = graph;
        _snapshotPreviousGraph = _store.Current;
        _snapshotChanges = changes;
        _snapshotChangeCount = changeCount;
        _world.ExecuteNavigationMaintenanceSnapshot(_maintainSnapshot);

        NavigationCandidatePublication publication;
        try
        {
            NavigationWorldGraph next = graph.ApplyMaintenanceSnapshot(
                _snapshotWorldSpawnToken,
                _snapshotPreviousGraph,
                _maintenanceEvents.AsSpan(0, _publicationEventCount),
                _resnapshotScopes.AsSpan(0, _publicationResnapshotScopeCount),
                _publicationResnapshotAll,
                _blockedScopes.AsSpan(0, _publicationBlockedScopeCount),
                _publicationBlockAll,
                changes,
                changeCount,
                _baselineCaptures,
                _affectedMapOrdinals,
                _snapshotAffectedCount,
                graphVersion);
            NavigationWorldGraph current = _store.Current;
            publication = ReferenceEquals(current, next)
                ? NavigationCandidatePublication.Published
                : _store.TryPublish(next);
            if (!ReferenceEquals(current, next)
                && publication == NavigationCandidatePublication.Published)
            {
                _publishedThisMaintenance = true;
            }
        }
        finally
        {
            ClearBaselineCaptures();
        }
        if (publication == NavigationCandidatePublication.Published)
        {
            RemoveCompletedBaselineRebuilds();
            CommitBlockedScopes(
                _publicationBlockedScopeCount,
                _publicationBlockAll,
                _snapshotDeferredScopeCount,
                _snapshotDeferAll);
            if (_store.CacheGate.IsSafetyPending
                && _publicationResnapshotAll
                && !_publicationBlockAll
                && _publicationBlockedScopeCount == 0
                && !_snapshotDeferAll
                && _snapshotDeferredScopeCount == 0)
            {
                _store.CacheGate.ClearSafetyPending();
            }
        }
        else
            MarkResnapshotRequired();
        return publication;
    }

    internal bool TryGetCellState(
        string mapId,
        VoxelIndex index,
        out NavigationGraphCellState state)
    {
        EnsureUsable();
        NavigationWorldGraph graph = _store.Current;
        if (!graph.TryGetMap(mapId, out NavigationMapInstance? instance)
            || instance == null
            || !instance.TryGetSlot(index, out int slot))
        {
            state = default;
            return false;
        }

        bool hasCell = instance.TryGetEffectiveCell(slot, out NavigationCell cell);
        instance.TryGetPhysicalState(slot, out bool isPresent, out byte obstacleCount);
        state = new NavigationGraphCellState(
            mapId,
            index,
            slot,
            slot >= instance.BakedSlotCount,
            hasCell,
            cell,
            instance.IsMaterialized && !graph.IsStructuralScopeClosed(mapId),
            isPresent,
            obstacleCount,
            instance.GridIdentity.GridSpawnToken);
        return true;
    }

    internal bool TryResolveAreaPolicy(
        NavigationAreaPolicyKey key,
        out NavigationAreaPolicy? policy)
    {
        EnsureUsable();
        return _store.Current.AreaCatalog.TryGet(key, out policy);
    }

    internal NavigationGraphDiagnosticsSnapshot GetDiagnostics(int maximumCells)
    {
        EnsureUsable();
        NavigationWorldGraph graph = _store.Current;
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long baselineRebuildBytes,
            out int baselineRebuildPages);
        int baselineCapacityBlockedCount = 0;
        for (int i = 0; i < _baselineRebuilds.Count; i++)
        {
            if (_baselineRebuilds.GetValueAt(i).IsCapacityBlocked)
                baselineCapacityBlockedCount++;
        }
        long compositionWorkBytes = _compositionWork?.RetainedBytes ?? 0;
        int compositionWorkPages = _compositionWork?.PersistentPageCount ?? 0;
        long operationWorkBytes = _operations.RetainedOperationWorkBytes;
        int operationWorkPages = _operations.RetainedOperationWorkPageCount;
        var maps = new NavigationGraphMapDiagnostic[graph.MapCount];
        int remaining = maximumCells;
        bool truncated = false;
        for (int i = 0; i < graph.MapCount; i++)
        {
            maps[i] = graph.GetInstance(i).CreateDiagnostic(
                remaining,
                graph.Composition.GetComponentId(i),
                graph.Composition.GetComponentVersion(i),
                graph.Composition.GetIncidentEdgeCount(i),
                out bool mapTruncated);
            remaining -= maps[i].Cells.Count;
            truncated |= mapTruncated;
        }
        return new NavigationGraphDiagnosticsSnapshot(
            graph.GraphVersion,
            graph.AreaCatalog.Version,
            checked(
                graph.RetainedBytes
                + baselineRebuildBytes
                + compositionWorkBytes
                + operationWorkBytes
                + _areaPolicies.PendingRetainedBytes),
            _store.ActiveGenerationCount,
            _store.ActiveLeaseCount,
            checked(
                graph.PersistentPageCount
                + baselineRebuildPages
                + compositionWorkPages
                + operationWorkPages
                + _areaPolicies.PendingCount),
            _store.RetiredGenerationCount,
            _store.RetiredBytes,
            _queryAdmission.ActiveCount,
            _workspaces.ActiveBytes,
            _workspaces.RetainedBytes,
            _store.CacheGate.TotalResultBytes,
            _areaPolicies.PendingCount,
            _areaPolicies.PendingRuleCount,
            _areaPolicies.PendingRetainedBytes,
            _baselineRebuilds.Count,
            baselineCapacityBlockedCount,
            baselineRebuildBytes,
            baselineRebuildPages,
            truncated,
            maps);
    }

    internal void Reset()
    {
        EnsureUsable();
        _operations.Reset();
        _areaPolicies.Reset();
        _ingress.Reset();
        _priorBlockedScopeCount = 0;
        _priorBlockAll = false;
        _baselineRebuilds = PersistentStringMap<NavigationBaselineRebuild>.Empty;
        _compositionWork = null;
        Array.Clear(_baselineCaptures, 0, _baselineCaptures.Length);
        _workspaces.ClearRetained();
        _resetPending = _store.TryPublish(
            NavigationWorldGraph.CreateEmpty(_store.Current.GraphVersion + 1))
            != NavigationCandidatePublication.Published;
        if (_resetPending)
            _store.CacheGate.MarkSafetyPending();
        else
            _store.CacheGate.ClearSafetyPending();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _baselineRebuilds = PersistentStringMap<NavigationBaselineRebuild>.Empty;
        _compositionWork = null;
        Array.Clear(_baselineCaptures, 0, _baselineCaptures.Length);
        _ingress.Dispose();
        _queryAdmission.Dispose();
        _workspaces.Dispose();
        _store.Dispose();
    }

    private NavigationCandidatePublication PublishCandidate(
        NavigationOperationCandidate candidate,
        NavigationAreaCatalog areaCatalog,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        if (candidate.GetTotalDynamicCellCandidateCount() > _maxDynamicSlots)
        {
            if (_compositionWork != null && !TryRollbackCompositionClosure())
                return NavigationCandidatePublication.Deferred;
            _compositionWork = null;
            return NavigationCandidatePublication.PermanentCapacity;
        }

        if (_compositionWork != null)
        {
            if (!_compositionWork.Matches(changes, changeCount))
                return NavigationCandidatePublication.Deferred;
            if (!_compositionWork.Advance(_maintenanceMeter))
            {
                if (!IsWithinRetainedWorkCapacity(
                        GetCombinedCompositionWorkBytes(_compositionWork),
                        GetCombinedCompositionWorkPages(_compositionWork)))
                {
                    if (!TryRollbackCompositionClosure())
                        return NavigationCandidatePublication.Deferred;
                    _compositionWork = null;
                    return NavigationCandidatePublication.PermanentCapacity;
                }
                return NavigationCandidatePublication.Deferred;
            }
            if (!IsWithinRetainedWorkCapacity(
                    GetCombinedCompositionWorkBytes(_compositionWork),
                    GetCombinedCompositionWorkPages(_compositionWork)))
            {
                if (!TryRollbackCompositionClosure())
                    return NavigationCandidatePublication.Deferred;
                _compositionWork = null;
                return NavigationCandidatePublication.PermanentCapacity;
            }
            NavigationWorldGraph completed = _compositionWork.Result.WithAreaCatalog(
                areaCatalog,
                _compositionWork.Result.GraphVersion);
            if (!completed.IsWithinDynamicSlotCapacity(_maxDynamicSlotsPerMap, _maxDynamicSlots))
            {
                if (!TryRollbackCompositionClosure())
                    return NavigationCandidatePublication.Deferred;
                _compositionWork = null;
                return NavigationCandidatePublication.PermanentCapacity;
            }
            NavigationCandidatePublication completedPublication = ReconcileAndPublish(
                completed,
                changes,
                changeCount,
                completed.GraphVersion);
            if (completedPublication == NavigationCandidatePublication.PermanentCapacity
                && !TryRollbackCompositionClosure())
            {
                return NavigationCandidatePublication.Deferred;
            }
            if (completedPublication != NavigationCandidatePublication.Deferred)
            {
                CaptureCompletedCompositionCounters();
                _compositionWork = null;
            }
            return completedPublication;
        }

        NavigationWorldGraph current = _store.Current;
        bool hasStructuralChanges = NavigationWorldGraph.HasStructuralChanges(changes, changeCount);
        long nextVersion = current.GraphVersion + 1;
        NavigationWorldGraph? completedStructural = null;
        if (changeCount > 0)
        {
            long minimumScratchBytes = NavigationStructuralCompositionWork.GetMinimumScratchBytes(
                current.MapCount,
                candidate.MapCount,
                changeCount,
                candidate.OverlayCellCount);
            int minimumScratchPages = NavigationStructuralCompositionWork.GetMinimumScratchPages(
                candidate.MapCount,
                candidate.OverlayCellCount);
            long candidateGrowthBytes = Math.Max(
                0L,
                candidate.RetainedBytes - current.RetainedBytes);
            int candidateGrowthPages = Math.Max(
                0,
                candidate.PersistentPageCount - current.PersistentPageCount);
            if (!IsWithinRetainedWorkCapacity(
                    checked(candidateGrowthBytes + minimumScratchBytes),
                    checked(candidateGrowthPages + minimumScratchPages)))
            {
                return NavigationCandidatePublication.PermanentCapacity;
            }
            var work = new NavigationStructuralCompositionWork(
                current,
                candidate,
                changes,
                changeCount,
                hasStructuralChanges);
            ResetCompletedCompositionCounters();
            if (!work.Advance(_maintenanceMeter))
            {
                NavigationWorldGraph closed = hasStructuralChanges
                    ? current.FailClosedStructuralScope(
                        work.ChangedMapIds,
                        current.GraphVersion + 1)
                    : current;
                if (!IsWithinRetainedWorkCapacity(
                        GetCombinedCompositionWorkBytes(work),
                        GetCombinedCompositionWorkPages(work),
                        closed))
                {
                    return NavigationCandidatePublication.PermanentCapacity;
                }
                if (!ReferenceEquals(closed, current))
                {
                    NavigationCandidatePublication closedPublication = ReconcileAndPublish(
                        closed,
                        Array.Empty<NavigationOperationFrameChange>(),
                        0,
                        closed.GraphVersion);
                    if (closedPublication != NavigationCandidatePublication.Published)
                        return closedPublication;
                }
                _compositionWork = work;
                return NavigationCandidatePublication.Deferred;
            }
            completedStructural = work.Result;
            _lastCompletedCopiedNodes = work.CopiedNodeRecords;
            _lastCompletedCopiedReverse = work.CopiedReverseRecords;
            _lastCompletedCopiedComponents = work.CopiedComponentRecords;
            _lastCompletedCopiedMemberships = work.CopiedMembershipRecords;
        }
        NavigationWorldGraph next = completedStructural ?? NavigationWorldGraph.Compose(
                candidate,
                current,
                nextVersion,
                changes,
                changeCount);
        if (!next.IsWithinDynamicSlotCapacity(_maxDynamicSlotsPerMap, _maxDynamicSlots))
            return NavigationCandidatePublication.PermanentCapacity;
        return ReconcileAndPublish(
            next.WithAreaCatalog(areaCatalog, nextVersion),
            changes,
            changeCount,
            nextVersion);
    }

    private bool IsWithinRetainedWorkCapacity(
        long workBytes,
        int workPages,
        NavigationWorldGraph? graph = null)
    {
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long rebuildBytes,
            out int rebuildPages);
        NavigationWorldGraph root = graph ?? _store.Current;
        // Conservative sum: persistent roots share pages, but double-counting is safer than
        // allowing aggregate active state to escape its configured ceiling.
        return root.RetainedBytes <= _maxActiveSnapshotBytes
            && workBytes <= _maxActiveSnapshotBytes - root.RetainedBytes
            && rebuildBytes <= _maxActiveSnapshotBytes - root.RetainedBytes - workBytes
            && root.PersistentPageCount <= _maxPersistentGraphPages
            && workPages <= _maxPersistentGraphPages - root.PersistentPageCount
            && rebuildPages <= _maxPersistentGraphPages - root.PersistentPageCount - workPages;
    }

    private bool TryRollbackCompositionClosure()
    {
        NavigationWorldGraph current = _store.Current;
        NavigationWorldGraph reopened = current.ReopenStructuralScopes(current.GraphVersion + 1);
        return ReferenceEquals(current, reopened)
            || _store.TryPublish(reopened) == NavigationCandidatePublication.Published;
    }

    private void CaptureCompletedCompositionCounters()
    {
        _lastCompletedCopiedNodes = _compositionWork!.CopiedNodeRecords;
        _lastCompletedCopiedReverse = _compositionWork.CopiedReverseRecords;
        _lastCompletedCopiedComponents = _compositionWork.CopiedComponentRecords;
        _lastCompletedCopiedMemberships = _compositionWork.CopiedMembershipRecords;
    }

    private void ResetCompletedCompositionCounters()
    {
        _lastCompletedCopiedNodes = 0;
        _lastCompletedCopiedReverse = 0;
        _lastCompletedCopiedComponents = 0;
        _lastCompletedCopiedMemberships = 0;
    }

    private bool IsOperationWithinRetainedWorkCapacity(long workBytes, int workPages) =>
        IsWithinRetainedWorkCapacity(workBytes, workPages);

    private long GetCombinedCompositionWorkBytes(NavigationStructuralCompositionWork work) =>
        checked(work.RetainedBytes + _operations.RetainedOperationWorkBytes);

    private int GetCombinedCompositionWorkPages(NavigationStructuralCompositionWork work) =>
        checked(work.PersistentPageCount + _operations.RetainedOperationWorkPageCount);

    private NavigationCandidatePublication PublishPendingCandidate(
        NavigationOperationCandidate candidate,
        int frame,
        NavigationOperationFrameChange[] changes,
        int changeCount) =>
        PublishCandidate(
            candidate,
            _publicationAreaCatalog,
            changes,
            changeCount);

    private int PrepareResnapshotScopes(int blockedScopeCount, bool blockAll)
    {
        if (_priorBlockAll || blockAll)
            return 0;

        int count = 0;
        for (int i = 0; i < _priorBlockedScopeCount; i++)
        {
            NavigationGridChangeScope prior = _priorBlockedScopes[i];
            if (!ContainsScope(_blockedScopes, blockedScopeCount, prior.ConfigurationKey))
                _resnapshotScopes[count++] = prior;
        }
        return count;
    }

    private void CommitBlockedScopes(
        int blockedScopeCount,
        bool blockAll,
        int deferredScopeCount,
        bool deferAll)
    {
        if (blockAll || deferAll)
        {
            _priorBlockedScopeCount = 0;
            _priorBlockAll = true;
            return;
        }

        int count = 0;
        for (int i = 0; i < blockedScopeCount; i++)
            _priorBlockedScopes[count++] = _blockedScopes[i];
        for (int i = 0; i < deferredScopeCount; i++)
        {
            NavigationGridChangeScope deferred = _deferredScopes[i];
            if (ContainsScope(_priorBlockedScopes, count, deferred.ConfigurationKey))
                continue;
            if (count == _priorBlockedScopes.Length)
            {
                _priorBlockedScopeCount = 0;
                _priorBlockAll = true;
                return;
            }
            _priorBlockedScopes[count++] = deferred;
        }
        _priorBlockedScopeCount = count;
        _priorBlockAll = false;
    }

    private static bool ContainsScope(
        NavigationGridChangeScope[] scopes,
        int count,
        GridForge.Configuration.GridConfigurationKey key)
    {
        for (int i = 0; i < count; i++)
        {
            if (scopes[i].ConfigurationKey.Equals(key))
                return true;
        }
        return false;
    }

    internal void EnqueueCommittedChange(GridEventInfo eventInfo)
    {
        if (_disposed || eventInfo.WorldSpawnToken != _world.SpawnToken)
            return;
        _ingress.Enqueue(eventInfo);
    }

    private void MarkResnapshotRequired()
    {
        _ingress.MarkResnapshotRequired();
    }

    private void RequeuePublicationEvents()
    {
        _ingress.RequeuePrefix(_maintenanceEvents.AsSpan(0, _publicationEventCount));
    }

    private void DrainSafetyPrefixUnderPressure()
    {
        int count = _ingress.DetachInto(
            _maintenanceEvents,
            _blockedScopes,
            out int blockedScopeCount,
            out bool blockAll);
        _maintenanceMeter.TryConsumeEnvelopes(count);
        if (count != 0 || blockedScopeCount != 0 || blockAll)
        {
            // Query admission is already closed by the store while the bounded leased generations
            // prevent publication. Drain a bounded final-state prefix now, then require an exact
            // baseline before any affected scope can reopen once publication pressure clears.
            _store.CacheGate.MarkSafetyPending();
            _ingress.MarkResnapshotRequired();
        }
    }

    private void ClearBaselineCaptures()
    {
        for (int i = 0; i < _snapshotAffectedCount; i++)
            _baselineCaptures[_affectedMapOrdinals[i]] = default;
    }

    private void RemoveCompletedBaselineRebuilds()
    {
        for (int i = _baselineRebuilds.Count - 1; i >= 0; i--)
        {
            NavigationBaselineRebuild rebuild = _baselineRebuilds.GetValueAt(i);
            if (rebuild.IsComplete)
                _baselineRebuilds = _baselineRebuilds.Remove(rebuild.MapId, out _);
        }
    }

    private void EnsureUsable()
    {
        SwiftThrowHelper.ThrowIfDisposed(_disposed, nameof(NavigationGraphRuntime));
    }
}
