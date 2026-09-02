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
    internal enum OperationTerminalDisposition
    {
        None,
        Release,
        ReleaseAndBeginRollback,
        ReleaseAndTryReopen
    }

    internal enum PolicyFrameDisposition
    {
        None,
        Complete,
        RejectCapacity
    }

    internal enum AutomaticSeamAdvanceDisposition
    {
        Continue,
        Retain,
        Restart,
        Complete
    }

    internal enum MaterializedAdvanceDisposition
    {
        Restart,
        RejectCapacity,
        Defer,
        Ready,
        Publish
    }

    internal enum PublishedCandidateEffect
    {
        InstallMaterialized,
        CompleteMaterialized,
        CompleteMaterializedWithinStructuralPublication,
        CompleteAutomaticSeam,
        CompleteComposition,
        ReopenStructuralScopes,
        StartAutomaticSeam
    }

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
        + GetOwnedClosureBaselineAdditionalRetainedBytes(_store.Current));

    internal long RetainedOperationWorkBytes => _operations.RetainedOperationWorkBytes;

    internal int RetainedOperationWorkCount => _operations.RetainedOperationWorkCount;

    internal int RetainedCompositionWorkPageCount =>
        checked(
            (_compositionWork?.PersistentPageCount ?? 0)
            + (_materializedComponentWork?.PersistentPageCount ?? 0)
            + (_lifecycleWork?.PersistentPageCount ?? 0)
            + GetOwnedClosureBaselineAdditionalPersistentPages(_store.Current));

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
        if (_operationClosureRollbackPending
            && !_automaticSeamFullRebuildPending
            && !_ingress.HasPendingWork
            && _materializedComponentWork == null
            && _lifecycleWork == null)
        {
            ApplyOperationTerminalRollback(
                OperationTerminalDisposition.ReleaseAndTryReopen);
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
        OperationTerminalDisposition terminalDisposition =
            ClassifyOperationTerminalDisposition(
                operationResult,
                _operations.RetainedOperationWorkCount,
                _compositionWork != null,
                _materializedComponentWorkCompletesOperation,
                _store.Current.HasClosedStructuralScope,
                _automaticSeamFullRebuildPending);
        if (terminalDisposition != OperationTerminalDisposition.None)
        {
            _compositionWork = null;
            AbandonMaterializedSnapshot(_materializedComponentWorkCompletesOperation);
            bool returnsAfterCleanup = terminalDisposition is
                OperationTerminalDisposition.ReleaseAndBeginRollback or
                OperationTerminalDisposition.ReleaseAndTryReopen;
            ApplyOperationTerminalRollback(terminalDisposition);
            if (returnsAfterCleanup)
                return;
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
            if (!IsWithinRetainedWorkCapacity(retainedWorkBytes, retainedWorkPages)) RejectDeferredOperationForCapacity();
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
            }
            else if (!_publishedThisMaintenance)
            {
                NavigationWorldGraph publishedGraph = _store.Current;
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
                    RejectDeferredOperationForCapacity();
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
        CompletePolicyFrame(policyFrame, frame, policyPrepared, fallbackPublication);
        _publicationAreaCatalog = _store.Current.AreaCatalog;
    }

    internal static void CompletePolicyFrame(
        NavigationAreaCatalogProcessor.PreparedFrame policyFrame,
        int frame,
        bool policyPrepared,
        NavigationCandidatePublication publication)
    {
        switch (ClassifyPolicyFrameDisposition(policyPrepared, publication))
        {
            case PolicyFrameDisposition.Complete:
                policyFrame.Complete(frame);
                break;
            case PolicyFrameDisposition.RejectCapacity:
                policyFrame.CompleteCapacityRejected();
                break;
        }
    }

    internal static PolicyFrameDisposition ClassifyPolicyFrameDisposition(
        bool policyPrepared,
        NavigationCandidatePublication publication)
    {
        if (!policyPrepared)
            return PolicyFrameDisposition.None;
        return publication switch
        {
            NavigationCandidatePublication.Published => PolicyFrameDisposition.Complete,
            NavigationCandidatePublication.PermanentCapacity => PolicyFrameDisposition.RejectCapacity,
            _ => PolicyFrameDisposition.None
        };
    }

    internal static NavigationCandidatePublication ContinueAfterRetainedClosure(
        NavigationCandidatePublication publication) =>
        publication == NavigationCandidatePublication.Published
            ? NavigationCandidatePublication.Deferred
            : publication;

    internal static AutomaticSeamAdvanceDisposition ClassifyAutomaticSeamAdvance(
        NavigationAutomaticSeamLifecycleWork.AdvanceStatus status)
    {
        if (status == NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Progressed)
            return AutomaticSeamAdvanceDisposition.Continue;
        if (status == NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Blocked)
            return AutomaticSeamAdvanceDisposition.Retain;
        return status == NavigationAutomaticSeamLifecycleWork.AdvanceStatus.Complete
            ? AutomaticSeamAdvanceDisposition.Complete
            : AutomaticSeamAdvanceDisposition.Restart;
    }

    internal static MaterializedAdvanceDisposition ClassifyMaterializedAdvance(
        bool requiresRestart,
        bool withinCapacity,
        bool complete,
        bool publish,
        out NavigationCandidatePublication? abandonmentPublication)
    {
        if (requiresRestart)
        {
            abandonmentPublication = NavigationCandidatePublication.Deferred;
            return MaterializedAdvanceDisposition.Restart;
        }
        if (!withinCapacity)
        {
            abandonmentPublication = NavigationCandidatePublication.PermanentCapacity;
            return MaterializedAdvanceDisposition.RejectCapacity;
        }
        abandonmentPublication = null;
        if (!complete)
            return MaterializedAdvanceDisposition.Defer;
        return publish
            ? MaterializedAdvanceDisposition.Publish
            : MaterializedAdvanceDisposition.Ready;
    }

    internal static OperationTerminalDisposition ClassifyOperationTerminalDisposition(
        NavigationOperationFrameResult result,
        int retainedOperationWorkCount,
        bool hasCompositionWork,
        bool materializedCompletesOperation,
        bool hasClosedStructuralScope,
        bool automaticSeamFullRebuildPending)
    {
        if (result == NavigationOperationFrameResult.Published
            && retainedOperationWorkCount == 0
            && (hasCompositionWork || materializedCompletesOperation))
        {
            return OperationTerminalDisposition.ReleaseAndBeginRollback;
        }
        if (result != NavigationOperationFrameResult.Rejected)
            return OperationTerminalDisposition.None;
        if (!hasClosedStructuralScope)
            return OperationTerminalDisposition.Release;
        return automaticSeamFullRebuildPending
            ? OperationTerminalDisposition.ReleaseAndBeginRollback
            : OperationTerminalDisposition.ReleaseAndTryReopen;
    }

    internal static void GetOwnedClosureBaselineAdditionalCapacity(
        NavigationSurfaceComponentKeySet? baseline,
        NavigationWorldGraph ownerGraph,
        out long retainedBytes,
        out int persistentPages)
    {
        if (baseline == null
            || ReferenceEquals(baseline, NavigationSurfaceComponentKeySet.Empty)
            || ownerGraph.RetainsClosedComponentRoot(baseline))
        {
            retainedBytes = 0;
            persistentPages = 0;
            return;
        }
        retainedBytes = baseline.RetainedBytes;
        persistentPages = baseline.PersistentPageCount;
    }

    internal static bool IsRetainedWorkWithinCapacity(
        long rootBytes,
        int rootPages,
        long workBytes,
        int workPages,
        long rebuildBytes,
        int rebuildPages,
        long maximumBytes,
        int maximumPages)
    {
        System.Diagnostics.Debug.Assert(workPages >= 0);
        System.Diagnostics.Debug.Assert(rebuildPages >= 0);
        long retainedPages = (long)rootPages + workPages + rebuildPages;
        return rootBytes <= maximumBytes
            && workBytes <= maximumBytes - rootBytes
            && rebuildBytes <= maximumBytes - rootBytes - workBytes
            && retainedPages <= maximumPages;
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
            return MaintainMaterializedComponentWork(structuralClosure: graph);
        AdvanceCoveredBaselineRebuilds(graph);
        _snapshotGraph = graph;
        _snapshotPreviousGraph = _store.Current;
        _snapshotChanges = changes;
        _snapshotChangeCount = changeCount;
        _snapshotStartsAutomaticSeamLifecycle = startAutomaticSeamLifecycle;
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
                    graphVersion)
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
            ClearSafetyPendingAfterExactResnapshot();
        }
        else
        {
            bool hasMaterializedWork = _materializedComponentWork != null;
            bool materializedWorkOwnsAllClosedSource = hasMaterializedWork
                && _store.Current.AreAllStructuralComponentsClosed;
            if (_automaticSeamFullRebuildPending
                && !materializedWorkOwnsAllClosedSource)
            {
                _store.MarkSafetyPending();
            }
            if (!hasMaterializedWork)
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
        System.Diagnostics.Debug.Assert(!ReferenceEquals(current, closed),
            "Materialized work closes a newly versioned maintenance candidate.");
        NavigationCandidatePublication publication = _store.TryPublish(closed);
        ApplyPublishedCandidate(
            publication,
            PublishedCandidateEffect.InstallMaterialized,
            work,
            startsAutomaticSeamLifecycle);
        return ContinueAfterRetainedClosure(publication);
    }

    private NavigationCandidatePublication MaintainMaterializedComponentWork(
        bool publish = true,
        NavigationWorldGraph? structuralClosure = null)
    {
        NavigationMaterializedComponentWork work = _materializedComponentWork!;
        bool complete = work.Advance(_maintenanceMeter);
        bool requiresRestart = work.RequiresSnapshotRestart;
        bool revalidated = !requiresRestart
            && complete
            && work.RevalidateForPublication();
        requiresRestart |= work.RequiresSnapshotRestart;
        bool withinCapacity = requiresRestart
            || IsWithinRetainedWorkCapacity(
                GetCombinedMaterializedWorkBytes(work),
                GetCombinedMaterializedWorkPages(work));
        MaterializedAdvanceDisposition disposition = ClassifyMaterializedAdvance(
            requiresRestart,
            withinCapacity,
            complete,
            publish,
            out NavigationCandidatePublication? abandonmentPublication);
        if (abandonmentPublication.HasValue)
            return RestartMaterializedSnapshot(abandonmentPublication.Value);
        if (disposition == MaterializedAdvanceDisposition.Defer)
            return NavigationCandidatePublication.Deferred;
        System.Diagnostics.Debug.Assert(revalidated,
            "Completed failed revalidation always requests a snapshot restart above.");
        if (disposition == MaterializedAdvanceDisposition.Ready)
            return NavigationCandidatePublication.Published;

        NavigationWorldGraph current = _store.Current;
        System.Diagnostics.Debug.Assert(_ownedStructuralClosureBaseline != null,
            "Retained materialized work owns the structural-closure baseline captured at installation.");
        NavigationWorldGraph completed = structuralClosure == null
            ? work.Result.WithClosedStructuralComponents(
                _ownedStructuralClosureBaseline!,
                _ownedStructuralClosureBaselineAll,
                current.GraphVersion + 1)
            : work.Result.WithStructuralClosureFrom(
                structuralClosure,
                current.GraphVersion + 1);
        NavigationCandidatePublication publication;
        if (_materializedStartsAutomaticSeamLifecycle)
        {
            _publicationEventCount = work.RetainedEventCount;
            publication = StartAutomaticSeamLifecycle(
                current,
                completed,
                completed.GraphVersion);
        }
        else
        {
            publication = _store.TryPublish(completed);
        }
        ApplyPublishedCandidate(
            publication,
            structuralClosure == null
                ? PublishedCandidateEffect.CompleteMaterialized
                : PublishedCandidateEffect.CompleteMaterializedWithinStructuralPublication);
        return publication;
    }

    private NavigationCandidatePublication RestartMaterializedSnapshot(
        NavigationCandidatePublication publication = NavigationCandidatePublication.Deferred)
    {
        AbandonMaterializedSnapshot();
        return publication;
    }

    private void AbandonMaterializedSnapshot(bool required = true)
    {
        if (!required)
            return;
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
                || rebuild.IsComplete)
            {
                continue;
            }
            bool mapFound = graph.TryGetMap(
                rebuild.MapId,
                out NavigationMapInstance instance);
            System.Diagnostics.Debug.Assert(
                mapFound && rebuild.Matches(instance),
                "An inspectable covered-baseline rebuild retains the exact live map generation that owns it.");
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
        long graphVersion)
    {
        NavigationWorldGraph closed = prepared.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty,
            true,
            graphVersion);
        System.Diagnostics.Debug.Assert(IsWithinRetainedWorkCapacity(
                checked(
                    NavigationAutomaticSeamLifecycleWork.BaseRetainedBytes
                    + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes),
                5,
                closed),
            "Successful materialized preparation strictly dominates the automatic-seam minimum retained-work envelope.");
        System.Diagnostics.Debug.Assert(!ReferenceEquals(current, closed),
            "Automatic seam lifecycle starts from a newly versioned closed candidate.");
        NavigationCandidatePublication publication = _store.TryPublish(closed);
        System.Diagnostics.Debug.Assert(
            publication == NavigationCandidatePublication.Published,
            "Maintenance preflight and retained-work bounds guarantee automatic-seam closure publication.");
        _store.SetSafetyPending(false);
        ApplyPublishedCandidate(
            publication,
            PublishedCandidateEffect.StartAutomaticSeam,
            automaticSeamGraph: closed);
        return publication;
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
            AutomaticSeamAdvanceDisposition disposition =
                ClassifyAutomaticSeamAdvance(status);
            if (disposition == AutomaticSeamAdvanceDisposition.Complete)
            {
                bool revalidated = work.RevalidateForPublication();
                bool withinCapacity = IsWithinRetainedWorkCapacity(0, 0, work.Result);
                System.Diagnostics.Debug.Assert(revalidated,
                    "A completed seam cursor is revalidated on the same deterministic maintenance boundary.");
                System.Diagnostics.Debug.Assert(withinCapacity,
                    "Completed automatic-seam work remains inside its exact retained-work allowance.");
                disposition = AutomaticSeamAdvanceDisposition.Complete;
            }
            switch (disposition)
            {
                case AutomaticSeamAdvanceDisposition.Continue:
                    continue;
                case AutomaticSeamAdvanceDisposition.Retain:
                    return;
                case AutomaticSeamAdvanceDisposition.Restart:
                    RestartAutomaticSeamLifecycle();
                    return;
                case AutomaticSeamAdvanceDisposition.Complete:
                    NavigationWorldGraph next = work.Result;
                    NavigationCandidatePublication publication = _store.TryPublish(next);
                    ApplyPublishedCandidate(
                        publication,
                        PublishedCandidateEffect.CompleteAutomaticSeam);
                    return;
            }
        }
    }

    private void GetLifecycleWorkAllowance(out long bytes, out int pages)
    {
        System.Diagnostics.Debug.Assert(_compositionWork == null,
            "Automatic-seam lifecycle work starts only from ingress reconciliation without operation composition ownership.");
        NavigationBaselineRebuild.GetRetainedTotals(
            _baselineRebuilds,
            out long rebuildBytes,
            out int rebuildPages);
        NavigationWorldGraph current = _store.Current;
        bytes = _maxActiveSnapshotBytes
            - current.RetainedBytes
            - rebuildBytes
            - _operations.RetainedOperationWorkBytes;
        pages = _maxPersistentGraphPages
            - current.PersistentPageCount
            - rebuildPages
            - _operations.RetainedOperationWorkPageCount;
    }

    private void RequeueAutomaticSeamLifecyclePrefix()
    {
        _ingress.RequeuePrefix(_maintenanceEvents.AsSpan(0, _lifecycleEventCount));
        _lifecycleEventCount = 0;
    }

    private void RestartAutomaticSeamLifecycle()
    {
        RequeueAutomaticSeamLifecyclePrefix();
        _lifecycleWork = null;
    }

    internal bool TryGetCellState(
        string mapId,
        VoxelIndex index,
        out NavigationGraphCellState state)
    {
        EnsureUsable();
        NavigationWorldGraph graph = _store.Current;
        if (!graph.TryGetMap(mapId, out NavigationMapInstance instance)
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
        int baselineCapacityBlockedCount =
            NavigationBaselineRebuild.CountCapacityBlocked(_baselineRebuilds);
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
            else if (!key.Equals(selected))
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
        MarkSafetyPendingIf(_resetPending);
        if (!_resetPending)
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
        if (_compositionWork != null)
        {
            if (_compositionWork.RequiresAllClosePublication) return PublishAllCompositionClosureAndDefer(_compositionWork);
            if (_compositionWork.RequiresAffectedClosurePublication)
            {
                if (!_compositionWork.RevalidateAutomaticSeamsForPublication()) return NavigationCandidatePublication.Deferred;
                return PublishAffectedCompositionClosureAndDefer(_compositionWork);
            }
            GetCompositionWorkAllowance(out long resumedBytes, out int resumedPages);
            if (!_compositionWork.Advance(
                    _maintenanceMeter,
                    resumedBytes,
                    resumedPages,
                    out bool resumedCapacityExceeded))
            {
                System.Diagnostics.Debug.Assert(
                    !_compositionWork.RequiresAllClosePublication,
                    "Affected closure publication follows completed seam capture; incomplete advancement cannot invalidate that completed cursor.");
                if (resumedCapacityExceeded) return RollbackCompositionForCapacityRejection();
                System.Diagnostics.Debug.Assert(IsWithinRetainedWorkCapacity(
                        GetCombinedCompositionWorkBytes(_compositionWork, _store.Current),
                        GetCombinedCompositionWorkPages(_compositionWork, _store.Current)),
                    "A non-capacity blocked advance remains inside its exact unchanged-root allowance.");
                return NavigationCandidatePublication.Deferred;
            }
            if (!_compositionWork.RevalidateAutomaticSeamsForPublication())
            {
                System.Diagnostics.Debug.Assert(
                    _compositionWork.RequiresAllClosePublication,
                    "Failed completed-work revalidation invalidates an affected-closure owner and requires all-close republication.");
                return PublishAllCompositionClosureAndDefer(_compositionWork);
            }
            System.Diagnostics.Debug.Assert(IsWithinRetainedWorkCapacity(
                    GetCombinedCompositionWorkBytes(_compositionWork, _store.Current),
                    GetCombinedCompositionWorkPages(_compositionWork, _store.Current)),
                "Completed revalidated work has not grown since its exact advance allowance check.");
            long completedVersion = checked(_store.Current.GraphVersion + 1);
            NavigationWorldGraph completed = _compositionWork.Result.WithAreaCatalog(
                areaCatalog,
                completedVersion).WithGraphVersion(completedVersion);
            completed = PreservePriorStructuralClosures(completed);
            if (!completed.IsWithinDynamicSlotCapacity(_maxDynamicSlotsPerMap, _maxDynamicSlots)) return RollbackCompositionForCapacityRejection();
            NavigationCandidatePublication completedPublication = ReconcileAndPublish(
                completed,
                changes,
                changeCount,
                completedVersion);
            if (completedPublication == NavigationCandidatePublication.PermanentCapacity) return RollbackCompositionForCapacityRejection();
            ApplyPublishedCandidate(
                completedPublication,
                PublishedCandidateEffect.CompleteComposition);
            return completedPublication;
        }

        NavigationWorldGraph current = _store.Current;
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
                    checked(candidateGrowthPages + minimumScratchPages))) return RejectInitialCompositionForCapacity();
            var work = new NavigationStructuralCompositionWork(
                _world,
                current,
                candidate,
                changes,
                changeCount);
            GetCompositionWorkAllowance(out long initialBytes, out int initialPages);
            if (!work.Advance(
                    _maintenanceMeter,
                    initialBytes,
                    initialPages,
                    out _))
            {
                NavigationSurfaceComponentKeySet closureBaseline =
                    _ownedStructuralClosureBaseline ?? current.ClosedStructuralComponents;
                bool closureBaselineAll = _ownedStructuralClosureBaseline == null
                    ? current.AreAllStructuralComponentsClosed
                    : _ownedStructuralClosureBaselineAll;
                NavigationWorldGraph closed = current.WithOwnedStructuralClosure(
                    closureBaseline,
                    work.AffectedComponents,
                    !work.IsChangedMapCaptureComplete || closureBaselineAll,
                    current.GraphVersion + 1);
                if (!IsWithinRetainedWorkCapacity(
                        GetCombinedCompositionWorkBytes(work, closed),
                        GetCombinedCompositionWorkPages(work, closed),
                        closed)) return RejectInitialCompositionForCapacity();
                CaptureOwnedStructuralClosureBaseline(current);
                System.Diagnostics.Debug.Assert(!ReferenceEquals(closed, current),
                    "An operation closure always advances the graph version.");
                NavigationCandidatePublication closedPublication = ReconcileAndPublish(
                    closed,
                    Array.Empty<NavigationOperationFrameChange>(),
                    0,
                    closed.GraphVersion);
                if (closedPublication != NavigationCandidatePublication.Published) return closedPublication;
                work.MarkInitialClosurePublished();
                _compositionWork = work;
                return NavigationCandidatePublication.Deferred;
            }
            completedStructural = PreservePriorStructuralClosures(work.Result);
        }
        NavigationWorldGraph next = changeCount == 0
            ? current.ReopenStructuralScopes(nextVersion)
            : completedStructural!;
        if (!next.IsWithinDynamicSlotCapacity(_maxDynamicSlotsPerMap, _maxDynamicSlots)) return RejectInitialCompositionForCapacity();
        NavigationCandidatePublication publication = ReconcileAndPublish(
            next.WithAreaCatalog(areaCatalog, nextVersion),
            changes,
            changeCount,
            nextVersion);
        ApplyPublishedCandidate(
            publication,
            PublishedCandidateEffect.CompleteComposition);
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
        return IsRetainedWorkWithinCapacity(
            root.RetainedBytes,
            root.PersistentPageCount,
            workBytes,
            workPages,
            rebuildBytes,
            rebuildPages,
            _maxActiveSnapshotBytes,
            _maxPersistentGraphPages);
    }

    private bool TryRollbackCompositionClosure()
        => _automaticSeamFullRebuildPending || TryPublishReopenedStructuralScopes();

    private NavigationCandidatePublication RollbackCompositionForCapacityRejection()
    {
        bool rollbackCompleted = TryRollbackCompositionClosure();
        System.Diagnostics.Debug.Assert(rollbackCompleted,
            "A retained composition rolls back only after the maintenance writer preflight succeeds.");
        _compositionWork = null;
        return NavigationCandidatePublication.PermanentCapacity;
    }

    private NavigationCandidatePublication RejectInitialCompositionForCapacity()
    {
        System.Diagnostics.Debug.Assert(_compositionWork == null,
            "Initial composition capacity rejection cannot own retained composition work.");
        return NavigationCandidatePublication.PermanentCapacity;
    }

    private void CompleteCompositionPublication()
    {
        _compositionWork = null;
        ClearOwnedStructuralClosureBaseline();
    }

    private NavigationCandidatePublication PublishAffectedCompositionClosureAndDefer(
        NavigationStructuralCompositionWork work)
    {
        NavigationWorldGraph current = _store.Current;
        CaptureOwnedStructuralClosureBaseline(current);
        NavigationWorldGraph narrowed = CreateOwnedStructuralClosure(
            current,
            work.AffectedComponents,
            false,
            current.GraphVersion + 1);
        System.Diagnostics.Debug.Assert(!ReferenceEquals(current, narrowed),
            "Narrowing an owned all-close publication changes its closure root.");
        NavigationCandidatePublication publication = ReconcileAndPublish(
            narrowed,
            Array.Empty<NavigationOperationFrameChange>(),
            0,
            narrowed.GraphVersion);
        work.RecordAffectedClosurePublication(publication);
        return ContinueAfterRetainedClosure(publication);
    }

    private NavigationCandidatePublication PublishAllCompositionClosureAndDefer(
        NavigationStructuralCompositionWork work)
    {
        NavigationWorldGraph current = _store.Current;
        CaptureOwnedStructuralClosureBaseline(current);
        NavigationWorldGraph closed = current.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty,
            true,
            current.GraphVersion + 1);
        System.Diagnostics.Debug.Assert(!ReferenceEquals(current, closed),
            "A stale affected closure must broaden before seam work restarts.");
        NavigationCandidatePublication publication = ReconcileAndPublish(
            closed,
            Array.Empty<NavigationOperationFrameChange>(),
            0,
            closed.GraphVersion);
        work.RecordAllClosePublication(publication);
        return ContinueAfterRetainedClosure(publication);
    }

    private void BeginOperationClosureRollback()
    {
        System.Diagnostics.Debug.Assert(_materializedComponentWork == null);
        System.Diagnostics.Debug.Assert(_lifecycleWork == null);
        System.Diagnostics.Debug.Assert(
            _automaticSeamFullRebuildPending
                || _publishedThisMaintenance
                || _ingress.HasPendingWork,
            "Terminal work begins rollback only while this maintenance pass still owns publication or ingress state.");
        _operationClosureRollbackPending = true;
    }

    private void ApplyOperationTerminalRollback(
        OperationTerminalDisposition disposition)
    {
        System.Diagnostics.Debug.Assert(disposition != OperationTerminalDisposition.None);
        if (disposition == OperationTerminalDisposition.ReleaseAndBeginRollback)
        {
            BeginOperationClosureRollback();
            return;
        }
        if (disposition == OperationTerminalDisposition.ReleaseAndTryReopen)
            TryCompleteOperationClosureRollback();
    }

    private void RejectDeferredOperationForCapacity()
    {
        _operations.RejectDeferredCapacity();
        _compositionWork = null;
        BeginOperationClosureRollback();
    }

    private void TryCompleteOperationClosureRollback()
    {
        _operationClosureRollbackPending = !TryPublishReopenedStructuralScopes();
    }

    private bool TryPublishReopenedStructuralScopes()
    {
        System.Diagnostics.Debug.Assert(!_automaticSeamFullRebuildPending);
        System.Diagnostics.Debug.Assert(!_publishedThisMaintenance);
        System.Diagnostics.Debug.Assert(
            _ownedStructuralClosureBaseline != null,
            "Only an operation-owned structural closure enters rollback.");
        NavigationWorldGraph current = _store.Current;
        NavigationWorldGraph reopened = current.WithClosedStructuralComponents(
            _ownedStructuralClosureBaseline!,
            _ownedStructuralClosureBaselineAll,
            current.GraphVersion + 1);
        System.Diagnostics.Debug.Assert(!ReferenceEquals(current, reopened),
            "An owned closure differs from the baseline it must restore.");
        NavigationCandidatePublication publication = _store.TryPublish(reopened);
        ApplyPublishedCandidate(
            publication,
            PublishedCandidateEffect.ReopenStructuralScopes);
        bool published = publication == NavigationCandidatePublication.Published;
        return published;
    }

    private void ApplyPublishedCandidate(
        NavigationCandidatePublication publication,
        PublishedCandidateEffect effect,
        NavigationMaterializedComponentWork? materializedWork = null,
        bool startsAutomaticSeamLifecycle = false,
        NavigationWorldGraph? automaticSeamGraph = null)
    {
        if (publication != NavigationCandidatePublication.Published)
            return;
        _publishedThisMaintenance = true;
        if (effect == PublishedCandidateEffect.InstallMaterialized)
        {
            System.Diagnostics.Debug.Assert(materializedWork != null);
            _materializedComponentWork = materializedWork;
            _materializedComponentWorkCompletesOperation =
                _operations.RetainedOperationWorkCount != 0;
            _materializedStartsAutomaticSeamLifecycle = startsAutomaticSeamLifecycle;
            return;
        }
        if (effect == PublishedCandidateEffect.CompleteMaterialized
            || effect == PublishedCandidateEffect.CompleteMaterializedWithinStructuralPublication)
        {
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
            ClearSafetyPendingAfterExactResnapshot();
            if (effect == PublishedCandidateEffect.CompleteMaterialized)
                ClearOwnedStructuralClosureBaseline();
            return;
        }
        if (effect == PublishedCandidateEffect.CompleteAutomaticSeam)
        {
            _lifecycleWork = null;
            _lifecycleEventCount = 0;
            _automaticSeamFullRebuildPending = false;
            _store.ClearSafetyPending();
            return;
        }
        if (effect == PublishedCandidateEffect.CompleteComposition)
        {
            CompleteCompositionPublication();
            return;
        }
        if (effect == PublishedCandidateEffect.ReopenStructuralScopes)
        {
            ClearOwnedStructuralClosureBaseline();
            return;
        }
        System.Diagnostics.Debug.Assert(
            effect == PublishedCandidateEffect.StartAutomaticSeam,
            "Every non-terminal published-candidate effect starts automatic-seam work.");
        System.Diagnostics.Debug.Assert(automaticSeamGraph != null);
        _lifecycleEventCount = _publicationEventCount;
        _lifecycleWork = new NavigationAutomaticSeamLifecycleWork(
            _world,
            automaticSeamGraph!,
            _maintenanceEvents,
            _lifecycleEventCount,
            _automaticSeamFullRebuildPending);
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
        NavigationWorldGraph ownerGraph) =>
        checked(
            work.RetainedBytes
            + _operations.RetainedOperationWorkBytes
            + GetOwnedClosureBaselineAdditionalRetainedBytes(ownerGraph));

    private int GetCombinedCompositionWorkPages(
        NavigationStructuralCompositionWork work,
        NavigationWorldGraph ownerGraph) =>
        checked(
            work.PersistentPageCount
            + _operations.RetainedOperationWorkPageCount
            + GetOwnedClosureBaselineAdditionalPersistentPages(ownerGraph));

    private long GetCombinedMaterializedWorkBytes(
        NavigationMaterializedComponentWork work)
    {
        System.Diagnostics.Debug.Assert(_lifecycleWork == null,
            "Materialized and automatic-seam lifecycle work are mutually exclusive.");
        return checked(
            work.RetainedBytes
            + _operations.RetainedOperationWorkBytes
            + (_compositionWork?.RetainedBytes ?? 0L)
            + GetOwnedClosureBaselineAdditionalRetainedBytes(_store.Current));
    }

    private int GetCombinedMaterializedWorkPages(
        NavigationMaterializedComponentWork work)
    {
        System.Diagnostics.Debug.Assert(_lifecycleWork == null,
            "Materialized and automatic-seam lifecycle work are mutually exclusive.");
        return checked(
            work.PersistentPageCount
            + _operations.RetainedOperationWorkPageCount
            + (_compositionWork?.PersistentPageCount ?? 0)
            + GetOwnedClosureBaselineAdditionalPersistentPages(_store.Current));
    }

    private long GetOwnedClosureBaselineAdditionalRetainedBytes(
        NavigationWorldGraph ownerGraph)
    {
        GetOwnedClosureBaselineAdditionalCapacity(
            _ownedStructuralClosureBaseline,
            ownerGraph,
            out long retainedBytes,
            out _);
        return retainedBytes;
    }

    private int GetOwnedClosureBaselineAdditionalPersistentPages(
        NavigationWorldGraph ownerGraph)
    {
        GetOwnedClosureBaselineAdditionalCapacity(
            _ownedStructuralClosureBaseline,
            ownerGraph,
            out _,
            out int persistentPages);
        return persistentPages;
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
            - GetOwnedClosureBaselineAdditionalRetainedBytes(current);
        pages = _maxPersistentGraphPages
            - current.PersistentPageCount
            - rebuildPages
            - _operations.RetainedOperationWorkPageCount
            - GetOwnedClosureBaselineAdditionalPersistentPages(current);
    }

    private NavigationCandidatePublication PublishPendingCandidate(
        NavigationOperationCandidate candidate,
        int frame,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        if (changeCount == 0 && _store.Current.HasClosedStructuralScope)
        {
            bool available = _automaticSeamFullRebuildPending
                || TryPublishReopenedStructuralScopes();
            System.Diagnostics.Debug.Assert(available,
                "A no-change operation reopens its owned closure only after writer preflight succeeds.");
            return NavigationCandidatePublication.Published;
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

        _priorBlockedScopeCount = MergeBlockedScopes(
            _blockedScopes,
            blockedScopeCount,
            _deferredScopes,
            deferredScopeCount,
            _priorBlockedScopes,
            out _priorBlockAll);
    }

    internal static int MergeBlockedScopes(
        NavigationGridChangeScope[] blockedScopes,
        int blockedScopeCount,
        NavigationGridChangeScope[] deferredScopes,
        int deferredScopeCount,
        NavigationGridChangeScope[] destination,
        out bool blockAll)
    {
        System.Diagnostics.Debug.Assert(blockedScopeCount <= destination.Length,
            "The runtime sizes blocked-scope capture to its committed-scope capacity.");
        int count = 0;
        for (int i = 0; i < blockedScopeCount; i++)
            destination[count++] = blockedScopes[i];
        for (int i = 0; i < deferredScopeCount; i++)
        {
            NavigationGridChangeScope deferred = deferredScopes[i];
            if (ContainsScope(destination, count, deferred.ConfigurationKey))
                continue;
            if (count == destination.Length)
            {
                blockAll = true;
                return 0;
            }
            destination[count++] = deferred;
        }
        blockAll = false;
        return count;
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

    private void MarkSafetyPendingIf(bool required)
    {
        if (required)
            _store.MarkSafetyPending();
    }

    private void ClearSafetyPendingAfterExactResnapshot()
    {
        System.Diagnostics.Debug.Assert(!_publicationResnapshotAll || !_publicationBlockAll);
        if (CanClearSafetyPendingAfterSnapshot(
                _store.IsSafetyPending,
                _publicationResnapshotAll,
                _publicationBlockedScopeCount,
                _snapshotDeferAll,
                _snapshotDeferredScopeCount))
        {
            _store.ClearSafetyPending();
        }
    }

    internal static bool CanClearSafetyPendingAfterSnapshot(
        bool safetyPending,
        bool resnapshotAll,
        int blockedScopeCount,
        bool deferAll,
        int deferredScopeCount) => safetyPending
        && resnapshotAll
        && blockedScopeCount == 0
        && !deferAll
        && deferredScopeCount == 0;

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
        bool requiresResnapshot = count != 0 || blockedScopeCount != 0 || blockAll;
        MarkSafetyPendingIf(requiresResnapshot);
        if (requiresResnapshot)
        {
            // Snapshot admission is already closed by the store while bounded leased generations
            // prevent publication. Drain a bounded final-state prefix now, then require an exact
            // baseline before any affected scope can reopen once publication pressure clears.
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
