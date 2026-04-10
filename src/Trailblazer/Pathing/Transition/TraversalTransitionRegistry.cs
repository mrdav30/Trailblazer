using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Global registry for authored traversal transitions.
/// </summary>
/// <remarks>
/// Registration resolves transition endpoints to the current voxel grid and keeps transitions
/// registered even while their resolved endpoints are temporarily unsupported. Manual transitions
/// participate in the same active versus suppressed lifecycle model as chart-generated transitions.
/// External <see cref="GlobalGridManager"/> add and remove events are reevaluated through
/// <see cref="PathManager"/>'s grid-lifecycle bridge, and external grid reset is treated as a hard
/// pathing reset.
/// </remarks>
public static class TraversalTransitionRegistry
{
    /// <summary>
    /// Default priority assigned to manual transition registrations.
    /// </summary>
    public const int DefaultManualPriority = 1;

    private static readonly SwiftDictionary<string, RegisteredTraversalTransition> _transitions =
        new(8, StringComparer.Ordinal);

    private static readonly SwiftHashSet<string> _activeTransitionIds = new();

    private static readonly SwiftHashSet<string> _suppressedManagedTransitionIds = new();

    private static readonly SwiftDictionary<GlobalVoxelIndex, SwiftHashSet<string>> _managedManualTransitionIdsByVoxel = new();

    private static readonly SwiftDictionary<GlobalVoxelIndex, SwiftHashSet<string>> _outgoingTransitionIdsByVoxel = new();

    private static readonly SwiftDictionary<GlobalVoxelIndex, SwiftHashSet<string>> _incomingTransitionIdsByVoxel = new();

    private static readonly SwiftDictionary<int, SwiftHashSet<string>> _transitionIdsBySourceGrid = new();

    private static readonly SwiftDictionary<int, SwiftHashSet<string>> _transitionIdsByDestinationGrid = new();

    private static readonly ReaderWriterLockSlim _transitionLock = new();

    private static int _registryVersion;

    private static int _registrationOrder;

    private static TraversalTransition[] _allTransitionsSnapshot = Array.Empty<TraversalTransition>();

    /// <summary>
    /// Monotonic version used to invalidate cache keys when transition topology changes.
    /// </summary>
    public static int RegistryVersion => _registryVersion;

