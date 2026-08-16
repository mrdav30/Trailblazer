//=======================================================================
// PathingWorldState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

namespace Trailblazer.Pathing;

/// <summary>
/// Owns mutable pathing state for one <see cref="TrailblazerWorldContext"/>.
/// </summary>
internal sealed class PathingWorldState : IDisposable
{
    private bool _disposed;

    internal PathingWorldState(TrailblazerWorldContext context)
    {
        Context = context;
        ExternalGridBridge = new PathingWorldGridBridge(this);
    }

    internal TrailblazerWorldContext Context { get; }

    internal GridWorld World => Context.World;

    internal PathingWorldGridBridge ExternalGridBridge { get; }

    internal SwiftObjectPool<SolidChartPartition> PartitionPool { get; } = new(
        () => new SolidChartPartition(),
        actionOnRelease: partition => partition.Reset());

    internal SwiftObjectPool<VolumeChartPartition> VolumeChartPartitionPool { get; } = new(
        () => new VolumeChartPartition(),
        actionOnRelease: partition => partition.Reset());

    internal SwiftDictionary<string, NavigationChartRegistration> NavigationChartMap { get; } = new();

    internal SwiftDictionary<WorldVoxelIndex, ResolvedChartVoxelState> ResolvedChartVoxelStates { get; } = new();

    internal SwiftDictionary<ushort, SwiftDictionary<string, int>> InitializedChartTouchCountsByGridIndex { get; } = new();

    internal ReaderWriterLockSlim NavigationChartMapLock { get; } = new();

    internal int ActiveAuthoredGasCellCount { get; set; }

    internal int ActiveAuthoredLiquidCellCount { get; set; }

    internal int NextChartRegistrationOrder { get; set; }

    internal TraversalTransitionRegistryState TransitionRegistryState { get; } = new();

    internal TraversalTransitionQueryCache TransitionQueryCache { get; } = new();

    internal VolumeMediumRulesState VolumeRulesState { get; } = new();

    internal TrailblazerGuideState GuideState { get; } = new();

    internal SwiftDictionary<ushort, ExternalGridEventObservation> ExternalGridEventObservationsByGridIndex { get; } = new();

    internal SwiftDictionary<ushort, PendingExternalGridChange> PendingGridChangesByGridIndex { get; } = new();

    internal SwiftList<ushort> PendingGridChangeOrder { get; } = new();

    internal int GridEventsReceived { get; set; }

    internal int GridAddEventsReceived { get; set; }

    internal int GridRemoveEventsReceived { get; set; }

    internal int GridChangeEventsReceived { get; set; }

    internal int DistinctObservedGridSlots { get; set; }

    internal int DuplicateGridEventSignaturesObserved { get; set; }

    internal int DuplicateGridAddEventSignaturesObserved { get; set; }

    internal int DuplicateGridRemoveEventSignaturesObserved { get; set; }

    internal int DuplicateGridChangeEventSignaturesObserved { get; set; }

    internal int MaxGridEventStreak { get; set; }

    internal int GridRebuildPassesExecuted { get; set; }

    internal int GridEventsIgnoredForNoIntersectingCharts { get; set; }

    internal int TotalChartsSelectedForGridRebuild { get; set; }

    internal int MaxChartsSelectedForSingleGridEvent { get; set; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ExternalGridBridge.Dispose();
        GuideState.Dispose();
        TransitionRegistryState.Dispose();
        NavigationChartMapLock.Dispose();
    }
}
