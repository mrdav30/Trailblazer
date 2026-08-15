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
    internal const long BaseRetainedBytes = 80L;

    private readonly NavigationWorldGraph _source;
    private readonly NavigationAutomaticSeamRefreshWork _refresh;
    private NavigationSurfaceComponentKeySet _affectedComponents =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationCellAddressSet _affectedAddresses = NavigationCellAddressSet.Empty;
    private NavigationWorldGraph? _structuralGraph;
    private NavigationSurfaceComponentBuildWork? _componentUpdate;
    private int _affectedMemberCount;
    private int _endpointOrdinal;
    private bool _affectedCaptureComplete;

    internal NavigationAutomaticSeamLifecycleWork(
        GridWorld world,
        NavigationWorldGraph source,
        GridEventInfo[] events,
        int eventCount,
        bool fullRebuild = false)
    {
        _source = source;
        _refresh = new NavigationAutomaticSeamRefreshWork(
            world,
            source,
            source,
            events,
            eventCount,
            fullRebuild);
    }

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _refresh.RetainedBytes
        + (ReferenceEquals(_affectedComponents, NavigationSurfaceComponentKeySet.Empty)
            ? 0L
            : _affectedComponents.RetainedBytes)
        + (ReferenceEquals(_affectedAddresses, NavigationCellAddressSet.Empty)
            ? 0L
            : _affectedAddresses.RetainedBytes)
        + (_componentUpdate?.RetainedBytes ?? 0L));

    internal int PersistentPageCount => checked(
        1
        + _refresh.PersistentPageCount
        + (ReferenceEquals(_affectedComponents, NavigationSurfaceComponentKeySet.Empty)
            ? 0
            : _affectedComponents.PersistentPageCount)
        + (ReferenceEquals(_affectedAddresses, NavigationCellAddressSet.Empty)
            ? 0
            : _affectedAddresses.PersistentPageCount)
        + (_componentUpdate?.PersistentPageCount ?? 0));

    internal NavigationWorldGraph Result
    {
        get
        {
            return _structuralGraph!.WithSurfaceComponents(_componentUpdate!.Result);
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

        while (!_affectedCaptureComplete)
        {
            if (_endpointOrdinal >= _refresh.ChangedStructuralEndpointCount)
            {
                _affectedCaptureComplete = true;
                break;
            }
            if (!meter.TryConsumeDependencyEntries(1))
                return AdvanceStatus.Blocked;
            NavigationCellAddress address =
                _refresh.GetChangedStructuralEndpointAt(_endpointOrdinal++);
            _affectedAddresses = _affectedAddresses.Add(address);
            if (_source.TryGetSurfaceComponent(
                    address,
                    out NavigationSurfaceComponentKey key,
                    out _)
                && !_affectedComponents.Contains(key))
            {
                _affectedComponents = _affectedComponents.Add(key);
                _source.SurfaceComponents.TryGet(key, out NavigationSurfaceComponent component);
                _affectedMemberCount = checked(
                    _affectedMemberCount + component.Members.Count);
            }
            if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
                return AdvanceStatus.CapacityExceeded;
        }

        NavigationWorldGraph next = _source
            .WithAutomaticSeams(_refresh.Result)
            .ReopenStructuralScopes(_source.GraphVersion + 1);
        _structuralGraph ??= next;
        _componentUpdate ??= new NavigationSurfaceComponentBuildWork(
            _structuralGraph,
            _source,
            _affectedComponents,
            _affectedAddresses,
            checked(_affectedMemberCount + _affectedAddresses.Count));
        bool complete = _componentUpdate.Advance(meter);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
            return AdvanceStatus.CapacityExceeded;
        return complete ? AdvanceStatus.Complete : AdvanceStatus.Blocked;
    }

    private bool ExceedsCapacity(long maximumRetainedBytes, int maximumPersistentPages) =>
        RetainedBytes > maximumRetainedBytes
        || PersistentPageCount > maximumPersistentPages;

    internal enum AdvanceStatus : byte
    {
        Blocked,
        Progressed,
        Complete,
        RestartRequired,
        CapacityExceeded
    }
}