    /// <summary>
    /// Returns a snapshot of all currently active transitions after precedence is applied.
    /// </summary>
    public static TraversalTransition[] AllTransitions
    {
        get
        {
            _transitionLock.EnterReadLock();
            try
            {
                TraversalTransition[] snapshot = _allTransitionsSnapshot;
                if (snapshot.Length == 0)
                    return Array.Empty<TraversalTransition>();

                var copy = new TraversalTransition[snapshot.Length];
                Array.Copy(snapshot, copy, snapshot.Length);
                return copy;
            }
            finally
            {
                _transitionLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Registers a managed manual traversal transition and resolves both endpoints against the active voxel grid.
    /// </summary>
    /// <param name="transition">The authored transition to register.</param>
    /// <param name="priority">
    /// The precedence priority used when multiple registered transitions have the same effective semantics.
    /// Higher priority wins before registration order ties are considered.
    /// </param>
    /// <returns>True when the transition is registered; false when the id already exists or either endpoint has no voxel.</returns>
    public static bool Register(TraversalTransition transition, int priority = DefaultManualPriority) =>
        RegisterInternal(
            transition,
            TraversalTransitionOwnershipKind.ManagedManual,
            priority,
            startSuppressed: false);

    internal static bool RegisterGenerated(
        TraversalTransition transition,
        int priority = 0,
        bool startSuppressed = false) =>
        RegisterInternal(
            transition,
            TraversalTransitionOwnershipKind.ManagedGenerated,
            priority,
            startSuppressed);

    internal static bool RegisterGeneratedRange(
        TraversalTransition[] transitions,
        int priority,
        bool startSuppressed = false)
    {
        if (transitions == null)
            throw new ArgumentNullException(nameof(transitions));

        if (transitions.Length == 0)
            return true;

        for (int i = 0; i < transitions.Length; i++)
        {
            if (!TryResolveAnchorVoxelIndex(transitions[i].Source, out _)
                || !TryResolveAnchorVoxelIndex(transitions[i].Destination, out _))
            {
                return false;
            }
        }

        _transitionLock.EnterWriteLock();
        try
        {
            string[] addedIds = new string[transitions.Length];
            int addedCount = 0;

            for (int i = 0; i < transitions.Length; i++)
            {
                TraversalTransition transition = transitions[i];
                if (_transitions.ContainsKey(transition.Id))
                {
                    RollbackRegisteredTransitions_NoLock(addedIds, addedCount);
                    return false;
                }

                var registered = new RegisteredTraversalTransition(
                    transition,
                    TraversalTransitionOwnershipKind.ManagedGenerated,
                    priority,
                    ++_registrationOrder);

                _transitions.Add(transition.Id, registered);
                addedIds[addedCount++] = transition.Id;

                if (startSuppressed)
                    _suppressedManagedTransitionIds.Add(transition.Id);
            }

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
            return true;
        }
        finally
        {
            _transitionLock.ExitWriteLock();
        }
    }

    private static bool RegisterInternal(
        TraversalTransition transition,
        TraversalTransitionOwnershipKind ownershipKind,
        int priority,
        bool startSuppressed)
    {
        // Validate that both endpoints resolve to voxels in the active grid setup before acquiring the lock.
        if (!TryResolveAnchorVoxelIndex(transition.Source, out _)
            || !TryResolveAnchorVoxelIndex(transition.Destination, out _))
        {
            return false;
        }

        _transitionLock.EnterWriteLock();
        try
        {
            if (_transitions.ContainsKey(transition.Id))
                return false;

            var registered = new RegisteredTraversalTransition(
                transition,
                ownershipKind,
                priority,
                ++_registrationOrder);

            if (IsManualOwnershipKind(ownershipKind)
                && CheckDuplicateManualTransition_NoLock(registered))
            {
                GridForgeLogger.Warn(
                    $"Ignored duplicate manual traversal transition '{transition.Id}' because it matches an existing manual transition.");
                return false;
            }

            _transitions.Add(transition.Id, registered);
            if (IsManualOwnershipKind(ownershipKind))
                AddManagedManualDependencyIndexes_NoLock(registered);

            if ((startSuppressed && IsManagedOwnershipKind(ownershipKind))
                || (ownershipKind == TraversalTransitionOwnershipKind.ManagedManual
                    && !IsManagedManualTransitionCurrentlyActive(registered)))
            {
                _suppressedManagedTransitionIds.Add(transition.Id);
            }

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
            return true;
        }
        finally
        {
            _transitionLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Returns true if a transition with the provided id exists.
    /// </summary>
    public static bool IsRegistered(string id)
    {
        _transitionLock.EnterReadLock();
        try { return _transitions.ContainsKey(id); }
        finally { _transitionLock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns true if the transition is currently active for query purposes after precedence is applied.
    /// </summary>
    public static bool IsActive(string id)
    {
        _transitionLock.EnterReadLock();
        try { return _activeTransitionIds.Contains(id); }
        finally { _transitionLock.ExitReadLock(); }
    }

    /// <summary>
    /// Attempts to retrieve a registered transition by id, even if it is currently inactive.
    /// </summary>
    public static bool TryGet(string id, out TraversalTransition transition)
    {
        _transitionLock.EnterReadLock();
        try
        {
            if (_transitions.TryGetValue(id, out RegisteredTraversalTransition registered))
            {
                transition = registered.Transition;
                return true;
            }

            transition = default;
            return false;
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Attempts to retrieve the voxel indices resolved for a registered transition, even if it is currently inactive.
    /// </summary>
    public static bool TryGetResolvedEndpoints(
        string id,
        out GlobalVoxelIndex sourceVoxelIndex,
        out GlobalVoxelIndex destinationVoxelIndex)
    {
        _transitionLock.EnterReadLock();
        try
        {
            if (_transitions.TryGetValue(id, out RegisteredTraversalTransition registered))
            {
                sourceVoxelIndex = registered.SourceVoxelIndex;
                destinationVoxelIndex = registered.DestinationVoxelIndex;
                return true;
            }

            sourceVoxelIndex = default;
            destinationVoxelIndex = default;
            return false;
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes a transition by id.
    /// </summary>
    public static bool Unregister(string id)
    {
        _transitionLock.EnterWriteLock();
        try
        {
            if (!_transitions.TryGetValue(id, out RegisteredTraversalTransition registered))
                return false;

            if (!_transitions.Remove(id))
                return false;

            RemoveManagedManualDependencyIndexes_NoLock(registered);
            _suppressedManagedTransitionIds.Remove(id);
            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
            return true;
        }
        finally { _transitionLock.ExitWriteLock(); }
    }

    internal static void UnregisterRange(string[] ids, int count = -1)
    {
        if (ids == null || ids.Length == 0)
            return;

        int unregisterCount = count < 0 || count > ids.Length
            ? ids.Length
            : count;
        if (unregisterCount == 0)
            return;

        _transitionLock.EnterWriteLock();
        try
        {
            bool removedAny = false;
            for (int i = 0; i < unregisterCount; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!_transitions.TryGetValue(id, out RegisteredTraversalTransition registered))
                    continue;

                if (_transitions.Remove(id))
                {
                    RemoveManagedManualDependencyIndexes_NoLock(registered);
                    _suppressedManagedTransitionIds.Remove(id);
                    removedAny = true;
                }
            }

            if (!removedAny)
                return;

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
        }
        finally { _transitionLock.ExitWriteLock(); }
    }

    /// <summary>
    /// Returns the currently active transitions whose authored source anchor resolves to the provided voxel.
    /// </summary>
    public static TraversalTransition[] GetOutgoingTransitions(GlobalVoxelIndex sourceVoxelIndex) =>
        QueryTransitionsByKey(_outgoingTransitionIdsByVoxel, sourceVoxelIndex);

    /// <summary>
    /// Returns the currently active transitions whose authored destination anchor resolves to the provided voxel.
    /// </summary>
    public static TraversalTransition[] GetIncomingTransitions(GlobalVoxelIndex destinationVoxelIndex) =>
        QueryTransitionsByKey(_incomingTransitionIdsByVoxel, destinationVoxelIndex);

    /// <summary>
    /// Resolves the world position to a voxel and returns outgoing transitions from that voxel.
    /// </summary>
    public static TraversalTransition[] GetOutgoingTransitions(Vector3d sourcePosition)
    {
        if (!TraversalTransitionAnchor.TryResolveVoxelIndex(sourcePosition, out GlobalVoxelIndex sourceVoxelIndex))
            return Array.Empty<TraversalTransition>();

        return GetOutgoingTransitions(sourceVoxelIndex);
    }

    /// <summary>
    /// Resolves the world position to a voxel and returns incoming transitions to that voxel.
    /// </summary>
    public static TraversalTransition[] GetIncomingTransitions(Vector3d destinationPosition)
    {
        if (!TraversalTransitionAnchor.TryResolveVoxelIndex(destinationPosition, out GlobalVoxelIndex destinationVoxelIndex))
            return Array.Empty<TraversalTransition>();

        return GetIncomingTransitions(destinationVoxelIndex);
    }

    internal static void Reset()
    {
        _transitionLock.EnterWriteLock();
        try
        {
            _transitions.Clear();
            _activeTransitionIds.Clear();
            _suppressedManagedTransitionIds.Clear();
            _managedManualTransitionIdsByVoxel.Clear();
            _outgoingTransitionIdsByVoxel.Clear();
            _incomingTransitionIdsByVoxel.Clear();
            _transitionIdsBySourceGrid.Clear();
            _transitionIdsByDestinationGrid.Clear();
            _registrationOrder = 0;
            _allTransitionsSnapshot = Array.Empty<TraversalTransition>();
            Interlocked.Increment(ref _registryVersion);
        }
        finally { _transitionLock.ExitWriteLock(); }
    }

    internal static TraversalTransition[] GetActiveTransitions()
    {
        _transitionLock.EnterReadLock();
        try
        {
            return _allTransitionsSnapshot;
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    internal static TraversalTransition[] GetActiveTransitionsTouchingGrid(int gridIndex)
    {
        _transitionLock.EnterReadLock();
        try
        {
            bool hasSource = _transitionIdsBySourceGrid.TryGetValue(gridIndex, out SwiftHashSet<string> sourceIds);
            bool hasDestination = _transitionIdsByDestinationGrid.TryGetValue(gridIndex, out SwiftHashSet<string> destinationIds);
            if (!hasSource && !hasDestination)
                return Array.Empty<TraversalTransition>();

            int capacity = (hasSource ? sourceIds.Count : 0) + (hasDestination ? destinationIds.Count : 0);
            SwiftList<TraversalTransition> result = new(capacity);
            SwiftHashSet<string> seenIds = new();

            if (hasSource)
                AppendTransitions(result, seenIds, sourceIds);

            if (hasDestination)
                AppendTransitions(result, seenIds, destinationIds);

            return BuildSortedSnapshot(result);
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    private static TraversalTransition[] QueryTransitionsByKey<TKey>(
        SwiftDictionary<TKey, SwiftHashSet<string>> index,
        TKey key)
    {
        _transitionLock.EnterReadLock();
        try
        {
            if (!index.TryGetValue(key, out SwiftHashSet<string> transitionIds))
                return Array.Empty<TraversalTransition>();

            SwiftList<TraversalTransition> result = new(transitionIds.Count);
            AppendTransitions(result, transitionIds);
            return BuildSortedSnapshot(result);
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    private static void AppendTransitions(
        SwiftList<TraversalTransition> destination,
        SwiftHashSet<string> transitionIds)
    {
        foreach (string transitionId in transitionIds)
        {
            if (_transitions.TryGetValue(transitionId, out RegisteredTraversalTransition registered))
                destination.Add(registered.Transition);
        }
    }

    private static void AppendTransitions(
        SwiftList<TraversalTransition> destination,
        SwiftHashSet<string> seenIds,
        SwiftHashSet<string> transitionIds)
    {
        foreach (string transitionId in transitionIds)
        {
            if (!seenIds.Add(transitionId))
                continue;

            if (_transitions.TryGetValue(transitionId, out RegisteredTraversalTransition registered))
                destination.Add(registered.Transition);
        }
    }

    private static void AddIndexValue<TKey>(
        SwiftDictionary<TKey, SwiftHashSet<string>> index,
        TKey key,
        string transitionId)
    {
        if (!index.TryGetValue(key, out SwiftHashSet<string> transitionIds))
        {
            transitionIds = new SwiftHashSet<string>();
            index.Add(key, transitionIds);
        }

        transitionIds.Add(transitionId);
    }

    private static TraversalTransition[] BuildSortedSnapshot(SwiftList<TraversalTransition> transitions)
    {
        TraversalTransition[] snapshot = transitions.ToArray();
        TraversalTransitionOrdering.Sort(snapshot);
        return snapshot;
    }

    private static bool CheckDuplicateManualTransition_NoLock(RegisteredTraversalTransition duplicate)
    {
        foreach (RegisteredTraversalTransition registered in _transitions.Values)
        {
            if (IsManualOwnershipKind(registered.OwnershipKind)
                && registered.Equals(duplicate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldPromote(
        RegisteredTraversalTransition candidate,
        RegisteredTraversalTransition current)
    {
        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;

        return candidate.RegistrationOrder < current.RegistrationOrder;
    }

    private static void RebuildActiveState_NoLock()
    {
        _activeTransitionIds.Clear();
        _outgoingTransitionIdsByVoxel.Clear();
        _incomingTransitionIdsByVoxel.Clear();
        _transitionIdsBySourceGrid.Clear();
        _transitionIdsByDestinationGrid.Clear();

        if (_transitions.Count == 0)
        {
            _allTransitionsSnapshot = Array.Empty<TraversalTransition>();
            return;
        }

        SwiftHashSet<RegisteredTraversalTransition> activeByIdentity = new(_transitions.Count);
        foreach (RegisteredTraversalTransition registered in _transitions.Values)
        {
            if (IsManagedOwnershipKind(registered.OwnershipKind)
                && _suppressedManagedTransitionIds.Contains(registered.Transition.Id))
            {
                continue;
            }

            if (!activeByIdentity.TryGetValue(registered, out RegisteredTraversalTransition current))
                activeByIdentity.Add(registered);
            else if (ShouldPromote(registered, current))
            {
                activeByIdentity.Remove(current);
                activeByIdentity.Add(registered);
            }
        }

        SwiftList<TraversalTransition> activeTransitions = new(activeByIdentity.Count);
        foreach (RegisteredTraversalTransition registered in activeByIdentity)
        {
            _activeTransitionIds.Add(registered.Transition.Id);
            AddIndexValue(_outgoingTransitionIdsByVoxel, registered.SourceVoxelIndex, registered.Transition.Id);
            AddIndexValue(_incomingTransitionIdsByVoxel, registered.DestinationVoxelIndex, registered.Transition.Id);
            AddIndexValue(_transitionIdsBySourceGrid, registered.SourceVoxelIndex.GridIndex, registered.Transition.Id);
            AddIndexValue(_transitionIdsByDestinationGrid, registered.DestinationVoxelIndex.GridIndex, registered.Transition.Id);
            activeTransitions.Add(registered.Transition);
        }

        _allTransitionsSnapshot = BuildSortedSnapshot(activeTransitions);
    }

    private static bool TryResolveAnchorVoxelIndex(
        TraversalTransitionAnchor anchor,
        out GlobalVoxelIndex voxelIndex)
    {
        voxelIndex = anchor.VoxelIndex;
        if (!GlobalGridManager.TryGetGridAndVoxel(voxelIndex, out _, out _))
            return false;

        if (!anchor.HasPointOverride)
            return true;

        return TraversalTransitionAnchor.TryResolveVoxelIndex(
                anchor.PointOverride,
                out GlobalVoxelIndex pointOverrideVoxelIndex)
            && pointOverrideVoxelIndex == voxelIndex;
    }

    internal static bool IsSuppressed(string id)
    {
        _transitionLock.EnterReadLock();
        try { return _suppressedManagedTransitionIds.Contains(id); }
        finally { _transitionLock.ExitReadLock(); }
    }

    internal static void RefreshManagedManualTransitions()
    {
        _transitionLock.EnterWriteLock();
        try
        {
            bool changed = false;
            foreach (RegisteredTraversalTransition registered in _transitions.Values)
            {
                if (registered.OwnershipKind != TraversalTransitionOwnershipKind.ManagedManual)
                    continue;

                changed |= SetManagedTransitionSuppressedState_NoLock(
                    registered.Transition.Id,
                    suppress: !IsManagedManualTransitionCurrentlyActive(registered));
            }

            if (!changed)
                return;

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
        }
        finally
        {
            _transitionLock.ExitWriteLock();
        }
    }

    internal static void RefreshManagedManualTransitionsForVoxel(GlobalVoxelIndex voxelIndex)
    {
        _transitionLock.EnterWriteLock();
        try
        {
            if (!_managedManualTransitionIdsByVoxel.TryGetValue(voxelIndex, out SwiftHashSet<string> transitionIds))
                return;

            bool changed = false;
            foreach (string transitionId in transitionIds)
            {
                if (!_transitions.TryGetValue(transitionId, out RegisteredTraversalTransition registered)
                    || registered.OwnershipKind != TraversalTransitionOwnershipKind.ManagedManual)
                {
                    continue;
                }

                changed |= SetManagedTransitionSuppressedState_NoLock(
                    transitionId,
                    suppress: !IsManagedManualTransitionCurrentlyActive(registered));
            }

            if (!changed)
                return;

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
        }
        finally
        {
            _transitionLock.ExitWriteLock();
        }
    }

    internal static void SetManagedTransitionsSuppressed(string[] ids, bool suppressed, int count = -1)
    {
        if (ids == null || ids.Length == 0)
            return;

        int changeCount = count < 0 || count > ids.Length
            ? ids.Length
            : count;
        if (changeCount == 0)
            return;

        _transitionLock.EnterWriteLock();
        try
        {
            bool changed = false;
            for (int i = 0; i < changeCount; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id)
                    || !_transitions.TryGetValue(id, out RegisteredTraversalTransition registered)
                    || !IsManagedOwnershipKind(registered.OwnershipKind))
                {
                    continue;
                }

                changed |= suppressed
                    ? _suppressedManagedTransitionIds.Add(id)
                    : _suppressedManagedTransitionIds.Remove(id);
            }

            if (!changed)
                return;

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
        }
        finally
        {
            _transitionLock.ExitWriteLock();
        }
    }

    private static void RollbackRegisteredTransitions_NoLock(string[] ids, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string id = ids[i];
            if (string.IsNullOrEmpty(id))
                continue;

            if (_transitions.TryGetValue(id, out RegisteredTraversalTransition registered))
                RemoveManagedManualDependencyIndexes_NoLock(registered);

            _transitions.Remove(id);
            _suppressedManagedTransitionIds.Remove(id);
        }
    }

    private static void AddManagedManualDependencyIndexes_NoLock(RegisteredTraversalTransition registered)
    {
        if (registered.OwnershipKind != TraversalTransitionOwnershipKind.ManagedManual)
            return;

        AddIndexValue(_managedManualTransitionIdsByVoxel, registered.SourceVoxelIndex, registered.Transition.Id);
        if (!registered.SourceVoxelIndex.Equals(registered.DestinationVoxelIndex))
            AddIndexValue(_managedManualTransitionIdsByVoxel, registered.DestinationVoxelIndex, registered.Transition.Id);
    }

    private static void RemoveManagedManualDependencyIndexes_NoLock(RegisteredTraversalTransition registered)
    {
        if (registered.OwnershipKind != TraversalTransitionOwnershipKind.ManagedManual)
            return;

        RemoveIndexValue(_managedManualTransitionIdsByVoxel, registered.SourceVoxelIndex, registered.Transition.Id);
        if (!registered.SourceVoxelIndex.Equals(registered.DestinationVoxelIndex))
            RemoveIndexValue(_managedManualTransitionIdsByVoxel, registered.DestinationVoxelIndex, registered.Transition.Id);
    }

    private static void RemoveIndexValue<TKey>(
        SwiftDictionary<TKey, SwiftHashSet<string>> index,
        TKey key,
        string transitionId)
    {
        if (!index.TryGetValue(key, out SwiftHashSet<string> transitionIds))
            return;

        transitionIds.Remove(transitionId);
        if (transitionIds.Count == 0)
            index.Remove(key);
    }

    private static bool SetManagedTransitionSuppressedState_NoLock(string id, bool suppress)
    {
        return suppress
            ? _suppressedManagedTransitionIds.Add(id)
            : _suppressedManagedTransitionIds.Remove(id);
    }

    private static bool IsManagedManualTransitionCurrentlyActive(RegisteredTraversalTransition registered)
    {
        return DoesResolvedEndpointSupportMedium(registered.SourceVoxelIndex, registered.Transition.Source.Medium)
            && DoesResolvedEndpointSupportMedium(registered.DestinationVoxelIndex, registered.Transition.Destination.Medium);
    }

    private static bool DoesResolvedEndpointSupportMedium(GlobalVoxelIndex voxelIndex, TraversalMedium medium)
    {
        if (!GlobalGridManager.TryGetGridAndVoxel(voxelIndex, out _, out Voxel voxel))
            return false;

        return medium switch
        {
            TraversalMedium.Solid => voxel.TryGetPartition(out SolidChartPartition _),
            TraversalMedium.Gas => VolumeMediumRules.Matches(voxel, TraversalMedium.Gas),
            TraversalMedium.Liquid => VolumeMediumRules.Matches(voxel, TraversalMedium.Liquid),
            _ => false
        };
    }

    private static bool IsManagedOwnershipKind(TraversalTransitionOwnershipKind ownershipKind) =>
        ownershipKind == TraversalTransitionOwnershipKind.ManagedManual
        || ownershipKind == TraversalTransitionOwnershipKind.ManagedGenerated;

    private static bool IsManualOwnershipKind(TraversalTransitionOwnershipKind ownershipKind) =>
        ownershipKind == TraversalTransitionOwnershipKind.ManagedManual;
}
