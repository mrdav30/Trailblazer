//=======================================================================
// NavigationGraphRuntime.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Grids.Topology;
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
    private NavigationMaterializedComponentWork? _materializedComponentWork;
    private bool _materializedComponentWorkCompletesOperation;
    private bool _materializedStartsAutomaticSeamLifecycle;
    private NavigationAutomaticSeamLifecycleWork? _lifecycleWork;
    private readonly VoxelIndex[] _baselineAddressScratch;
    private readonly GridCoveredAddress[] _baselineCoveredAddressScratch;
    private int _publicationEventCount;
    private int _lifecycleEventCount;
    private int _publicationBlockedScopeCount;
    private int _publicationResnapshotScopeCount;
    private NavigationAreaCatalog _publicationAreaCatalog = NavigationAreaCatalog.Empty;
    private bool _publicationResnapshotAll;
    private bool _publicationBlockAll;
    private NavigationWorldGraph _snapshotGraph = NavigationWorldGraph.Empty;
    private NavigationWorldGraph _snapshotPreviousGraph = NavigationWorldGraph.Empty;
    private NavigationOperationFrameChange[] _snapshotChanges = Array.Empty<NavigationOperationFrameChange>();
    private int _snapshotChangeCount;
    private bool _snapshotStartsAutomaticSeamLifecycle;
    private int _snapshotAffectedCount;
    private int _snapshotDeferredScopeCount;
    private long _snapshotWorldSpawnToken;
    private bool _snapshotDeferAll;
    private int _priorBlockedScopeCount;
    private bool _priorBlockAll;
    private readonly int[] _affectedMapOrdinals;
    private readonly int[] _affectedMapStamps;
    private int _affectedMapStamp;
    private int _lastAffectedMapCollectionCount;
    private bool _publishedThisMaintenance;
    private bool _automaticSeamFullRebuildPending;
    private bool _operationClosureRollbackPending;
    private NavigationSurfaceComponentKeySet? _ownedStructuralClosureBaseline;
    private bool _ownedStructuralClosureBaselineAll;
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
            settings.MaxConcurrentSnapshotLeases);
        _areaPolicies = new NavigationAreaCatalogProcessor(settings);
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
        _baselineCoveredAddressScratch =
            new GridCoveredAddress[_maintenanceBudget.MaxBaselineAddresses];
        _affectedMapOrdinals = new int[settings.OperationLimits.MaxMaps];
        _affectedMapStamps = new int[settings.OperationLimits.MaxMaps];
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

    internal int RetainedCompositionWorkCount => (_compositionWork == null ? 0 : 1)
        + (_materializedComponentWork == null ? 0 : 1)
        + (_lifecycleWork == null ? 0 : 1);

    internal long RetainedCompositionWorkBytes => checked(
        (_compositionWork?.RetainedBytes ?? 0L)
        + (_materializedComponentWork?.RetainedBytes ?? 0L)
        + (_lifecycleWork?.RetainedBytes ?? 0L)
        + GetOwnedClosureBaselineAdditionalRetainedBytes());

    internal long RetainedOperationWorkBytes => _operations.RetainedOperationWorkBytes;

    internal int RetainedOperationWorkCount => _operations.RetainedOperationWorkCount;

    internal int RetainedCompositionWorkPageCount =>
        checked(
            (_compositionWork?.PersistentPageCount ?? 0)
            + (_materializedComponentWork?.PersistentPageCount ?? 0)
            + (_lifecycleWork?.PersistentPageCount ?? 0)
            + GetOwnedClosureBaselineAdditionalPersistentPages());

    internal int RetainedOperationWorkPageCount => _operations.RetainedOperationWorkPageCount;

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

    internal void Maintain(int frame)
    {
        EnsureUsable();
        _maintenanceMeter.Reset();
        _publicationEventCount = 0;
        _publishedThisMaintenance = false;
        if (_resetPending)
        {
            NavigationCandidatePublication resetPublication = _store.TryPublish(
                NavigationWorldGraph.CreateEmpty(_store.Current.GraphVersion + 1));
            if (resetPublication != NavigationCandidatePublication.Published)
                return;
            _resetPending = false;
            _store.ClearSafetyPending();
        }
        if (_operationClosureRollbackPending && !_automaticSeamFullRebuildPending)
        {
            if (!TryPublishReopenedStructuralScopes())
                return;
            _operationClosureRollbackPending = false;
            if (_publishedThisMaintenance)
                return;
        }
        if (_lifecycleWork != null)
        {
            // Lifecycle work owns the detached prefix in _maintenanceEvents. Its published
            // source is already all-closed, so leave later ingress queued while leases block
            // publication rather than overwriting the retained prefix with a pressure drain.
            if (!_store.CanPublish)
                return;
            MaintainAutomaticSeamLifecycle();
            return;
        }

        if (!_store.CanPublish)
        {
            DrainSafetyPrefixUnderPressure();
            return;
        }

        if (_materializedComponentWork != null)
        {
            if (!_materializedComponentWorkCompletesOperation
                || _operations.RetainedOperationWorkCount == 0)
            {
                MaintainMaterializedComponentWork();
                return;
            }

            if (MaintainMaterializedComponentWork(publish: false)
                == NavigationCandidatePublication.Deferred)
            {
                return;
            }
        }

        if (_operations.RetainedOperationWorkCount == 0
            && _compositionWork == null
            && _materializedComponentWork == null)
        {
            NavigationWorldGraph ingressCurrent = _store.Current;
            NavigationCandidatePublication ingressPublication = ReconcileAndPublish(
                ingressCurrent,
                Array.Empty<NavigationOperationFrameChange>(),
                0,
                ingressCurrent.GraphVersion + 1,
                startAutomaticSeamLifecycle: true);
            if (_lifecycleWork != null
                || _publishedThisMaintenance
                || ingressPublication != NavigationCandidatePublication.Published)
            {
                return;
            }
        }

        NavigationWorldGraph before = _store.Current;
        bool graphWorkWasPending = _operations.RetainedOperationWorkCount != 0
            || _compositionWork != null
            || _materializedComponentWork != null;
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
        if (operationResult == NavigationOperationFrameResult.Published
            && _operations.RetainedOperationWorkCount == 0
            && (_compositionWork != null || _materializedComponentWorkCompletesOperation))
        {
            _compositionWork = null;
            if (_materializedComponentWorkCompletesOperation)
                AbandonMaterializedSnapshot();
            if (_store.Current.HasClosedStructuralScope)
            {
                BeginOperationClosureRollback();
                return;
            }
        }
        if (operationResult == NavigationOperationFrameResult.Published)
        {
            if (policyPrepared)
                policyFrame.Complete(frame);
            return;
        }
        if (operationResult == NavigationOperationFrameResult.Deferred)
        {
            _publicationAreaCatalog = before.AreaCatalog;
            long retainedWorkBytes = checked(
                _operations.RetainedOperationWorkBytes
                + (_compositionWork?.RetainedBytes ?? 0L));
            int retainedWorkPages = checked(
                _operations.RetainedOperationWorkPageCount
                + (_compositionWork?.PersistentPageCount ?? 0));
            if (!IsWithinRetainedWorkCapacity(
                    retainedWorkBytes,
                    retainedWorkPages))
            {
                _operations.RejectDeferredCapacity();
                _compositionWork = null;
                BeginOperationClosureRollback();
            }
            else if (_compositionWork != null)
            {
                if (!_publishedThisMaintenance)
                {
                    NavigationWorldGraph compositionCurrent = _store.Current;
                    ReconcileAndPublish(
                        compositionCurrent,
                        Array.Empty<NavigationOperationFrameChange>(),
                        0,
                        compositionCurrent.GraphVersion + 1);
                }
                RequeuePublicationEvents();
                return;
            }
            else if (!_publishedThisMaintenance)
            {
                NavigationWorldGraph publishedGraph = _store.Current;
                if (!IsWithinRetainedWorkCapacity(
                        _operations.RetainedOperationWorkBytes,
                        _operations.RetainedOperationWorkPageCount))
                {
                    _operations.RejectDeferredCapacity();
                    BeginOperationClosureRollback();
                }
                else
                {
                    long safetyVersion = publishedGraph.GraphVersion + 1;
                    CaptureOwnedStructuralClosureBaseline(publishedGraph);
                    NavigationWorldGraph closed = publishedGraph.WithClosedStructuralComponents(
                        NavigationSurfaceComponentKeySet.Empty,
                        true,
                        safetyVersion);
                    NavigationCandidatePublication closurePublication = ReconcileAndPublish(
                        closed,
                        Array.Empty<NavigationOperationFrameChange>(),
                        0,
                        safetyVersion);
                    if (closurePublication == NavigationCandidatePublication.PermanentCapacity)
                    {
                        _operations.RejectDeferredCapacity();
                        BeginOperationClosureRollback();
                    }
                }
            }
            if (_compositionWork != null)
                RequeuePublicationEvents();
            return;
        }

        if (operationResult == NavigationOperationFrameResult.Rejected)
        {
            _compositionWork = null;
            if (_materializedComponentWorkCompletesOperation)
                AbandonMaterializedSnapshot();
            if (_store.Current.HasClosedStructuralScope)
            {
                if (!TryPublishReopenedStructuralScopes())
                    _operationClosureRollbackPending = true;
                return;
            }
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
            out _publicationBlockAll,
            out bool topologyLifecycleCoverageLost);
        if (topologyLifecycleCoverageLost
            || (!_snapshotStartsAutomaticSeamLifecycle
                && ContainsTopologyLifecycle(_maintenanceEvents, _publicationEventCount)))
        {
            _automaticSeamFullRebuildPending = true;
        }
        _maintenanceMeter.TryConsumeEnvelopes(_publicationEventCount);
        _publicationResnapshotScopeCount = PrepareResnapshotScopes(
            _publicationBlockedScopeCount,
            _publicationBlockAll);
        _publicationResnapshotAll = _priorBlockAll && !_publicationBlockAll;
        _snapshotWorldSpawnToken = _world.SpawnToken;
        long retainedWorkBytes = checked(
            _operations.RetainedOperationWorkBytes
            + (_compositionWork?.RetainedBytes ?? 0L));
        int retainedWorkPages = checked(
            _operations.RetainedOperationWorkPageCount
            + (_compositionWork?.PersistentPageCount ?? 0));
        long maximumGraphBytes = Math.Max(0L, _maxActiveSnapshotBytes - retainedWorkBytes);
        int maximumGraphPages = Math.Max(0, _maxPersistentGraphPages - retainedWorkPages);
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
            maximumGraphBytes,
            maximumGraphPages,
            _baselineCaptures,
            ref _baselineRebuilds,
            _baselineAddressScratch,
            _baselineCoveredAddressScratch,
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
        long graphVersion,
        bool startAutomaticSeamLifecycle = false)
    {
        if (_materializedComponentWork != null)
            return MaintainMaterializedComponentWork();
        AdvanceCoveredBaselineRebuilds(graph);
        _snapshotGraph = graph;
        _snapshotPreviousGraph = _store.Current;
        _snapshotChanges = changes;
        _snapshotChangeCount = changeCount;
        _snapshotStartsAutomaticSeamLifecycle = startAutomaticSeamLifecycle;
        _world.ExecuteNavigationMaintenanceSnapshot(_maintainSnapshot);

        NavigationCandidatePublication publication;
        bool lifecycleEventsRequeued = false;
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
            bool hasAutomaticSeamLifecycle = startAutomaticSeamLifecycle
                && HasRelevantAutomaticSeamLifecycleEvent(graph, next);
            NavigationCandidatePublication? componentHold =
                !next.IsWithinDynamicSlotCapacity(
                    _maxDynamicSlotsPerMap,
                    _maxDynamicSlots)
                    ? NavigationCandidatePublication.PermanentCapacity
                    : TryPrepareMaterializedComponents(
                        ref next,
                        hasAutomaticSeamLifecycle);
            if (_automaticSeamFullRebuildPending && !startAutomaticSeamLifecycle)
            {
                next = next.WithClosedStructuralComponents(
                    NavigationSurfaceComponentKeySet.Empty,
                    true,
                    graphVersion);
            }
            NavigationWorldGraph current = _store.Current;
            publication = componentHold.HasValue
                ? componentHold.Value
                : hasAutomaticSeamLifecycle
                ? StartAutomaticSeamLifecycle(
                    current,
                    next,
                    graphVersion,
                    out lifecycleEventsRequeued)
                : ReferenceEquals(current, next)
                    ? NavigationCandidatePublication.Published
                    : _store.TryPublish(next);
            if (!ReferenceEquals(current, next)
                && publication == NavigationCandidatePublication.Published)
            {
                _publishedThisMaintenance = true;
            }
            if (publication == NavigationCandidatePublication.Published
                && _automaticSeamFullRebuildPending
                && next.HasClosedStructuralScope)
            {
                _store.ClearSafetyPending();
            }
        }
        finally
        {
            if (_materializedComponentWork == null)
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
            if (_store.IsSafetyPending
                && _publicationResnapshotAll
                && !_publicationBlockAll
                && _publicationBlockedScopeCount == 0
                && !_snapshotDeferAll
                && _snapshotDeferredScopeCount == 0)
            {
                _store.ClearSafetyPending();
            }
        }
        else
        {
            if (_automaticSeamFullRebuildPending
                && !(_materializedComponentWork != null
                    && _store.Current.AreAllStructuralComponentsClosed))
                _store.MarkSafetyPending();
            if (!lifecycleEventsRequeued && _materializedComponentWork == null)
                MarkResnapshotRequired();
        }
        return publication;
    }

    private NavigationCandidatePublication? TryPrepareMaterializedComponents(
        ref NavigationWorldGraph candidate,
        bool startsAutomaticSeamLifecycle)
    {
        if (_snapshotAffectedCount == 0 && _publicationEventCount == 0)
            return null;
        var work = new NavigationMaterializedComponentWork(
            candidate,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationCellAddressSet.Empty,
            affectedMemberCount: 0,
            _world,
            _baselineCaptures,
            _affectedMapOrdinals,
            _snapshotAffectedCount,
            _maintenanceEvents,
            _publicationEventCount,
            _operations.Candidate);
        bool complete = work.Advance(_maintenanceMeter);
        if (!IsWithinRetainedWorkCapacity(
                GetCombinedMaterializedWorkBytes(work),
                GetCombinedMaterializedWorkPages(work)))
            return NavigationCandidatePublication.PermanentCapacity;
        if (complete && work.RevalidateForPublication())
        {
            candidate = work.Result;
            return null;
        }

        NavigationWorldGraph current = _store.Current;
        CaptureOwnedStructuralClosureBaseline(current);
        NavigationWorldGraph closed = CreateOwnedStructuralClosure(
            current,
            NavigationSurfaceComponentKeySet.Empty,
            closeAll: true,
            current.GraphVersion + 1);
        NavigationCandidatePublication publication = ReferenceEquals(current, closed)
            ? NavigationCandidatePublication.Published
            : _store.TryPublish(closed);
        if (publication != NavigationCandidatePublication.Published)
            return publication;
        if (!ReferenceEquals(current, closed))
            _publishedThisMaintenance = true;
        _materializedComponentWork = work;
        _materializedComponentWorkCompletesOperation =
            _operations.RetainedOperationWorkCount != 0;
        _materializedStartsAutomaticSeamLifecycle = startsAutomaticSeamLifecycle;
        return NavigationCandidatePublication.Deferred;
    }

    private NavigationCandidatePublication MaintainMaterializedComponentWork(
        bool publish = true)
    {
        NavigationMaterializedComponentWork work = _materializedComponentWork!;
        bool complete = work.Advance(_maintenanceMeter);
        if (work.RequiresSnapshotRestart)
            return RestartMaterializedSnapshot();
        bool revalidated = complete && work.RevalidateForPublication();
        if (work.RequiresSnapshotRestart)
            return RestartMaterializedSnapshot();
        if (!IsWithinRetainedWorkCapacity(
                GetCombinedMaterializedWorkBytes(work),
                GetCombinedMaterializedWorkPages(work)))
        {
            AbandonMaterializedSnapshot();
            return NavigationCandidatePublication.PermanentCapacity;
        }
        if (!complete)
            return NavigationCandidatePublication.Deferred;
        if (!revalidated)
            return NavigationCandidatePublication.Deferred;
        if (!publish)
            return NavigationCandidatePublication.Published;

        NavigationWorldGraph current = _store.Current;
        NavigationWorldGraph completed = work.Result.WithClosedStructuralComponents(
            _ownedStructuralClosureBaseline ?? NavigationSurfaceComponentKeySet.Empty,
            _ownedStructuralClosureBaselineAll,
            current.GraphVersion + 1);
        NavigationCandidatePublication publication;
        if (_materializedStartsAutomaticSeamLifecycle)
        {
            _publicationEventCount = work.RetainedEventCount;
            publication = StartAutomaticSeamLifecycle(
                current,
                completed,
                completed.GraphVersion,
                out _);
        }
        else
        {
            publication = _store.TryPublish(completed);
        }
        if (publication == NavigationCandidatePublication.Published)
        {
            _publishedThisMaintenance = true;
            _materializedComponentWork = null;
            _materializedComponentWorkCompletesOperation = false;
            _materializedStartsAutomaticSeamLifecycle = false;
            ClearBaselineCaptures();
            RemoveCompletedBaselineRebuilds();
            CommitBlockedScopes(
                _publicationBlockedScopeCount,
                _publicationBlockAll,
                _snapshotDeferredScopeCount,
                _snapshotDeferAll);
            if (_store.IsSafetyPending
                && _publicationResnapshotAll
                && !_publicationBlockAll
                && _publicationBlockedScopeCount == 0
                && !_snapshotDeferAll
                && _snapshotDeferredScopeCount == 0)
            {
                _store.ClearSafetyPending();
            }
            ClearOwnedStructuralClosureBaseline();
        }
        return publication;
    }

    private NavigationCandidatePublication RestartMaterializedSnapshot()
    {
        AbandonMaterializedSnapshot();
        return NavigationCandidatePublication.Deferred;
    }

    private void AbandonMaterializedSnapshot()
    {
        _publicationEventCount = _materializedComponentWork!.RetainedEventCount;
        _materializedComponentWork = null;
        _materializedComponentWorkCompletesOperation = false;
        _materializedStartsAutomaticSeamLifecycle = false;
        ClearBaselineCaptures();
        RemoveCompletedBaselineRebuilds();
        RequeuePublicationEvents();
        MarkResnapshotRequired();
    }

    private void AdvanceCoveredBaselineRebuilds(NavigationWorldGraph graph)
    {
        int remainingAddresses = _maintenanceMeter.RemainingBaselineAddresses;
        if (remainingAddresses == 0 || _baselineRebuilds.Count == 0)
            return;
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long totalBytes,
            out int totalPages);
        for (int i = 0; i < _baselineRebuilds.Count && remainingAddresses > 0; i++)
        {
            NavigationBaselineRebuild rebuild = _baselineRebuilds.GetValueAt(i);
            if (!rebuild.RequiresCoveredDiscovery
                || rebuild.IsComplete
                || rebuild.IsCapacityBlocked
                || !graph.TryGetMap(rebuild.MapId, out NavigationMapInstance? instance)
                || instance == null
                || !rebuild.Matches(instance))
            {
                continue;
            }
            long beforeBytes = rebuild.RetainedBytes;
            int beforePages = rebuild.PersistentPageCount;
            long maximumBytes = Math.Max(
                0L,
                _maxActiveSnapshotBytes - graph.RetainedBytes - (totalBytes - beforeBytes));
            int maximumPages = Math.Max(
                0,
                _maxPersistentGraphPages - graph.PersistentPageCount - (totalPages - beforePages));
            int consumed = rebuild.Advance(
                _world,
                instance,
                remainingAddresses,
                maximumBytes,
                maximumPages,
                _baselineAddressScratch,
                _baselineCoveredAddressScratch,
                out _,
                out _);
            totalBytes = checked(totalBytes - beforeBytes + rebuild.RetainedBytes);
            totalPages = checked(totalPages - beforePages + rebuild.PersistentPageCount);
            _maintenanceMeter.TryConsumeBaselineAddresses(consumed);
            remainingAddresses -= consumed;
        }
    }

    private bool HasRelevantAutomaticSeamLifecycleEvent(
        NavigationWorldGraph source,
        NavigationWorldGraph prepared)
    {
        if (_automaticSeamFullRebuildPending)
            return true;
        for (int i = 0; i < _publicationEventCount; i++)
        {
            GridEventInfo eventInfo = _maintenanceEvents[i];
            if (eventInfo.ChangeKind == GridEventKind.WorldReset)
            {
                if (source.MapCount != 0)
                    return true;
                continue;
            }
            GridForge.Configuration.GridConfigurationKey key =
                eventInfo.Configuration.ToGridKey();
            if (eventInfo.ChangeKind == GridEventKind.GridRemoved
                && source.TryGetMapId(key, out _))
            {
                return true;
            }
            if (eventInfo.ChangeKind == GridEventKind.GridAdded
                && prepared.TryGetMapId(key, out _))
            {
                return true;
            }
        }
        return false;
    }

    private NavigationCandidatePublication StartAutomaticSeamLifecycle(
        NavigationWorldGraph current,
        NavigationWorldGraph prepared,
        long graphVersion,
        out bool eventsRequeued)
    {
        eventsRequeued = false;
        NavigationWorldGraph closed = prepared.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty,
            true,
            graphVersion);
        long minimumBytes = checked(
            NavigationAutomaticSeamLifecycleWork.BaseRetainedBytes
            + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes);
        const int MinimumPages = 5;
        bool canRetainWork = IsWithinRetainedWorkCapacity(
            minimumBytes,
            MinimumPages,
            closed);
        NavigationCandidatePublication publication = ReferenceEquals(current, closed)
            ? NavigationCandidatePublication.Published
            : _store.TryPublish(closed);
        if (publication != NavigationCandidatePublication.Published)
        {
            RequeuePublicationEvents();
            eventsRequeued = true;
            _store.MarkSafetyPending();
            return publication;
        }
        if (!ReferenceEquals(current, closed))
            _publishedThisMaintenance = true;
        _store.ClearSafetyPending();
        if (!canRetainWork)
        {
            RequeuePublicationEvents();
            return NavigationCandidatePublication.Published;
        }

        _lifecycleEventCount = _publicationEventCount;
        _lifecycleWork = new NavigationAutomaticSeamLifecycleWork(
            _world,
            closed,
            _maintenanceEvents,
            _lifecycleEventCount,
            _automaticSeamFullRebuildPending);
        return NavigationCandidatePublication.Published;
    }

    private void MaintainAutomaticSeamLifecycle()
    {
        NavigationAutomaticSeamLifecycleWork work = _lifecycleWork!;
        GetLifecycleWorkAllowance(out long bytes, out int pages);
        while (true)
        {
            NavigationAutomaticSeamLifecycleWork.AdvanceStatus status = work.AdvanceOne(
                _maintenanceMeter,
                bytes,
                pages);
            switch (status)
            {
                case NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Progressed:
                    continue;
                case NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Blocked:
                    return;
                case NavigationAutomaticSeamLifecycleWork.AdvanceStatus.RestartRequired:
                case NavigationAutomaticSeamLifecycleWork.AdvanceStatus.CapacityExceeded:
                    RequeueAutomaticSeamLifecyclePrefix();
                    _lifecycleWork = null;
                    return;
                case NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Complete:
                    if (!work.RevalidateForPublication())
                    {
                        RequeueAutomaticSeamLifecyclePrefix();
                        _lifecycleWork = null;
                        return;
                    }
                    NavigationWorldGraph next = work.Result;
                    if (!IsWithinRetainedWorkCapacity(0, 0, next))
                    {
                        RequeueAutomaticSeamLifecyclePrefix();
                        _lifecycleWork = null;
                        return;
                    }
                    NavigationCandidatePublication publication = _store.TryPublish(next);
                    if (publication == NavigationCandidatePublication.Published)
                    {
                        _publishedThisMaintenance = true;
                        _lifecycleWork = null;
                        _lifecycleEventCount = 0;
                        _automaticSeamFullRebuildPending = false;
                        _store.ClearSafetyPending();
                    }
                    return;
            }
        }
    }

    private void GetLifecycleWorkAllowance(out long bytes, out int pages)
    {
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long rebuildBytes,
            out int rebuildPages);
        NavigationWorldGraph current = _store.Current;
        bytes = _maxActiveSnapshotBytes
            - current.RetainedBytes
            - rebuildBytes
            - _operations.RetainedOperationWorkBytes
            - (_compositionWork?.RetainedBytes ?? 0L);
        pages = _maxPersistentGraphPages
            - current.PersistentPageCount
            - rebuildPages
            - _operations.RetainedOperationWorkPageCount
            - (_compositionWork?.PersistentPageCount ?? 0);
    }

    private void RequeueAutomaticSeamLifecyclePrefix()
    {
        _ingress.RequeuePrefix(_maintenanceEvents.AsSpan(0, _lifecycleEventCount));
        _lifecycleEventCount = 0;
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
            instance.IsMaterialized
                && !graph.IsSurfaceAddressClosed(
                    new NavigationCellAddress(mapId, index),
                    TraversalMedium.Solid),
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
        long compositionWorkBytes = RetainedCompositionWorkBytes;
        int compositionWorkPages = RetainedCompositionWorkPageCount;
        long operationWorkBytes = _operations.RetainedOperationWorkBytes;
        int operationWorkPages = _operations.RetainedOperationWorkPageCount;
        var maps = new NavigationGraphMapDiagnostic[graph.MapCount];
        int remaining = maximumCells;
        bool truncated = false;
        for (int i = 0; i < graph.MapCount; i++)
        {
            NavigationMapInstance instance = graph.GetInstance(i);
            GetSurfaceComponentDiagnostic(
                graph,
                instance,
                out int componentId,
                out long componentVersion);
            maps[i] = instance.CreateDiagnostic(
                remaining,
                componentId,
                componentVersion,
                graph.ExplicitConnections.GetActiveIncidentEdgeCount(
                    instance.MapId),
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

    private static void GetSurfaceComponentDiagnostic(
        NavigationWorldGraph graph,
        NavigationMapInstance instance,
        out int componentId,
        out long componentVersion)
    {
        componentId = 0;
        componentVersion = 0;
        NavigationSurfaceComponentKey selected = default;
        bool found = false;
        int bakedCursor = 0;
        int dynamicCursor = 0;
        Span<GridForge.Spatial.VoxelIndex> address =
            stackalloc GridForge.Spatial.VoxelIndex[1];
        for (int ordinal = 0; ordinal < instance.AddressCount; ordinal++)
        {
            instance.CopyCanonicalAddressChunk(
                ref bakedCursor,
                ref dynamicCursor,
                address);
            var exact = new NavigationCellAddress(instance.MapId, address[0]);
            if (!graph.HasEffectiveCell(exact)
                || !graph.TryGetSurfaceComponent(
                    exact,
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponentKey key,
                    out long version))
            {
                continue;
            }
            if (!found)
            {
                selected = key;
                componentId = key.GetHashCode();
                componentVersion = version;
                found = true;
            }
            else if (key != selected)
            {
                componentId = -1;
                componentVersion = 0;
                return;
            }
        }
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
        _materializedComponentWork = null;
        _materializedComponentWorkCompletesOperation = false;
        _materializedStartsAutomaticSeamLifecycle = false;
        _lifecycleWork = null;
        _lifecycleEventCount = 0;
        _automaticSeamFullRebuildPending = false;
        _operationClosureRollbackPending = false;
        ClearOwnedStructuralClosureBaseline();
        Array.Clear(_baselineCaptures, 0, _baselineCaptures.Length);
        _resetPending = _store.TryPublish(
            NavigationWorldGraph.CreateEmpty(_store.Current.GraphVersion + 1))
            != NavigationCandidatePublication.Published;
        if (_resetPending)
            _store.MarkSafetyPending();
        else
            _store.ClearSafetyPending();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _baselineRebuilds = PersistentStringMap<NavigationBaselineRebuild>.Empty;
        _compositionWork = null;
        _materializedComponentWork = null;
        _materializedComponentWorkCompletesOperation = false;
        _materializedStartsAutomaticSeamLifecycle = false;
        _lifecycleWork = null;
        _lifecycleEventCount = 0;
        _automaticSeamFullRebuildPending = false;
        Array.Clear(_baselineCaptures, 0, _baselineCaptures.Length);
        _ingress.Dispose();
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
            if (_compositionWork.RequiresAllClosePublication)
            {
                NavigationCandidatePublication closeAllPublication =
                    PublishAllCompositionClosure(_compositionWork);
                if (closeAllPublication != NavigationCandidatePublication.Published)
                    return closeAllPublication;
                if (_publishedThisMaintenance)
                    return NavigationCandidatePublication.Deferred;
            }
            if (_compositionWork.RequiresAffectedClosurePublication)
            {
                if (!_compositionWork.RevalidateAutomaticSeamsForPublication())
                {
                    if (_compositionWork.RequiresAllClosePublication)
                        return PublishAllCompositionClosure(_compositionWork);
                    return NavigationCandidatePublication.Deferred;
                }
                NavigationCandidatePublication closurePublication =
                    PublishAffectedCompositionClosure(_compositionWork);
                if (closurePublication != NavigationCandidatePublication.Published)
                    return closurePublication;
                if (_publishedThisMaintenance)
                    return NavigationCandidatePublication.Deferred;
            }
            GetCompositionWorkAllowance(out long resumedBytes, out int resumedPages);
            if (!_compositionWork.Advance(
                    _maintenanceMeter,
                    resumedBytes,
                    resumedPages,
                    out bool resumedCapacityExceeded))
            {
                if (_compositionWork.RequiresAllClosePublication)
                    return PublishAllCompositionClosure(_compositionWork);
                if (resumedCapacityExceeded || !IsWithinRetainedWorkCapacity(
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
            long completedVersion = checked(_store.Current.GraphVersion + 1);
            NavigationWorldGraph completed = _compositionWork.Result.WithAreaCatalog(
                areaCatalog,
                completedVersion).WithGraphVersion(completedVersion);
            completed = PreservePriorStructuralClosures(completed);
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
                completedVersion);
            if (completedPublication == NavigationCandidatePublication.PermanentCapacity
                && !TryRollbackCompositionClosure())
            {
                return NavigationCandidatePublication.Deferred;
            }
            if (completedPublication != NavigationCandidatePublication.Deferred)
            {
                _compositionWork = null;
                if (completedPublication == NavigationCandidatePublication.Published)
                    ClearOwnedStructuralClosureBaseline();
            }
            return completedPublication;
        }

        NavigationWorldGraph current = _store.Current;
        bool hasStructuralChanges = NavigationWorldGraph.HasStructuralChanges(
            changes,
            changeCount,
            candidate,
            current);
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
                _world,
                current,
                candidate,
                changes,
                changeCount,
                hasStructuralChanges);
            GetCompositionWorkAllowance(out long initialBytes, out int initialPages);
            if (!work.Advance(
                    _maintenanceMeter,
                    initialBytes,
                    initialPages,
                    out bool initialCapacityExceeded))
            {
                if (initialCapacityExceeded)
                    return NavigationCandidatePublication.PermanentCapacity;
                if (hasStructuralChanges)
                    CaptureOwnedStructuralClosureBaseline(current);
                NavigationWorldGraph closed = hasStructuralChanges
                    ? CreateOwnedStructuralClosure(
                        current,
                        work.AffectedComponents,
                        !work.IsChangedMapCaptureComplete,
                        current.GraphVersion + 1)
                    : current;
                if (!IsWithinRetainedWorkCapacity(
                        GetCombinedCompositionWorkBytes(work, closed),
                        GetCombinedCompositionWorkPages(work, closed),
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
                if (hasStructuralChanges)
                {
                    if (work.IsChangedMapCaptureComplete)
                        work.MarkAffectedClosurePublished();
                    else
                        work.MarkAllClosePublished();
                }
                _compositionWork = work;
                return NavigationCandidatePublication.Deferred;
            }
            if (!IsWithinRetainedWorkCapacity(
                    GetCombinedCompositionWorkBytes(work),
                    GetCombinedCompositionWorkPages(work)))
            {
                return NavigationCandidatePublication.PermanentCapacity;
            }
            completedStructural = PreservePriorStructuralClosures(work.Result);
        }
        NavigationWorldGraph next = changeCount == 0
            ? current.ReopenStructuralScopes(nextVersion)
            : completedStructural!;
        if (!next.IsWithinDynamicSlotCapacity(_maxDynamicSlotsPerMap, _maxDynamicSlots))
            return NavigationCandidatePublication.PermanentCapacity;
        NavigationCandidatePublication publication = ReconcileAndPublish(
            next.WithAreaCatalog(areaCatalog, nextVersion),
            changes,
            changeCount,
            nextVersion);
        if (publication == NavigationCandidatePublication.Published)
            ClearOwnedStructuralClosureBaseline();
        return publication;
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
        if (_automaticSeamFullRebuildPending)
            return true;
        return TryPublishReopenedStructuralScopes();
    }

    private NavigationCandidatePublication PublishAffectedCompositionClosure(
        NavigationStructuralCompositionWork work)
    {
        NavigationWorldGraph current = _store.Current;
        CaptureOwnedStructuralClosureBaseline(current);
        NavigationWorldGraph narrowed = CreateOwnedStructuralClosure(
            current,
            work.AffectedComponents,
            false,
            current.GraphVersion + 1);
        if (ReferenceEquals(current, narrowed))
        {
            work.MarkAffectedClosurePublished();
            return NavigationCandidatePublication.Published;
        }
        NavigationCandidatePublication publication = ReconcileAndPublish(
            narrowed,
            Array.Empty<NavigationOperationFrameChange>(),
            0,
            narrowed.GraphVersion);
        if (publication == NavigationCandidatePublication.Published)
            work.MarkAffectedClosurePublished();
        return publication;
    }

    private NavigationCandidatePublication PublishAllCompositionClosure(
        NavigationStructuralCompositionWork work)
    {
        NavigationWorldGraph current = _store.Current;
        CaptureOwnedStructuralClosureBaseline(current);
        NavigationWorldGraph closed = current.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty,
            true,
            current.GraphVersion + 1);
        if (ReferenceEquals(current, closed))
        {
            work.MarkAllCloseRepublished();
            return NavigationCandidatePublication.Published;
        }
        NavigationCandidatePublication publication = ReconcileAndPublish(
            closed,
            Array.Empty<NavigationOperationFrameChange>(),
            0,
            closed.GraphVersion);
        if (publication == NavigationCandidatePublication.Published)
            work.MarkAllCloseRepublished();
        return publication;
    }

    private void BeginOperationClosureRollback()
    {
        _operationClosureRollbackPending = !TryPublishReopenedStructuralScopes();
    }

    private bool TryPublishReopenedStructuralScopes()
    {
        if (_automaticSeamFullRebuildPending || _publishedThisMaintenance)
            return false;
        if (_ownedStructuralClosureBaseline == null)
            return true;
        NavigationWorldGraph current = _store.Current;
        NavigationWorldGraph reopened = current.WithClosedStructuralComponents(
            _ownedStructuralClosureBaseline,
            _ownedStructuralClosureBaselineAll,
            current.GraphVersion + 1);
        if (ReferenceEquals(current, reopened))
        {
            ClearOwnedStructuralClosureBaseline();
            return true;
        }
        if (_store.TryPublish(reopened) != NavigationCandidatePublication.Published)
            return false;
        _publishedThisMaintenance = true;
        ClearOwnedStructuralClosureBaseline();
        return true;
    }

    private void CaptureOwnedStructuralClosureBaseline(NavigationWorldGraph graph)
    {
        if (_ownedStructuralClosureBaseline != null)
            return;
        _ownedStructuralClosureBaseline = graph.ClosedStructuralComponents;
        _ownedStructuralClosureBaselineAll = graph.AreAllStructuralComponentsClosed;
    }

    private NavigationWorldGraph CreateOwnedStructuralClosure(
        NavigationWorldGraph graph,
        NavigationSurfaceComponentKeySet affected,
        bool closeAll,
        long graphVersion)
    {
        return graph.WithOwnedStructuralClosure(
            _ownedStructuralClosureBaseline!,
            affected,
            closeAll || _ownedStructuralClosureBaselineAll,
            graphVersion);
    }

    private NavigationWorldGraph PreservePriorStructuralClosures(
        NavigationWorldGraph graph) => _ownedStructuralClosureBaseline == null
            ? graph
            : graph.WithClosedStructuralComponents(
                _ownedStructuralClosureBaseline,
                _ownedStructuralClosureBaselineAll,
                graph.GraphVersion);

    private void ClearOwnedStructuralClosureBaseline()
    {
        _ownedStructuralClosureBaseline = null;
        _ownedStructuralClosureBaselineAll = false;
    }

    private bool IsOperationWithinRetainedWorkCapacity(long workBytes, int workPages) =>
        IsWithinRetainedWorkCapacity(workBytes, workPages);

    private long GetCombinedCompositionWorkBytes(
        NavigationStructuralCompositionWork work,
        NavigationWorldGraph? ownerGraph = null) =>
        checked(
            work.RetainedBytes
            + _operations.RetainedOperationWorkBytes
            + GetOwnedClosureBaselineAdditionalRetainedBytes(ownerGraph));

    private int GetCombinedCompositionWorkPages(
        NavigationStructuralCompositionWork work,
        NavigationWorldGraph? ownerGraph = null) =>
        checked(
            work.PersistentPageCount
            + _operations.RetainedOperationWorkPageCount
            + GetOwnedClosureBaselineAdditionalPersistentPages(ownerGraph));

    private long GetCombinedMaterializedWorkBytes(
        NavigationMaterializedComponentWork work) =>
        checked(
            work.RetainedBytes
            + _operations.RetainedOperationWorkBytes
            + (_compositionWork?.RetainedBytes ?? 0L)
            + (_lifecycleWork?.RetainedBytes ?? 0L)
            + GetOwnedClosureBaselineAdditionalRetainedBytes());

    private int GetCombinedMaterializedWorkPages(
        NavigationMaterializedComponentWork work) =>
        checked(
            work.PersistentPageCount
            + _operations.RetainedOperationWorkPageCount
            + (_compositionWork?.PersistentPageCount ?? 0)
            + (_lifecycleWork?.PersistentPageCount ?? 0)
            + GetOwnedClosureBaselineAdditionalPersistentPages());

    private long GetOwnedClosureBaselineAdditionalRetainedBytes(
        NavigationWorldGraph? ownerGraph = null)
    {
        NavigationSurfaceComponentKeySet? baseline = _ownedStructuralClosureBaseline;
        return baseline == null
            || ReferenceEquals(baseline, NavigationSurfaceComponentKeySet.Empty)
            || (ownerGraph ?? _store.Current).RetainsClosedComponentRoot(baseline)
                ? 0L
                : baseline.RetainedBytes;
    }

    private int GetOwnedClosureBaselineAdditionalPersistentPages(
        NavigationWorldGraph? ownerGraph = null)
    {
        NavigationSurfaceComponentKeySet? baseline = _ownedStructuralClosureBaseline;
        return baseline == null
            || ReferenceEquals(baseline, NavigationSurfaceComponentKeySet.Empty)
            || (ownerGraph ?? _store.Current).RetainsClosedComponentRoot(baseline)
                ? 0
                : baseline.PersistentPageCount;
    }

    private void GetCompositionWorkAllowance(out long bytes, out int pages)
    {
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long rebuildBytes,
            out int rebuildPages);
        NavigationWorldGraph current = _store.Current;
        bytes = _maxActiveSnapshotBytes
            - current.RetainedBytes
            - rebuildBytes
            - _operations.RetainedOperationWorkBytes
            - GetOwnedClosureBaselineAdditionalRetainedBytes();
        pages = _maxPersistentGraphPages
            - current.PersistentPageCount
            - rebuildPages
            - _operations.RetainedOperationWorkPageCount
            - GetOwnedClosureBaselineAdditionalPersistentPages();
    }

    private NavigationCandidatePublication PublishPendingCandidate(
        NavigationOperationCandidate candidate,
        int frame,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        if (changeCount == 0 && _store.Current.HasClosedStructuralScope)
        {
            if (_automaticSeamFullRebuildPending)
                return NavigationCandidatePublication.Published;
            return TryPublishReopenedStructuralScopes()
                ? NavigationCandidatePublication.Published
                : NavigationCandidatePublication.Deferred;
        }
        return PublishCandidate(
            candidate,
            _publicationAreaCatalog,
            changes,
            changeCount);
    }

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
            out bool blockAll,
            out bool topologyLifecycleCoverageLost);
        _maintenanceMeter.TryConsumeEnvelopes(count);
        if (topologyLifecycleCoverageLost
            || ContainsTopologyLifecycle(_maintenanceEvents, count))
        {
            _automaticSeamFullRebuildPending = true;
        }
        if (count != 0 || blockedScopeCount != 0 || blockAll)
        {
            // Snapshot admission is already closed by the store while bounded leased generations
            // prevent publication. Drain a bounded final-state prefix now, then require an exact
            // baseline before any affected scope can reopen once publication pressure clears.
            _store.MarkSafetyPending();
            _ingress.MarkResnapshotRequired();
        }
    }

    private static bool ContainsTopologyLifecycle(GridEventInfo[] events, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GridEventKind kind = events[i].ChangeKind;
            if (kind == GridEventKind.GridAdded
                || kind == GridEventKind.GridRemoved
                || kind == GridEventKind.WorldReset)
            {
                return true;
            }
        }
        return false;
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
