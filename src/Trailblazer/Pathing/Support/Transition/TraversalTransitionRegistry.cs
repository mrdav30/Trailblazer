using System;
using System.Threading;
using FixedMathSharp;
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

    private static readonly SwiftDictionary<GlobalVoxelIndex, SwiftHashSet<string>> _outgoingTransitionIdsByVoxel = new();

    private static readonly SwiftDictionary<GlobalVoxelIndex, SwiftHashSet<string>> _incomingTransitionIdsByVoxel = new();

    private static readonly SwiftDictionary<int, SwiftHashSet<string>> _transitionIdsBySourceGrid = new();

    private static readonly SwiftDictionary<int, SwiftHashSet<string>> _transitionIdsByDestinationGrid = new();

    private static readonly ReaderWriterLockSlim _transitionLock = new();

    private static int _registryVersion;

    private static int _allTransitionsSnapshotVersion = -1;

    private static TraversalTransition[] _allTransitionsSnapshot = Array.Empty<TraversalTransition>();

    /// <summary>
    /// Monotonic version used to invalidate cache keys when transition topology changes.
    /// </summary>
    public static int RegistryVersion => _registryVersion;

    /// <summary>
    /// Returns a snapshot of all currently registered transitions.
    /// </summary>
    public static TraversalTransition[] AllTransitions
    {
        get
        {
            _transitionLock.EnterUpgradeableReadLock();
            try
            {
                int registryVersion = _registryVersion;
                if (_allTransitionsSnapshotVersion != registryVersion)
                {
                    _transitionLock.EnterWriteLock();
                    try
                    {
                        if (_allTransitionsSnapshotVersion != _registryVersion)
                        {
                            _allTransitionsSnapshot = BuildTransitionSnapshot();
                            _allTransitionsSnapshotVersion = _registryVersion;
                        }
                    }
                    finally
                    {
                        _transitionLock.ExitWriteLock();
                    }
                }

                TraversalTransition[] snapshot = _allTransitionsSnapshot;
                if (snapshot.Length == 0)
                    return Array.Empty<TraversalTransition>();

                var copy = new TraversalTransition[snapshot.Length];
                Array.Copy(snapshot, copy, snapshot.Length);
                return copy;
            }
            finally
            {
                _transitionLock.ExitUpgradeableReadLock();
            }
        }
    }

    /// <summary>
    /// Registers a traversal transition and resolves both endpoints against the active voxel grid.
    /// </summary>
    /// <returns>True when the transition is registered; false when the id already exists or either endpoint has no voxel.</returns>
    public static bool Register(TraversalTransition transition)
    {
        if (!TryResolveAnchorVoxelIndex(transition.Source, out GlobalVoxelIndex sourceVoxelIndex)
            || !TryResolveAnchorVoxelIndex(transition.Destination, out GlobalVoxelIndex destinationVoxelIndex))
        {
            return false;
        }

        _transitionLock.EnterWriteLock();
        try
        {
            if (_transitions.ContainsKey(transition.Id))
                return false;

            var registered = new RegisteredTraversalTransition(transition, sourceVoxelIndex, destinationVoxelIndex);
            _transitions.Add(transition.Id, registered);
            IndexTransition(registered);
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
    /// Attempts to retrieve a registered transition by id.
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
    /// Attempts to retrieve the voxel indices resolved for a registered transition.
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

            UnindexTransition(registered);
            if (!_transitions.Remove(id))
                return false;

            Interlocked.Increment(ref _registryVersion);
            return true;
        }
        finally { _transitionLock.ExitWriteLock(); }
    }

    /// <summary>
    /// Returns the transitions whose authored source anchor resolves to the provided voxel.
    /// </summary>
    public static TraversalTransition[] GetOutgoingTransitions(GlobalVoxelIndex sourceVoxelIndex) =>
        QueryTransitionsByKey(_outgoingTransitionIdsByVoxel, sourceVoxelIndex);

    /// <summary>
    /// Returns the transitions whose authored destination anchor resolves to the provided voxel.
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
            _outgoingTransitionIdsByVoxel.Clear();
            _incomingTransitionIdsByVoxel.Clear();
            _transitionIdsBySourceGrid.Clear();
            _transitionIdsByDestinationGrid.Clear();
            Interlocked.Increment(ref _registryVersion);
        }
        finally { _transitionLock.ExitWriteLock(); }
    }

    internal static RegisteredTraversalTransition[] GetRegisteredTransitions()
    {
        _transitionLock.EnterReadLock();
        try
        {
            SwiftList<RegisteredTraversalTransition> result = new(_transitions.Count);
            foreach (RegisteredTraversalTransition registered in _transitions.Values)
                result.Add(registered);

            return result.ToArray();
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    internal static RegisteredTraversalTransition[] GetRegisteredTransitionsTouchingGrid(int gridIndex)
    {
        _transitionLock.EnterReadLock();
        try
        {
            bool hasSource = _transitionIdsBySourceGrid.TryGetValue(gridIndex, out SwiftHashSet<string> sourceIds);
            bool hasDestination = _transitionIdsByDestinationGrid.TryGetValue(gridIndex, out SwiftHashSet<string> destinationIds);
            if (!hasSource && !hasDestination)
                return Array.Empty<RegisteredTraversalTransition>();

            int capacity = (hasSource ? sourceIds.Count : 0) + (hasDestination ? destinationIds.Count : 0);
            SwiftList<RegisteredTraversalTransition> result = new(capacity);
            SwiftHashSet<string> seenIds = new();

            if (hasSource)
                AppendRegisteredTransitions(result, seenIds, sourceIds);

            if (hasDestination)
                AppendRegisteredTransitions(result, seenIds, destinationIds);

            return result.ToArray();
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
            return result.ToArray();
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    private static void IndexTransition(RegisteredTraversalTransition registered)
    {
        AddIndexValue(_outgoingTransitionIdsByVoxel, registered.SourceVoxelIndex, registered.Transition.Id);
        AddIndexValue(_incomingTransitionIdsByVoxel, registered.DestinationVoxelIndex, registered.Transition.Id);
        AddIndexValue(_transitionIdsBySourceGrid, registered.SourceVoxelIndex.GridIndex, registered.Transition.Id);
        AddIndexValue(_transitionIdsByDestinationGrid, registered.DestinationVoxelIndex.GridIndex, registered.Transition.Id);
    }

    private static void UnindexTransition(RegisteredTraversalTransition registered)
    {
        RemoveIndexValue(_outgoingTransitionIdsByVoxel, registered.SourceVoxelIndex, registered.Transition.Id);
        RemoveIndexValue(_incomingTransitionIdsByVoxel, registered.DestinationVoxelIndex, registered.Transition.Id);
        RemoveIndexValue(_transitionIdsBySourceGrid, registered.SourceVoxelIndex.GridIndex, registered.Transition.Id);
        RemoveIndexValue(_transitionIdsByDestinationGrid, registered.DestinationVoxelIndex.GridIndex, registered.Transition.Id);
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

    private static void AppendRegisteredTransitions(
        SwiftList<RegisteredTraversalTransition> destination,
        SwiftHashSet<string> seenIds,
        SwiftHashSet<string> transitionIds)
    {
        foreach (string transitionId in transitionIds)
        {
            if (!seenIds.Add(transitionId))
                continue;

            if (_transitions.TryGetValue(transitionId, out RegisteredTraversalTransition registered))
                destination.Add(registered);
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

    private static TraversalTransition[] BuildTransitionSnapshot()
    {
        SwiftList<TraversalTransition> snapshot = new(_transitions.Count);
        foreach (RegisteredTraversalTransition registered in _transitions.Values)
            snapshot.Add(registered.Transition);

        return snapshot.ToArray();
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
