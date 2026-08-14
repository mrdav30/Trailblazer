//=======================================================================
// NavigationAutomaticSeamLifecycleWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Retains one fail-closed GridForge lifecycle prefix until seam state is atomic.</summary>
internal sealed class NavigationAutomaticSeamLifecycleWork
{
    internal const long BaseRetainedBytes = 48L;

    private readonly NavigationWorldGraph _source;
    private readonly NavigationAutomaticSeamRefreshWork _refresh;
    private readonly NavigationCompositionWorkspace _workspace;
    private NavigationCompositionIndex.UpdateWork? _update;

    internal NavigationAutomaticSeamLifecycleWork(
        GridWorld world,
        NavigationWorldGraph source,
        GridEventInfo[] events,
        int eventCount,
        NavigationCompositionWorkspace workspace,
        bool fullRebuild = false)
    {
        _source = source;
        _workspace = workspace;
        _refresh = new NavigationAutomaticSeamRefreshWork(
            world,
            source,
            source,
            events,
            eventCount,
            fullRebuild);
    }

    internal bool IsComplete => _refresh.IsComplete
        && (!_refresh.StructuralLinksChanged || (_update?.IsComplete ?? false));

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _refresh.RetainedBytes
        + GetUpdateAdditionalRetainedBytes());

    internal int PersistentPageCount => checked(
        1
        + _refresh.PersistentPageCount
        + GetUpdateAdditionalPersistentPages());

    internal NavigationWorldGraph Result
    {
        get
        {
            NavigationWorldGraph next = _source
                .WithAutomaticSeams(_refresh.Result)
                .ReopenStructuralScopes(_source.GraphVersion + 1);
            NavigationCompositionIndex composition = _update?.Result
                ?? _source.Composition.WithVersion(next.GraphVersion);
            return next.WithComposition(composition);
        }
    }

    internal bool RevalidateForPublication() => _refresh.RevalidateForPublication();

    internal AdvanceStatus AdvanceOne(
        MaintenanceWorkMeter meter,
        long maximumRetainedBytes,
        int maximumPersistentPages)
    {
        if (!_refresh.IsComplete)
        {
            long revision = _refresh.Revision;
            NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus status =
                _refresh.AdvanceOne(meter);
            if (_refresh.Revision != revision)
                return AdvanceStatus.RestartRequired;
            if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
                return AdvanceStatus.CapacityExceeded;
            return status switch
            {
                NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus.Blocked =>
                    AdvanceStatus.Blocked,
                NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus.Complete =>
                    AdvanceStatus.Progressed,
                _ => AdvanceStatus.Progressed
            };
        }

        if (!_refresh.StructuralLinksChanged)
            return AdvanceStatus.Complete;
        if (_update == null)
        {
            NavigationWorldGraph next = _source
                .WithAutomaticSeams(_refresh.Result)
                .ReopenStructuralScopes(_source.GraphVersion + 1);
            _update = next.BeginCompositionUpdate(
                _source,
                _refresh.ChangedMapIds,
                _source.GraphVersion + 1,
                _source.GraphVersion + 1,
                _workspace);
            return ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages)
                ? AdvanceStatus.CapacityExceeded
                : AdvanceStatus.Progressed;
        }

        int before = GetConsumedWork(meter);
        bool complete = _update.Advance(meter);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
            return AdvanceStatus.CapacityExceeded;
        if (complete)
            return AdvanceStatus.Complete;
        return GetConsumedWork(meter) != before
            ? AdvanceStatus.Progressed
            : AdvanceStatus.Blocked;
    }

    private bool ExceedsCapacity(long maximumRetainedBytes, int maximumPersistentPages) =>
        RetainedBytes > maximumRetainedBytes
        || PersistentPageCount > maximumPersistentPages;

    private long GetUpdateAdditionalRetainedBytes()
    {
        if (_update == null)
            return 0;
        return checked(
            System.Math.Max(
                0L,
                _update.NonPayloadRetainedBytes
                    - _source.Composition.RootAndValueRetainedBytes)
            + _update.PayloadAdditionalRetainedBytes);
    }

    private int GetUpdateAdditionalPersistentPages()
    {
        if (_update == null)
            return 0;
        return checked(
            System.Math.Max(
                0,
                _update.NonPayloadPersistentPageCount
                    - _source.Composition.PersistentPageCount)
            + _update.PayloadAdditionalPersistentPages);
    }

    private static int GetConsumedWork(MaintenanceWorkMeter meter) => checked(
        meter.ConsumedEnvelopes
        + meter.BaselineAddresses
        + meter.OverlaySlots
        + meter.ComponentNodes
        + meter.SeamCandidateProbes
        + meter.ExplicitEdges
        + meter.DependencyEntries);

    internal enum AdvanceStatus : byte
    {
        Blocked,
        Progressed,
        Complete,
        RestartRequired,
        CapacityExceeded
    }
}
