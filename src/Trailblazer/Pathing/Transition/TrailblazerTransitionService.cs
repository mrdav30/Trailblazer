//=======================================================================
// TrailblazerTransitionService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Spatial;

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
            using (EnterUsableState())
                return TraversalTransitionRegistry.RegistryVersion;
        }
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.AllTransitions"/>
    public TraversalTransition[] AllTransitions
    {
        get
        {
            using (EnterUsableState())
                return TraversalTransitionRegistry.AllTransitions;
        }
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.Register(TraversalTransition,int)"/>
    public bool Register(TraversalTransition transition, int priority = TraversalTransitionRegistry.DefaultManualPriority)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.Register(transition, priority);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.IsRegistered(string)"/>
    public bool IsRegistered(string id)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.IsRegistered(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.IsActive(string)"/>
    public bool IsActive(string id)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.IsActive(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.TryGet(string,out TraversalTransition)"/>
    public bool TryGet(string id, out TraversalTransition transition)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.TryGet(id, out transition);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.TryGetResolvedEndpoints(string,out WorldVoxelIndex,out WorldVoxelIndex)"/>
    public bool TryGetResolvedEndpoints(
        string id,
        out WorldVoxelIndex sourceVoxelIndex,
        out WorldVoxelIndex destinationVoxelIndex)
    {
        using (EnterUsableState())
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
        using (EnterUsableState())
            return TraversalTransitionRegistry.Unregister(id);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetOutgoingTransitions(WorldVoxelIndex)"/>
    public TraversalTransition[] GetOutgoingTransitions(WorldVoxelIndex sourceVoxelIndex)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.GetOutgoingTransitions(sourceVoxelIndex);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d)"/>
    public TraversalTransition[] GetOutgoingTransitions(Vector3d sourcePosition)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.GetOutgoingTransitions(sourcePosition);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetIncomingTransitions(WorldVoxelIndex)"/>
    public TraversalTransition[] GetIncomingTransitions(WorldVoxelIndex destinationVoxelIndex)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.GetIncomingTransitions(destinationVoxelIndex);
    }

    /// <inheritdoc cref="TraversalTransitionRegistry.GetIncomingTransitions(Vector3d)"/>
    public TraversalTransition[] GetIncomingTransitions(Vector3d destinationPosition)
    {
        using (EnterUsableState())
            return TraversalTransitionRegistry.GetIncomingTransitions(destinationPosition);
    }

    internal TraversalTransition[] GetDirectedTransitionsFromSourceGrid(int sourceGridIndex)
    {
        using (EnterUsableState())
            return TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceGridIndex);
    }

    internal TraversalTransition[] GetDirectedTransitions(
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        using (EnterUsableState())
            return TraversalTransitionQuery.GetDirectedTransitions(sourceMedium, destinationMedium);
    }

    internal TraversalTransition[] GetDirectedTransitionsToDestinationGrid(
        int destinationGridIndex,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        using (EnterUsableState())
        {
            return TraversalTransitionQuery.GetDirectedTransitionsToDestinationGrid(
                destinationGridIndex,
                sourceMedium,
                destinationMedium);
        }
    }

    private IDisposable EnterUsableState()
    {
        EnsureUsable();
        return PathManager.EnterState(_state);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerTransitionService is bound to an inactive GridWorld.");
    }
}
