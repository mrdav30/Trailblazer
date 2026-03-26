using System;
using System.Threading;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Global registry for authored traversal transitions.
/// </summary>
/// <remarks>
/// Registration resolves transition endpoints to the current voxel grid, but it does not validate
/// chart ownership or configured volume rules yet. Hosts should unregister or rebuild transitions
/// when topology changes outside of <see cref="PathManager.Reset"/>.
/// </remarks>
public static class TraversalTransitionRegistry
{
    private static readonly SwiftDictionary<string, RegisteredTraversalTransition> _transitions =
        new(8, StringComparer.Ordinal);

    private static readonly SwiftHashSet<string> _activeTransitionIds = new();

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
    /// Registers a manual traversal transition and resolves both endpoints against the active voxel grid.
    /// </summary>
    /// <returns>True when the transition is registered; false when the id already exists or either endpoint has no voxel.</returns>
    public static bool Register(TraversalTransition transition) =>
        RegisterInternal(transition, TraversalTransitionRegistrationSource.Manual);

    internal static bool RegisterGenerated(TraversalTransition transition) =>
        RegisterInternal(transition, TraversalTransitionRegistrationSource.Generated);

    private static bool RegisterInternal(
        TraversalTransition transition,
        TraversalTransitionRegistrationSource registrationSource)
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
                registrationSource,
                ++_registrationOrder);

            if (registrationSource == TraversalTransitionRegistrationSource.Manual
                && CheckDuplicateManualTransition_NoLock(registered))
            {
                GridForgeLogger.Warn(
                    $"Ignored duplicate manual traversal transition '{transition.Id}' because it matches an existing manual transition.");
                return false;
            }

            _transitions.Add(transition.Id, registered);
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

            RebuildActiveState_NoLock();
            Interlocked.Increment(ref _registryVersion);
            return true;
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
            if (registered.RegistrationSource == TraversalTransitionRegistrationSource.Manual
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
        if (candidate.RegistrationSource != current.RegistrationSource)
            return candidate.RegistrationSource == TraversalTransitionRegistrationSource.Manual;

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
            if (!activeByIdentity.TryGetValue(registered, out RegisteredTraversalTransition current))
                activeByIdentity.Add(registered);
            else if (ShouldPromote(registered, current))
            {
                // TODO: this will do until we add an indexer to SwiftHashSet or switch to a different data structure that allows updating values in-place.
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
}
