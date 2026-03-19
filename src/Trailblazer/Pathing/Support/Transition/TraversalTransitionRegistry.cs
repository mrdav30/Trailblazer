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
    private readonly struct RegisteredTraversalTransition
    {
        public RegisteredTraversalTransition(
            TraversalTransition transition,
            GlobalVoxelIndex sourceVoxelIndex,
            GlobalVoxelIndex destinationVoxelIndex)
        {
            Transition = transition;
            SourceVoxelIndex = sourceVoxelIndex;
            DestinationVoxelIndex = destinationVoxelIndex;
        }

        public TraversalTransition Transition { get; }

        public GlobalVoxelIndex SourceVoxelIndex { get; }

        public GlobalVoxelIndex DestinationVoxelIndex { get; }
    }

    private static readonly SwiftDictionary<string, RegisteredTraversalTransition> _transitions =
        new(8, StringComparer.Ordinal);

    private static readonly ReaderWriterLockSlim _transitionLock = new();

    /// <summary>
    /// Returns a snapshot of all currently registered transitions.
    /// </summary>
    public static TraversalTransition[] AllTransitions
    {
        get
        {
            _transitionLock.EnterReadLock();
            try
            {
                SwiftList<TraversalTransition> snapshot = new(_transitions.Count);
                foreach (RegisteredTraversalTransition registered in _transitions.Values)
                    snapshot.Add(registered.Transition);

                return snapshot.ToArray();
            }
            finally
            {
                _transitionLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Registers a traversal transition and resolves both endpoints against the active voxel grid.
    /// </summary>
    /// <returns>True when the transition is registered; false when the id already exists or either endpoint has no voxel.</returns>
    public static bool Register(TraversalTransition transition)
    {
        if (!TryResolveVoxelIndex(transition.Source.Position, out GlobalVoxelIndex sourceVoxelIndex)
            || !TryResolveVoxelIndex(transition.Destination.Position, out GlobalVoxelIndex destinationVoxelIndex))
        {
            return false;
        }

        _transitionLock.EnterWriteLock();
        try
        {
            if (_transitions.ContainsKey(transition.Id))
                return false;

            _transitions.Add(
                transition.Id,
                new RegisteredTraversalTransition(transition, sourceVoxelIndex, destinationVoxelIndex));
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
        try { return _transitions.Remove(id); }
        finally { _transitionLock.ExitWriteLock(); }
    }

    /// <summary>
    /// Returns the transitions whose authored source anchor resolves to the provided voxel.
    /// </summary>
    public static TraversalTransition[] GetOutgoingTransitions(GlobalVoxelIndex sourceVoxelIndex) =>
        QueryTransitions(registered => registered.SourceVoxelIndex.Equals(sourceVoxelIndex));

    /// <summary>
    /// Returns the transitions whose authored destination anchor resolves to the provided voxel.
    /// </summary>
    public static TraversalTransition[] GetIncomingTransitions(GlobalVoxelIndex destinationVoxelIndex) =>
        QueryTransitions(registered => registered.DestinationVoxelIndex.Equals(destinationVoxelIndex));

    /// <summary>
    /// Resolves the world position to a voxel and returns outgoing transitions from that voxel.
    /// </summary>
    public static TraversalTransition[] GetOutgoingTransitions(Vector3d sourcePosition)
    {
        if (!TryResolveVoxelIndex(sourcePosition, out GlobalVoxelIndex sourceVoxelIndex))
            return Array.Empty<TraversalTransition>();

        return GetOutgoingTransitions(sourceVoxelIndex);
    }

    /// <summary>
    /// Resolves the world position to a voxel and returns incoming transitions to that voxel.
    /// </summary>
    public static TraversalTransition[] GetIncomingTransitions(Vector3d destinationPosition)
    {
        if (!TryResolveVoxelIndex(destinationPosition, out GlobalVoxelIndex destinationVoxelIndex))
            return Array.Empty<TraversalTransition>();

        return GetIncomingTransitions(destinationVoxelIndex);
    }

    internal static void Reset()
    {
        _transitionLock.EnterWriteLock();
        try { _transitions.Clear(); }
        finally { _transitionLock.ExitWriteLock(); }
    }

    private static TraversalTransition[] QueryTransitions(Func<RegisteredTraversalTransition, bool> predicate)
    {
        _transitionLock.EnterReadLock();
        try
        {
            SwiftList<TraversalTransition> result = new(_transitions.Count);
            foreach (RegisteredTraversalTransition registered in _transitions.Values)
            {
                if (predicate(registered))
                    result.Add(registered.Transition);
            }

            return result.ToArray();
        }
        finally
        {
            _transitionLock.ExitReadLock();
        }
    }

    private static bool TryResolveVoxelIndex(Vector3d position, out GlobalVoxelIndex voxelIndex)
    {
        if (GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
        {
            voxelIndex = voxel.GlobalIndex;
            return true;
        }

        voxelIndex = default;
        return false;
    }
}
