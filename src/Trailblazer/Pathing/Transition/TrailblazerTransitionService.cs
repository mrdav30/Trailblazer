using FixedMathSharp;
using GridForge.Spatial;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned API for traversal transition registration and deterministic query snapshots.
/// </summary>
public sealed class TrailblazerTransitionService
{
    private readonly TrailblazerWorldContext _context;
    private readonly PathingWorldState _state;

    internal TrailblazerTransitionService(TrailblazerWorldContext context, PathingWorldState state)
    {
        _context = context;
        _state = state;
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.RegistryVersion"/>
    public int RegistryVersion
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return TraversalTransitionRegistry.RegistryVersion;
        }
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.AllTransitions"/>
    public TraversalTransition[] AllTransitions
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return TraversalTransitionRegistry.AllTransitions;
        }
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.Register(TraversalTransition,int)"/>
    public bool Register(TraversalTransition transition, int priority = TraversalTransitionRegistry.DefaultManualPriority)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.Register(transition, priority);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.IsRegistered(string)"/>
    public bool IsRegistered(string id)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.IsRegistered(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.IsActive(string)"/>
    public bool IsActive(string id)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.IsActive(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.TryGet(string,out TraversalTransition)"/>
    public bool TryGet(string id, out TraversalTransition transition)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.TryGet(id, out transition);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.TryGetResolvedEndpoints(string,out WorldVoxelIndex,out WorldVoxelIndex)"/>
    public bool TryGetResolvedEndpoints(
        string id,
        out WorldVoxelIndex sourceVoxelIndex,
        out WorldVoxelIndex destinationVoxelIndex)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
        {
            return TraversalTransitionRegistry.TryGetResolvedEndpoints(
                id,
                out sourceVoxelIndex,
                out destinationVoxelIndex);
        }
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.Unregister(string)"/>
    public bool Unregister(string id)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.Unregister(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetOutgoingTransitions(WorldVoxelIndex)"/>
    public TraversalTransition[] GetOutgoingTransitions(WorldVoxelIndex sourceVoxelIndex)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.GetOutgoingTransitions(sourceVoxelIndex);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d)"/>
    public TraversalTransition[] GetOutgoingTransitions(Vector3d sourcePosition)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.GetOutgoingTransitions(sourcePosition);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetIncomingTransitions(WorldVoxelIndex)"/>
    public TraversalTransition[] GetIncomingTransitions(WorldVoxelIndex destinationVoxelIndex)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.GetIncomingTransitions(destinationVoxelIndex);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetIncomingTransitions(Vector3d)"/>
    public TraversalTransition[] GetIncomingTransitions(Vector3d destinationPosition)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionRegistry.GetIncomingTransitions(destinationPosition);
    }

    internal TraversalTransition[] GetDirectedTransitionsFromSourceGrid(int sourceGridIndex)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceGridIndex);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerTransitionService is bound to an inactive GridWorld.");
    }
}
